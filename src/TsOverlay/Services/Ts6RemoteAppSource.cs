using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TsOverlay.Models;

namespace TsOverlay.Services;

/// <summary>
/// Источник данных TeamSpeak 6 (и TS5) через встроенный WebSocket-интерфейс Remote Apps
/// (по умолчанию ws://127.0.0.1:5899). При первом подключении клиент спрашивает разрешение,
/// после чего выдаёт ключ API — его сохраняем и используем дальше.
/// </summary>
public sealed class Ts6RemoteAppSource : ITeamSpeakSource
{
    private const string AppIdentifier = "lv.aliennation.tsoverlay";
    private const string AppVersion = "1.0.0";

    private readonly int _port;
    private readonly Func<string> _getApiKey;
    private readonly Action<string> _saveApiKey;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _stateLock = new();

    private readonly Dictionary<int, TsChannelState> _channels = new();
    private readonly Dictionary<int, TsClientState> _clients = new();

    private Task? _worker;
    private ClientWebSocket? _ws;
    private int _connectionId;
    private int _preferredConnectionId;
    private int _ownClientId;
    private int _ownChannelId;
    private string _serverName = "";
    private volatile bool _resyncRequested;
    private bool _authenticated;
    private bool _authDenied;
    private readonly Dictionary<string, int> _unknownEvents = new();
    private DateTime _lastPermissionNotice = DateTime.MinValue;
    private string _status = "Подключение к TeamSpeak 6…";

    public Ts6RemoteAppSource(int port, Func<string> getApiKey, Action<string> saveApiKey)
    {
        _port = port;
        _getApiKey = getApiKey;
        _saveApiKey = saveApiKey;
    }

    public TsClientKind Kind => TsClientKind.Ts6;

    public TsSnapshot Current { get; private set; } = new()
    {
        Source = TsClientKind.Ts6,
        Status = "TeamSpeak 6 не запущен",
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
            await _stop.CancelAsync();
            CloseSocket();
            if (_worker != null)
            {
                await Task.WhenAny(_worker, Task.Delay(1500));
            }
        }
        catch (Exception ex)
        {
            Log.Error("TS6: ошибка остановки", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
    }

    private async Task WorkerAsync()
    {
        var ct = _stop.Token;
        while (!ct.IsCancellationRequested)
        {
            var wasAuthenticated = false;
            try
            {
                await ConnectAsync(ct);
                await ReceiveLoopAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Обрыв сокета — штатная ситуация при перезапуске клиента.
                Log.Info("TS6: нет связи с Remote Apps — " + ex.Message);
            }
            finally
            {
                wasAuthenticated = _authenticated;
                _authenticated = false;
                CloseSocket();
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            if (!wasAuthenticated)
            {
                var now = DateTime.UtcNow;
                if (_authDenied)
                {
                    // Приложение действительно не разрешено в клиенте — напоминаем редко.
                    SetStatus("TeamSpeak 6: доступ запрещён — разрешите приложение");
                    if (now - _lastPermissionNotice > TimeSpan.FromMinutes(10))
                    {
                        _lastPermissionNotice = now;
                        UserNotice?.Invoke("TeamSpeak 6: разрешите подключение оверлея в клиенте (Настройки → Remote Apps).");
                    }
                }
                else
                {
                    // Сервер/клиент временно недоступен — это не отказ в разрешениях.
                    SetStatus("TeamSpeak 6: нет связи, переподключаюсь…");
                }

                Publish();
            }
            else
            {
                SetStatus("TeamSpeak 6: нет связи, переподключаюсь…");
                Publish();
            }

            try
            {
                await Task.Delay(wasAuthenticated ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(10), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        CloseSocket();
        _resyncRequested = false;
        _connectionId = 0;
        _ownClientId = 0;
        _ownChannelId = 0;

        var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        _ws = ws;

        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(TimeSpan.FromSeconds(4));
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), connectCts.Token);
        }

        var auth = new
        {
            type = "auth",
            payload = new
            {
                identifier = AppIdentifier,
                version = AppVersion,
                name = "TS Overlay",
                description = "Оверлей TeamSpeak: кто в канале, кто говорит, у кого выключен микрофон",
                content = new
                {
                    apiKey = _getApiKey(),
                },
            },
        };

        var json = JsonSerializer.Serialize(auth);
        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, ct);
        Log.Info("TS6: отправлен запрос авторизации Remote Apps");

        // Если клиент молчит, значит пользователь ещё не нажал «Разрешить» — подскажем.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(6), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_authenticated)
            {
                return;
            }

            SetStatus("TeamSpeak 6: ожидаю разрешение приложения");
            Publish();
        }, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var ws = _ws ?? throw new InvalidOperationException("Нет WebSocket");
        var buffer = new byte[16 * 1024];
        using var accumulator = new MemoryStream();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                Log.Info($"TS6: клиент закрыл соединение ({ws.CloseStatus})");
                break;
            }

            accumulator.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(accumulator.ToArray(), 0, (int)accumulator.Length);
            accumulator.SetLength(0);

            HandleMessage(text);

            if (_resyncRequested)
            {
                Log.Info("TS6: требуется полная синхронизация, переподключаюсь");
                break;
            }
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = Str(root, "type");
            if (type.Length == 0)
            {
                return;
            }

            if (!root.TryGetProperty("payload", out var payload))
            {
                return;
            }

            // TS6 присылает часть событий в обёртке:
            // {"type":"proto.ServerEvent","payload":{"connectionId":2,"clientEnterView":{...}}}
            if (type.StartsWith("proto.", StringComparison.Ordinal))
            {
                HandleWrappedEvents(payload);
                return;
            }

            switch (type)
            {
                case "auth":
                    HandleAuth(root, payload);
                    break;
                case "channels":
                    HandleChannels(payload);
                    break;
                case "channelCreated":
                case "channelEdited":
                case "channelPropertiesUpdated":
                    HandleChannelUpsert(payload);
                    break;
                case "channelDeleted":
                    HandleChannelDeleted(payload);
                    break;
                case "clientMoved":
                    HandleClientMoved(payload);
                    break;
                case "clientPropertiesUpdated":
                    HandleClientPropertiesUpdated(payload);
                    break;
                case "talkStatusChanged":
                    HandleTalkStatus(payload);
                    break;
                case "clientSelfPropertyUpdated":
                    HandleSelfProperty(payload);
                    break;
                case "connectStatusChanged":
                    HandleConnectStatus(payload);
                    break;
                case "neededPermissions":
                case "clientNeededPermissions":
                    // Клиент запрашивает подтверждение прав; это не ошибка и не повод для уведомления.
                    break;
                case "serverPropertiesUpdated":
                    HandleServerProperties(payload);
                    break;
                case "clientJoined":
                case "channelMoved":
                case "serverEdited":
                    RequestResync("событие " + type);
                    break;
                case "clientLeft":
                case "clientLeftView":
                case "clientDisconnected":
                    HandleClientLeftView(payload);
                    break;
                default:
                    LogUnknown(type);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("TS6: ошибка разбора сообщения: " + Truncate(json, 400), ex);
        }
    }

    /// <summary>Разбор событий TS6 в обёртке proto.ClientEvent / proto.ServerEvent.</summary>
    private void HandleWrappedEvents(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && _connectionId != 0 && connId != _connectionId)
        {
            // Даже у неактивного подключения смотрим только сигнал смены активного сервера
            // (inputHardware) и connectStatus — остальные события не нашего состояния.
            foreach (var prop in payload.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (prop.Name == "clientSelfPropertyUpdated")
                {
                    HandleSelfProperty(prop.Value);
                }
                else if (prop.Name == "connectStatusChanged")
                {
                    HandleConnectStatus(prop.Value);
                }
            }

            return;
        }

        foreach (var prop in payload.EnumerateObject())
        {
            if (prop.Name == "connectionId" || prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            switch (prop.Name)
            {
                case "talkStatusChanged":
                    HandleTalkStatus(prop.Value);
                    break;
                case "clientEnterView":
                    HandleClientEnterView(prop.Value);
                    break;
                case "clientUpdated":
                    HandleClientPropertiesUpdated(prop.Value);
                    break;
                case "clientMoved":
                    HandleClientMoved(prop.Value);
                    break;
                case "clientLeftView":
                case "clientLeft":
                case "clientDisconnected":
                    HandleClientLeftView(prop.Value);
                    break;
                case "channelCreated":
                case "channelEdited":
                case "channelPropertiesUpdated":
                    HandleChannelUpsert(prop.Value);
                    break;
                case "channelDeleted":
                    HandleChannelDeleted(prop.Value);
                    break;
                case "serverUpdated":
                case "serverEdited":
                case "channelMoved":
                case "neededPermissions":
                case "clientNeededPermissions":
                    break;
                default:
                    LogUnknown("proto." + prop.Name);
                    break;
            }
        }
    }

    private void HandleClientEnterView(JsonElement payload)
    {
        var clientId = Int(payload, "clientId");
        var channelId = Int(payload, "channelToId");
        if (channelId <= 0)
        {
            channelId = Int(payload, "channelId");
        }

        if (clientId <= 0 || channelId <= 0 || !payload.TryGetProperty("properties", out var props))
        {
            RequestResync("clientEnterView без id/канала/свойств");
            return;
        }

        lock (_stateLock)
        {
            var isSelf = clientId == _ownClientId;
            _clients[clientId] = BuildClient(clientId, channelId, props, isSelf);
            if (isSelf)
            {
                _ownChannelId = channelId;
            }
        }

        Publish();
    }

    private void HandleClientLeftView(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && _connectionId != 0 && connId != _connectionId)
        {
            return;
        }

        var clientId = Int(payload, "clientId");
        if (clientId <= 0)
        {
            clientId = Int(payload, "id");
        }

        if (clientId <= 0)
        {
            return;
        }

        lock (_stateLock)
        {
            _clients.Remove(clientId);
        }

        Publish();
    }

    private void LogUnknown(string type)
    {
        if (!_unknownEvents.TryGetValue(type, out var seen) || seen < 3)
        {
            _unknownEvents[type] = seen + 1;
            Log.Info($"TS6: необработанное событие {type}");
        }
    }

    private void HandleAuth(JsonElement root, JsonElement payload)
    {
        var statusCode = 0;
        var statusMessage = "";
        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            statusCode = Int(status, "code");
            statusMessage = Str(status, "message");
        }

        if (statusCode != 0)
        {
            _authDenied = true;
            Log.Error($"TS6: авторизация отклонена ({statusCode} {statusMessage})");
            SetStatus("TeamSpeak 6: доступ запрещён — разрешите приложение");
            Publish();
            return;
        }

        _authDenied = false;

        var apiKey = Str(payload, "apiKey");
        if (apiKey.Length > 0 && apiKey != _getApiKey())
        {
            _saveApiKey(apiKey);
            Log.Info("TS6: получен и сохранён ключ Remote Apps");
        }

        _authenticated = true;

        if (!payload.TryGetProperty("connections", out var connections) || connections.ValueKind != JsonValueKind.Array)
        {
            Log.Info("TS6: в ответе auth нет списка подключений");
            SetStatus("TeamSpeak 6: нет активного подключения к серверу");
            Publish();
            return;
        }

        JsonElement chosen = default;
        var found = false;
        var fallback = false;

        // Сначала пробуем подключение, которое TS6 уже называл активным в событиях.
        if (_preferredConnectionId > 0)
        {
            foreach (var conn in connections.EnumerateArray())
            {
                if (conn.ValueKind != JsonValueKind.Object || Int(conn, "id") != _preferredConnectionId)
                {
                    continue;
                }

                var preferredClientId = Int(conn, "clientId");
                if (preferredClientId > 0 && ConnectionContainsClient(conn, preferredClientId))
                {
                    chosen = conn;
                    found = true;
                }

                break;
            }
        }

        if (!found)
        {
            foreach (var conn in connections.EnumerateArray())
            {
                if (conn.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!fallback)
                {
                    chosen = conn;
                    fallback = true;
                }

                var clientId = Int(conn, "clientId");
                if (clientId > 0 && ConnectionContainsClient(conn, clientId))
                {
                    chosen = conn;
                    found = true;
                    fallback = false;
                    break;
                }
            }
        }

        if (!fallback && !found)
        {
            Log.Info("TS6: нет активных серверных подключений");
            SetStatus("TeamSpeak 6: нет активного подключения к серверу");
            Publish();
            return;
        }

        var connectionIds = new StringBuilder();
        foreach (var conn in connections.EnumerateArray())
        {
            if (conn.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (connectionIds.Length > 0)
            {
                connectionIds.Append(',');
            }

            connectionIds.Append(Int(conn, "id"));
        }

        ParseConnection(chosen);
        _preferredConnectionId = _connectionId;
        Log.Info($"TS6: подключения [{connectionIds}], выбрано {_connectionId}, свой clid={_ownClientId}, cid={_ownChannelId}");
        SetStatus("TeamSpeak 6: подключено");
        Publish();
    }

    private static bool ConnectionContainsClient(JsonElement conn, int clientId)
    {
        if (!conn.TryGetProperty("clientInfos", out var infos) || infos.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var info in infos.EnumerateArray())
        {
            if (Int(info, "id") == clientId)
            {
                return true;
            }
        }

        return false;
    }

    private void ParseConnection(JsonElement conn)
    {
        lock (_stateLock)
        {
            _channels.Clear();
            _clients.Clear();
            _connectionId = Int(conn, "id");
            _ownClientId = Int(conn, "clientId");
            _serverName = "";

            if (conn.TryGetProperty("properties", out var serverProps) && serverProps.ValueKind == JsonValueKind.Object)
            {
                var name = Str(serverProps, "name");
                if (name.Length > 0)
                {
                    _serverName = name;
                }
            }

            if (conn.TryGetProperty("channelInfos", out var channelInfos))
            {
                ParseChannelInfos(channelInfos);
            }

            if (conn.TryGetProperty("clientInfos", out var clientInfos) && clientInfos.ValueKind == JsonValueKind.Array)
            {
                foreach (var info in clientInfos.EnumerateArray())
                {
                    var id = Int(info, "id");
                    var channelId = Int(info, "channelId");
                    if (id <= 0 || !info.TryGetProperty("properties", out var props))
                    {
                        continue;
                    }

                    _clients[id] = BuildClient(id, channelId, props, id == _ownClientId);
                }
            }

            if (_clients.TryGetValue(_ownClientId, out var own))
            {
                _ownChannelId = own.ChannelId;
            }
        }
    }

    private void ParseChannelInfos(JsonElement channelInfos)
    {
        if (channelInfos.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (channelInfos.TryGetProperty("rootChannels", out var roots) && roots.ValueKind == JsonValueKind.Array)
        {
            foreach (var ch in roots.EnumerateArray())
            {
                AddChannel(ch);
            }
        }

        if (channelInfos.TryGetProperty("subChannels", out var subs) && subs.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in subs.EnumerateObject())
            {
                if (group.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var ch in group.Value.EnumerateArray())
                {
                    AddChannel(ch);
                }
            }
        }
    }

    private void AddChannel(JsonElement ch)
    {
        var id = Int(ch, "id");
        if (id <= 0 || !ch.TryGetProperty("properties", out var props))
        {
            return;
        }

        _channels[id] = new TsChannelState
        {
            Id = id,
            ParentId = ParseInt(Str(ch, "parentId")),
            Name = Str(props, "name"),
            Order = ParseInt(Str(ch, "order")),
        };
    }

    private void HandleChannels(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && connId != _connectionId)
        {
            return;
        }

        lock (_stateLock)
        {
            _channels.Clear();
            if (payload.TryGetProperty("info", out var info))
            {
                ParseChannelInfos(info);
            }
        }

        Publish();
    }

    private void HandleChannelUpsert(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && connId != _connectionId)
        {
            return;
        }

        var id = Int(payload, "channelId");
        if (id <= 0)
        {
            id = Int(payload, "id");
        }

        if (id <= 0 || !payload.TryGetProperty("properties", out var props))
        {
            RequestResync("channel upsert без id/свойств");
            return;
        }

        lock (_stateLock)
        {
            var parent = ParseInt(Str(payload, "parentId"));
            _channels.TryGetValue(id, out var old);
            var name = Str(props, "name");
            if (name.Length == 0 && old != null)
            {
                name = old.Name;
            }

            var order = Str(props, "order");
            _channels[id] = new TsChannelState
            {
                Id = id,
                ParentId = parent != 0 ? parent : old?.ParentId ?? 0,
                Name = name,
                Order = order.Length > 0 ? ParseInt(order) : old?.Order ?? 0,
            };
        }

        Publish();
    }

    private void HandleChannelDeleted(JsonElement payload)
    {
        var id = Int(payload, "channelId");
        if (id <= 0)
        {
            return;
        }

        lock (_stateLock)
        {
            _channels.Remove(id);
        }

        Publish();
    }

    private void HandleClientMoved(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && connId != _connectionId)
        {
            return;
        }

        var clientId = Int(payload, "clientId");
        var newChannel = Int(payload, "newChannelId");
        if (newChannel <= 0)
        {
            newChannel = Int(payload, "channelToId");
        }

        var oldChannel = Int(payload, "oldChannelId");
        if (oldChannel <= 0)
        {
            oldChannel = Int(payload, "channelFromId");
        }

        if (clientId <= 0)
        {
            return;
        }

        lock (_stateLock)
        {
            if (newChannel <= 0)
            {
                _clients.Remove(clientId);
            }
            else if (payload.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                var isSelf = clientId == _ownClientId;
                var baseClient = _clients.TryGetValue(clientId, out var previous)
                    ? previous
                    : new TsClientState { Id = clientId, IsSelf = isSelf };
                _clients[clientId] = ApplyProperties(baseClient, props).Copy(channelId: newChannel, isSelf: isSelf);
                if (isSelf)
                {
                    _ownChannelId = newChannel;
                }
            }
            else if (_clients.TryGetValue(clientId, out var existing))
            {
                _clients[clientId] = existing.Copy(channelId: newChannel);
                if (clientId == _ownClientId)
                {
                    _ownChannelId = newChannel;
                }
            }
            else if (oldChannel <= 0)
            {
                // Вошёл новый клиент, а свойств в событии нет — дочитаем полным снимком.
                RequestResync($"вход клиента {clientId} без свойств");
            }
        }

        Publish();
    }

    private void HandleClientPropertiesUpdated(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && _connectionId != 0 && connId != _connectionId)
        {
            // Обновление неактивного подключения: у нас хранится только активное.
            return;
        }

        var clientId = Int(payload, "clientId");
        if (clientId <= 0 || !payload.TryGetProperty("properties", out var props))
        {
            return;
        }


        lock (_stateLock)
        {
            if (!_clients.TryGetValue(clientId, out var existing))
            {
                existing = new TsClientState { Id = clientId, IsSelf = clientId == _ownClientId };
                _clients[clientId] = existing;
            }

            _clients[clientId] = ApplyProperties(existing, props);
        }

        Publish();
    }

    private void HandleTalkStatus(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && _connectionId != 0 && connId != _connectionId)
        {
            return;
        }

        var clientId = Int(payload, "clientId");
        var status = Int(payload, "status");
        lock (_stateLock)
        {
            if (_clients.TryGetValue(clientId, out var client))
            {
                var talking = status == 1;
                if (client.Talking != talking)
                {
                    _clients[clientId] = client.Copy(talking: talking);
                }
            }
        }

        Publish();
    }

    private void HandleSelfProperty(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        var flag = Str(payload, "flag");
        var newValue = Bool(payload, "newValue");

        // TS6 сообщает о смене активного подключения только так: clientSelfPropertyUpdated
        // с flag=inputHardware. Это же единственный сигнал, что пользователь переключил сервер.
        if (string.Equals(flag, "inputHardware", StringComparison.OrdinalIgnoreCase))
        {
            NoteActiveConnection(connId, "смена активного подключения (inputHardware)");
            return;
        }

        if (connId != 0 && _connectionId != 0 && connId != _connectionId)
        {
            // Событие неактивного сервера — не наше состояние.
            return;
        }

        lock (_stateLock)
        {
            if (_clients.TryGetValue(_ownClientId, out var me))
            {
                var updated = flag switch
                {
                    "flagTalking" => me.Copy(talking: newValue),
                    "inputMuted" => me.Copy(inputMuted: newValue),
                    "outputMuted" => me.Copy(outputMuted: newValue),
                    "isMuted" => me.Copy(inputMuted: newValue),
                    _ => me,
                };

                if (!ReferenceEquals(updated, me))
                {
                    _clients[_ownClientId] = updated;
                }
            }
        }

        Publish();
    }

    private void HandleConnectStatus(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        var status = Int(payload, "status");
        Log.Info($"TS6: connectStatus={status} conn={connId} active={_connectionId}");

        if (status == 0)
        {
            if (connId == _connectionId || _connectionId == 0)
            {
                lock (_stateLock)
                {
                    _clients.Clear();
                    _channels.Clear();
                    _ownClientId = 0;
                    _ownChannelId = 0;
                }

                SetStatus("TeamSpeak 6 не подключён к серверу");
                Publish();
            }

            return;
        }

        if (status == 4)
        {
            // Подключение к серверу установлено. Активное подключение определяется
            // только событием inputHardware, поэтому здесь ничего не переключаем.
            Log.Info($"TS6: подключение {connId} установлено");
        }
    }

    private void HandleServerProperties(JsonElement payload)
    {
        var connId = Int(payload, "connectionId");
        if (connId != 0 && connId != _connectionId)
        {
            return;
        }

        if (payload.TryGetProperty("properties", out var props))
        {
            var name = Str(props, "name");
            if (name.Length > 0)
            {
                lock (_stateLock)
                {
                    _serverName = name;
                }

                Publish();
            }
        }
    }

    private TsClientState BuildClient(int id, int channelId, JsonElement props, bool isSelf)
    {
        var fresh = new TsClientState { Id = id, ChannelId = channelId, IsSelf = isSelf };
        var result = ApplyProperties(fresh, props);
        return result.Copy(isQuery: IsQueryClient(props));
    }

    /// <summary>
    /// Применяет к клиенту только те свойства, которые реально пришли в событии
    /// (в TS6 частичные обновления: clientUpdated присылает один-два ключа).
    /// </summary>
    private TsClientState ApplyProperties(TsClientState client, JsonElement props)
    {
        var nickname = Has(props, "nickname") ? Str(props, "nickname")
            : Has(props, "name") ? Str(props, "name")
            : null;

        bool? inputMuted = null;
        if (Has(props, "inputMuted"))
        {
            inputMuted = Bool(props, "inputMuted");
        }

        if (Has(props, "inputDeactivated"))
        {
            inputMuted = (inputMuted ?? false) || Bool(props, "inputDeactivated");
        }

        // isMuted — мьют на стороне сервера (не путать с inputMuted).
        if (Has(props, "isMuted"))
        {
            inputMuted = (inputMuted ?? false) || Bool(props, "isMuted");
        }

        bool? outputMuted = null;
        if (Has(props, "outputMuted"))
        {
            outputMuted = Bool(props, "outputMuted");
        }

        if (Has(props, "outputOnlyMuted"))
        {
            outputMuted = (outputMuted ?? false) || Bool(props, "outputOnlyMuted");
        }

        return client.Copy(
            nickname: nickname,
            inputMuted: inputMuted,
            outputMuted: outputMuted,
            talking: Has(props, "flagTalking") ? Bool(props, "flagTalking") : null,
            away: Has(props, "away") ? Bool(props, "away") : null,
            prioritySpeaker: Has(props, "isPrioritySpeaker") ? Bool(props, "isPrioritySpeaker") : null,
            channelCommander: Has(props, "isChannelCommander") ? Bool(props, "isChannelCommander") : null,
            avatarHash: Has(props, "flagAvatar") ? Str(props, "flagAvatar") : null,
            serverGroups: Has(props, "serverGroups") ? Str(props, "serverGroups") : null,
            isQuery: IsQueryClient(props) ? true : null);
    }

    private static bool IsQueryClient(JsonElement props)
    {
        if (!Has(props, "type"))
        {
            return false;
        }

        return props.GetProperty("type").ValueKind switch
        {
            JsonValueKind.String => props.GetProperty("type").GetString()?.Contains("QUERY", StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Number => props.GetProperty("type").TryGetInt32(out var number) && number == 1,
            _ => false,
        };
    }

    private static bool Has(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _);

    private void SetStatus(string status)
    {
        _status = status;
    }

    private void RequestResync(string reason)
    {
        _resyncRequested = true;
        Log.Info("TS6: нужна полная синхронизация — " + reason);
    }

    /// <summary>
    /// Запоминает подключение, которое TS6 считает активным, и просит один ресинк,
    /// чтобы авторизация выбрала именно его. Без этого события с другим connectionId
    /// бесконечно вызывали реконнект.
    /// </summary>
    private void NoteActiveConnection(int connId, string reason)
    {
        if (connId <= 0 || connId == _connectionId)
        {
            return;
        }

        var changed = _preferredConnectionId != connId;
        _preferredConnectionId = connId;

        if (!_resyncRequested || changed)
        {
            RequestResync($"{reason}: активное {connId}, текущее {_connectionId}");
        }
    }

    private void Publish()
    {
        TsSnapshot snapshot;
        lock (_stateLock)
        {
            var channelName = _channels.TryGetValue(_ownChannelId, out var own) ? own.Name : "";
            var connected = _ownClientId > 0 && _ownChannelId > 0;
            snapshot = new TsSnapshot
            {
                Source = TsClientKind.Ts6,
                Connected = connected,
                Status = connected ? _status : (_status.StartsWith("TeamSpeak 6: подключено", StringComparison.Ordinal) ? "TeamSpeak 6 не подключён к серверу" : _status),
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

    /// <summary>
    /// Жёстко закрываем сокет: CloseAsync ждёт ответа сервера и может зависнуть на минуты,
    /// а Abort освобождает ресурсы сразу.
    /// </summary>
    private void CloseSocket()
    {
        var ws = Interlocked.Exchange(ref _ws, null);
        if (ws == null)
        {
            return;
        }

        try
        {
            ws.Abort();
        }
        catch
        {
            // не критично
        }

        try
        {
            ws.Dispose();
        }
        catch
        {
        }
    }

    private static string Str(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                _ => "",
            };
        }

        return "";
    }

    private static int Int(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    }

    private static bool Bool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            JsonValueKind.String => value.GetString() is "1" or "true",
            _ => false,
        };
    }

    private static int ParseInt(string value) => int.TryParse(value, out var result) ? result : 0;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

