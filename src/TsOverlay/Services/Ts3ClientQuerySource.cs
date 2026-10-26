using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TsOverlay.Models;

namespace TsOverlay.Services;

/// <summary>
/// Источник данных TeamSpeak 3 через встроенный плагин ClientQuery (TCP 127.0.0.1:25639).
/// Даёт полный список клиентов канала, флаги микрофона/наушников и события «начал/закончил говорить».
/// </summary>
public sealed class Ts3ClientQuerySource : ITeamSpeakSource
{
    private sealed record CommandResult(string Response, int ErrorId, string Message)
    {
        public bool Ok => ErrorId == 0;
    }

    private sealed class AuthException : Exception
    {
        public AuthException(string message) : base(message)
        {
        }
    }

    private readonly string _apiKey;
    private readonly int _port;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<int, TsClientState> _clients = new();
    private readonly Dictionary<int, TsChannelState> _channels = new();
    private readonly StringBuilder _responseBuffer = new();

    private Task? _worker;
    private Task? _readTask;
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private TaskCompletionSource<CommandResult>? _pending;

    private int _ownClientId;
    private int _ownChannelId;
    private int _selectedHandlerId = -1;
    private string _serverName = "";
    private volatile bool _needsRefresh = true;
    private DateTime _lastFullRefresh = DateTime.MinValue;
    private volatile bool _authFailed;
    private string _status = "Подключение к TeamSpeak 3…";

    public Ts3ClientQuerySource(string apiKey, int port)
    {
        _apiKey = apiKey;
        _port = port;
    }

    public TsClientKind Kind => TsClientKind.Ts3;

    public TsSnapshot Current { get; private set; } = new()
    {
        Source = TsClientKind.Ts3,
        Status = "TeamSpeak 3 не запущен",
    };

    public bool IsRunning => _worker is { IsCompleted: false };

    public event Action<TsSnapshot>? SnapshotChanged;

    public event Action<string>? UserNotice;

    public Task StartAsync()
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _worker = Task.Run(WorkerAsync);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        try
        {
            // CancelAsync не блокирует поток на колбэках сокета.
            await _stop.CancelAsync();
            Cleanup();
            if (_worker != null)
            {
                await Task.WhenAny(_worker, Task.Delay(1500));
            }
        }
        catch (Exception ex)
        {
            Log.Error("TS3: ошибка остановки", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
        _commandLock.Dispose();
    }

    private async Task WorkerAsync()
    {
        var ct = _stop.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(ct);
                _authFailed = false;
                _status = "TeamSpeak 3: подключено";
                _needsRefresh = true;
                await RefreshAsync();

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(350, ct);

                    if (_readTask is { IsFaulted: true })
                    {
                        throw new IOException("ClientQuery: обрыв чтения", _readTask.Exception);
                    }

                    var due = _needsRefresh && DateTime.UtcNow - _lastFullRefresh > TimeSpan.FromMilliseconds(600);
                    var periodic = DateTime.UtcNow - _lastFullRefresh > TimeSpan.FromSeconds(20);
                    if (due || periodic)
                    {
                        _needsRefresh = false;
                        await RefreshAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (AuthException ex)
            {
                _authFailed = true;
                _status = "TeamSpeak 3: неверный ключ ClientQuery";
                Log.Error("TS3: " + ex.Message);
                UserNotice?.Invoke("TeamSpeak 3: неверный ключ ClientQuery. Проверьте ключ в настройках оверлея.");
                Publish("Неверный ключ ClientQuery — проверьте настройки");
            }
            catch (Exception ex)
            {
                // Штатная ситуация (клиент ещё не поднял ClientQuery) — без стектрейса в журнале.
                Log.Info("TS3: нет связи с ClientQuery — " + ex.Message);
                Publish(_authFailed ? "Неверный ключ ClientQuery" : "Нет связи с TeamSpeak 3");
            }
            finally
            {
                Cleanup();
            }

            try
            {
                await Task.Delay(_authFailed ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(3), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        Cleanup();
        _selectedHandlerId = -1;
        _lastFullRefresh = DateTime.MinValue;

        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(IPAddress.Loopback, _port, ct);
        var stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Ts3Protocol.WireEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        _writer = new StreamWriter(stream, Ts3Protocol.WireEncoding, bufferSize: 8192, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };

        await ReadGreetingAsync(ct);

        _readTask = Task.Run(() => ReadLoopAsync(ct), ct);

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new AuthException("Ключ ClientQuery не задан");
        }

        var auth = await CommandAsync("auth apikey=" + Ts3Protocol.Escape(_apiKey));
        if (!auth.Ok)
        {
            throw new AuthException($"auth вернул ошибку {auth.ErrorId} ({auth.Message})");
        }

        await CommandAsync("clientnotifyregister schandlerid=0 event=any");
        Log.Info("TS3: ClientQuery подключён");
    }

    private async Task ReadGreetingAsync(CancellationToken ct)
    {
        // Приветствие — несколько строк, заканчивается строкой "selected schandlerid=N".
        // Если строка не пришла (старая версия плагина), просто продолжаем.
        for (var i = 0; i < 12; i++)
        {
            if (_reader == null)
            {
                return;
            }

            var read = _reader.ReadLineAsync(ct).AsTask();
            var completed = await Task.WhenAny(read, Task.Delay(1200, ct));
            if (completed != read)
            {
                return;
            }

            var line = await read;
            if (line == null)
            {
                throw new IOException("ClientQuery закрыл соединение");
            }

            if (line.StartsWith("selected schandlerid=", StringComparison.Ordinal) ||
                line.StartsWith("error id=", StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null)
                {
                    throw new IOException("ClientQuery закрыл соединение");
                }

                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("notify", StringComparison.Ordinal))
                {
                    HandleEvent(line);
                    continue;
                }

                if (line.StartsWith("selected schandlerid=", StringComparison.Ordinal))
                {
                    // Плагин повторяет эту строку часто; перечитываем состояние только при реальной смене.
                    if (int.TryParse(line["selected schandlerid=".Length..].Trim(), out var handlerId) && handlerId != _selectedHandlerId)
                    {
                        _selectedHandlerId = handlerId;
                        _needsRefresh = true;
                    }

                    continue;
                }

                _responseBuffer.Append(line).Append('\n');
                if (line.StartsWith("error id=", StringComparison.Ordinal))
                {
                    var text = _responseBuffer.ToString();
                    _responseBuffer.Clear();
                    var parsed = Ts3Protocol.ParseResponse(text);
                    var pending = _pending;
                    _pending = null;
                    pending?.TrySetResult(new CommandResult(text, parsed.ErrorId, parsed.ErrorMessage));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("TS3: чтение прервано", ex);
            throw;
        }
    }

    private async Task<CommandResult> CommandAsync(string command, int timeoutMs = 6000)
    {
        var writer = _writer ?? throw new IOException("Нет соединения с ClientQuery");
        var acquired = await _commandLock.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), _stop.Token);
        if (!acquired)
        {
            throw new TimeoutException("ClientQuery не ответил на команду: " + command);
        }

        try
        {
            var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            await writer.WriteLineAsync(command);
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), _stop.Token);
        }
        finally
        {
            if (_pending != null && !_pending.Task.IsCompleted)
            {
                _pending = null;
            }

            _commandLock.Release();
        }
    }

    private async Task RefreshAsync()
    {
        var who = await CommandAsync("whoami");
        var whoItems = Ts3Protocol.ParseResponse(who.Response).Items;
        if (!who.Ok || whoItems.Count == 0)
        {
            _ownClientId = 0;
            _ownChannelId = 0;
            lock (_stateLock)
            {
                _clients.Clear();
                _channels.Clear();
            }

            Publish("TeamSpeak 3 не подключён к серверу");
            return;
        }

        var self = whoItems[0];
        var ownId = Ts3Protocol.Int(self, "clid");
        var ownCid = Ts3Protocol.Int(self, "cid");

        var server = await CommandAsync("servervariable virtualserver_name");
        var serverName = "";
        var serverItems = Ts3Protocol.ParseResponse(server.Response).Items;
        if (server.Ok && serverItems.Count > 0)
        {
            serverName = Ts3Protocol.Str(serverItems[0], "virtualserver_name");
        }

        var channelsResponse = await CommandAsync("channellist");
        var clientsResponse = await CommandAsync("clientlist -uid -away -voice -groups");

        var channels = new Dictionary<int, TsChannelState>();
        foreach (var item in Ts3Protocol.ParseResponse(channelsResponse.Response).Items)
        {
            var id = Ts3Protocol.Int(item, "cid");
            if (id <= 0)
            {
                continue;
            }

            channels[id] = new TsChannelState
            {
                Id = id,
                ParentId = Ts3Protocol.Int(item, "pid"),
                Name = Ts3Protocol.Str(item, "channel_name"),
                Order = Ts3Protocol.Int(item, "channel_order"),
            };
        }

        var clients = new Dictionary<int, TsClientState>();
        foreach (var item in Ts3Protocol.ParseResponse(clientsResponse.Response).Items)
        {
            var id = Ts3Protocol.Int(item, "clid");
            if (id <= 0)
            {
                continue;
            }

            var type = Ts3Protocol.Int(item, "client_type");
            var inputMuted = Ts3Protocol.Bool(item, "client_input_muted") || !Ts3Protocol.Bool(item, "client_input_hardware");
            clients[id] = new TsClientState
            {
                Id = id,
                ChannelId = Ts3Protocol.Int(item, "cid"),
                Nickname = Ts3Protocol.Str(item, "client_nickname"),
                Talking = Ts3Protocol.Bool(item, "client_flag_talking"),
                InputMuted = inputMuted,
                OutputMuted = Ts3Protocol.Bool(item, "client_output_muted") || !Ts3Protocol.Bool(item, "client_output_hardware"),
                Away = Ts3Protocol.Bool(item, "client_away"),
                PrioritySpeaker = Ts3Protocol.Bool(item, "client_is_priority_speaker"),
                ChannelCommander = Ts3Protocol.Bool(item, "client_is_channel_commander") || Ts3Protocol.Bool(item, "client_is_muted"),
                IsSelf = id == ownId,
                IsQuery = type == 1,
            };
        }

        lock (_stateLock)
        {
            _ownClientId = ownId;
            _ownChannelId = ownCid;
            _serverName = serverName;
            _channels.Clear();
            foreach (var kv in channels)
            {
                _channels[kv.Key] = kv.Value;
            }

            _clients.Clear();
            foreach (var kv in clients)
            {
                _clients[kv.Key] = kv.Value;
            }
        }

        _lastFullRefresh = DateTime.UtcNow;
        _status = "TeamSpeak 3: подключено";
        Publish(null);
    }

    private void HandleEvent(string line)
    {
        var space = line.IndexOf(' ');
        var name = space < 0 ? line : line[..space];
        var fields = Ts3Protocol.ParseFields(space < 0 ? "" : line[(space + 1)..]);
        var changed = false;

        switch (name)
        {
            case "notifytalkstatuschange":
            {
                var clid = Ts3Protocol.Int(fields, "clid");
                var status = Ts3Protocol.Int(fields, "status");
                lock (_stateLock)
                {
                    if (_clients.TryGetValue(clid, out var client))
                    {
                        var talking = status != 0;
                        if (client.Talking != talking)
                        {
                            _clients[clid] = client.Copy(talking: talking);
                            changed = true;
                        }
                    }
                }

                break;
            }

            case "notifyclientupdated":
            {
                var clid = Ts3Protocol.Int(fields, "clid");
                lock (_stateLock)
                {
                    if (_clients.TryGetValue(clid, out var client))
                    {
                        var updated = client.Copy(
                            channelId: fields.ContainsKey("ctid") ? Ts3Protocol.Int(fields, "ctid") : null,
                            nickname: fields.TryGetValue("client_nickname", out var nick) ? nick : null,
                            inputMuted: fields.ContainsKey("client_input_muted") ? Ts3Protocol.Bool(fields, "client_input_muted") : null,
                            outputMuted: fields.ContainsKey("client_output_muted") ? Ts3Protocol.Bool(fields, "client_output_muted") : null,
                            away: fields.ContainsKey("client_away") ? Ts3Protocol.Bool(fields, "client_away") : null,
                            prioritySpeaker: fields.ContainsKey("client_is_priority_speaker") ? Ts3Protocol.Bool(fields, "client_is_priority_speaker") : null,
                            talking: fields.ContainsKey("client_flag_talking") ? Ts3Protocol.Bool(fields, "client_flag_talking") : null,
                            channelCommander: fields.ContainsKey("client_is_muted") ? Ts3Protocol.Bool(fields, "client_is_muted") : null);
                        _clients[clid] = updated;
                        changed = true;
                    }
                }

                break;
            }

            case "notifyclientmoved":
            {
                var clid = Ts3Protocol.Int(fields, "clid");
                var ctid = Ts3Protocol.Int(fields, "ctid");
                var isSelf = false;
                lock (_stateLock)
                {
                    if (_clients.TryGetValue(clid, out var client))
                    {
                        _clients[clid] = client.Copy(channelId: ctid);
                        changed = true;
                        isSelf = client.IsSelf;
                        if (isSelf)
                        {
                            _ownChannelId = ctid;
                        }
                    }
                }

                if (isSelf || changed)
                {
                    _needsRefresh = true;
                }

                break;
            }

            case "notifyclientleftview":
            {
                var clid = Ts3Protocol.Int(fields, "clid");
                lock (_stateLock)
                {
                    if (_clients.Remove(clid))
                    {
                        changed = true;
                    }
                }

                _needsRefresh = true;
                break;
            }

            case "notifycliententerview":
            case "notifyconnectstatuschange":
            case "notifycurrentserverconnectionchanged":
            case "notifychannelcreated":
            case "notifychanneledited":
            case "notifychanneldeleted":
            case "notifychannelmoved":
            case "notifyserverupdated":
            case "notifyserveredited":
                _needsRefresh = true;
                break;
        }

        if (changed)
        {
            Publish(null);
        }
    }

    private void Publish(string? forcedStatus)
    {
        TsSnapshot snapshot;
        lock (_stateLock)
        {
            var channelName = _channels.TryGetValue(_ownChannelId, out var own) ? own.Name : "";
            var connected = _ownClientId > 0 && _ownChannelId > 0;
            snapshot = new TsSnapshot
            {
                Source = TsClientKind.Ts3,
                Connected = connected,
                Status = forcedStatus ?? (connected ? _status : "TeamSpeak 3 не подключён к серверу"),
                ServerName = _serverName,
                OwnClientId = _ownClientId,
                CurrentChannelId = _ownChannelId,
                ChannelName = channelName,
                Channels = _channels.Values.ToArray(),
                Clients = _clients.Values.ToArray(),
            };
        }

        Current = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    private void Cleanup()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        try
        {
            _reader?.Dispose();
        }
        catch
        {
        }

        try
        {
            _tcp?.Dispose();
        }
        catch
        {
        }

        _writer = null;
        _reader = null;
        _tcp = null;
        _readTask = null;
        _pending = null;
        _responseBuffer.Clear();
    }
}
