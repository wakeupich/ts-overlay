using System.Diagnostics;
using System.Windows.Threading;
using TsOverlay.Models;

namespace TsOverlay.Services;

/// <summary>
/// Определяет, какой клиент TeamSpeak запущен, и держит активное подключение к нему.
/// Авто-режим: если запущен TS3 — работаем с ним, иначе с TS6.
/// </summary>
public sealed class TeamSpeakMonitor : IAsyncDisposable
{
    private readonly Func<AppConfig> _config;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private ITeamSpeakSource? _source;
    private TsClientKind _kind = TsClientKind.None;
    private bool? _lastAvailability;
    private bool _started;
    private bool _autoSwitched;
    private DateTime _kindSince = DateTime.UtcNow;

    public TeamSpeakMonitor(Func<AppConfig> config, Dispatcher dispatcher)
    {
        _config = config;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.5),
        };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    public event Action<TsSnapshot>? SnapshotChanged;

    public event Action<string>? UserNotice;

    public event Action<bool>? AvailabilityChanged;

    public TsSnapshot Current { get; private set; } = TsSnapshot.Empty;

    public TsClientKind ActiveKind => _kind;

    public string LastError { get; private set; } = "";

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _timer.Start();
        _ = CheckAsync();
    }

    public async Task RestartAsync()
    {
        await StopSourceAsync();
        _kind = TsClientKind.None;
        _autoSwitched = false;
        await CheckAsync();
    }

    public async Task StopAsync()
    {
        _started = false;
        _timer.Stop();
        await StopSourceAsync();
        _kind = TsClientKind.None;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task CheckAsync()
    {
        if (!_started)
        {
            return;
        }

        try
        {
            var cfg = _config();
            var ts3 = IsProcessRunning("ts3client_win64") || IsProcessRunning("ts3client_win32");
            var ts6 = IsProcessRunning("TeamSpeak");
            var any = ts3 || ts6;

            if (_lastAvailability != any)
            {
                _lastAvailability = any;
                AvailabilityChanged?.Invoke(any);
            }

            var desired = cfg.ClientMode switch
            {
                "Ts3" => ts3 ? TsClientKind.Ts3 : TsClientKind.None,
                "Ts6" => ts6 ? TsClientKind.Ts6 : TsClientKind.None,
                _ => ts3 ? TsClientKind.Ts3 : ts6 ? TsClientKind.Ts6 : TsClientKind.None,
            };

            // В авто-режиме: если выбранный клиент запущен, но не подключён к серверу,
            // а второй клиент подключён — переключаемся на него.
            if (cfg.ClientMode == "Auto" && desired == _kind && _kind != TsClientKind.None && !_autoSwitched &&
                !Current.Connected && DateTime.UtcNow - _kindSince > TimeSpan.FromSeconds(6))
            {
                if (_kind == TsClientKind.Ts3 && ts6)
                {
                    Log.Info("Монитор: TS3 не подключён, переключаюсь на TS6");
                    desired = TsClientKind.Ts6;
                    _autoSwitched = true;
                }
                else if (_kind == TsClientKind.Ts6 && ts3)
                {
                    Log.Info("Монитор: TS6 не подключён, переключаюсь на TS3");
                    desired = TsClientKind.Ts3;
                    _autoSwitched = true;
                }
            }

            if (desired != _kind)
            {
                await StopSourceAsync();
                _kind = desired;
                _kindSince = DateTime.UtcNow;
                if (desired != TsClientKind.None)
                {
                    StartSource(desired, cfg);
                }
                else
                {
                    Publish(new TsSnapshot
                    {
                        Source = TsClientKind.None,
                        Status = "TeamSpeak не запущен",
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Монитор TeamSpeak: ошибка проверки", ex);
        }
    }

    private void StartSource(TsClientKind kind, AppConfig cfg)
    {
        try
        {
            _source = kind == TsClientKind.Ts3
                ? new Ts3ClientQuerySource(cfg.Ts3ApiKey, cfg.Ts3Port)
                : new Ts6RemoteAppSource(cfg.Ts6Port, () => _config().Ts6ApiKey, SaveTs6Key);

            _source.SnapshotChanged += OnSnapshot;
            _source.UserNotice += msg => _dispatcher.BeginInvoke(() => UserNotice?.Invoke(msg));
            _ = _source.StartAsync();
            Log.Info($"Монитор: запущен источник {kind}");
            Publish(new TsSnapshot { Source = kind, Status = kind == TsClientKind.Ts3 ? "TeamSpeak 3: подключение…" : "TeamSpeak 6: подключение…" });
        }
        catch (Exception ex)
        {
            Log.Error("Монитор: не удалось запустить источник", ex);
        }
    }

    private void SaveTs6Key(string key)
    {
        try
        {
            var cfg = _config();
            cfg.Ts6ApiKey = key;
            OnTs6Key?.Invoke(key);
        }
        catch (Exception ex)
        {
            Log.Error("Монитор: не удалось сохранить ключ TS6", ex);
        }
    }

    /// <summary>AppHost подписывается, чтобы сохранить ключ в конфиг.</summary>
    public event Action<string>? OnTs6Key;

    private void OnSnapshot(TsSnapshot snapshot)
    {
        _dispatcher.InvokeAsync(() => Publish(snapshot));
    }

    private void Publish(TsSnapshot snapshot)
    {
        Current = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    private async Task StopSourceAsync()
    {
        var source = _source;
        _source = null;
        if (source == null)
        {
            return;
        }

        try
        {
            await source.StopAsync();
            await source.DisposeAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Монитор: ошибка остановки источника", ex);
        }
    }

    private static bool IsProcessRunning(string name)
    {
        try
        {
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
