using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using TsOverlay.Models;
using TsOverlay.Services;
using TsOverlay.ViewModels;
using TsOverlay.Views;
using WinForms = System.Windows.Forms;

namespace TsOverlay;

/// <summary>Связывает всё вместе: конфиг, клиент TeamSpeak, слежение за играми, оверлей и трей.</summary>
public sealed class AppHost
{
    private readonly ConfigService _configService = new();
    private readonly OverlayViewModel _viewModel = new();

    private TeamSpeakMonitor? _monitor;
    private GameWatcher? _gameWatcher;
    private OverlayWindow? _overlay;
    private TrayService? _tray;
    private SettingsWindow? _settings;
    private HotkeyService? _hotkeys;
    private System.Windows.Threading.DispatcherTimer? _hudTimer;

    private GameWindowInfo? _lastGame;
    private bool _preview;
    private bool _pinned;
    private bool _hudVisible;
    private bool _everSawTeamSpeak;
    private bool _shuttingDown;

    private AppConfig Config => _configService.Config;

    public void Start()
    {
        var firstRun = !_configService.Exists;
        _configService.Load();
        var config = Config;

        _overlay = new OverlayWindow(_viewModel);
        _overlay.Configure(config);

        _tray = new TrayService();
        _tray.SettingsRequested += OpenSettings;
        _tray.OverlayEnabledChanged += SetOverlayEnabled;
        _tray.PreviewToggled += TogglePreview;
        _tray.ToggleOverlayRequested += TogglePinnedOverlay;
        _tray.TalkingHudRequested += ShowTalkingHud;
        _tray.StartupChanged += ToggleStartup;
        _tray.RestartRequested += () => _ = _monitor?.RestartAsync();
        _tray.LaunchTeamSpeakRequested += LaunchTeamSpeak;
        _tray.CheckUpdatesRequested += () => _ = CheckUpdatesAsync(silent: false);
        _tray.ExitRequested += () => Application.Current.Shutdown();

        _monitor = new TeamSpeakMonitor(() => Config, Application.Current.Dispatcher);
        _monitor.SnapshotChanged += OnSnapshot;
        _monitor.UserNotice += OnUserNotice;
        _monitor.AvailabilityChanged += OnAvailability;
        _monitor.OnTs6Key += key =>
        {
            Config.Ts6ApiKey = key;
            _configService.Save();
        };

        _gameWatcher = new GameWatcher();
        _gameWatcher.SetGames(Config.Games);
        _gameWatcher.SetEnabled(Config.OverlayEnabled);
        _gameWatcher.ActiveWindowChanged += OnGameWindowChanged;
        _gameWatcher.Start();

        _tray.SetOverlayChecked(Config.OverlayEnabled);
        _tray.SetStartupChecked(StartupService.IsEnabled());
        _tray.SetStatus(Config.OverlayEnabled ? "Подключение к TeamSpeak…" : "Оверлей выключен");
        InitHotkeys();

        if (Config.AutoLaunchTeamSpeak && !IsTeamSpeakRunning())
        {
            LaunchTeamSpeak();
        }

        _monitor.Start();
        Log.Info("TS Overlay запущен");

        if (Config.CheckUpdatesOnStart)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _ = CheckUpdatesAsync(silent: true);
            };
            timer.Start();
        }

        if (firstRun)
        {
            // Первый запуск: показываем настройки, чтобы сразу добавить игры.
            OpenSettings();
        }
    }

    public void Shutdown()
    {
        _shuttingDown = true;
        try
        {
            _hotkeys?.Dispose();
        }
        catch
        {
        }

        try
        {
            _gameWatcher?.Stop();
        }
        catch
        {
        }

        try
        {
            _overlay?.HideOverlay();
            _overlay?.AllowClose();
        }
        catch
        {
        }

        try
        {
            _tray?.Dispose();
        }
        catch
        {
        }

        try
        {
            _monitor?.StopAsync().Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        Log.Info("TS Overlay остановлен");
    }

    private void OnSnapshot(TsSnapshot snapshot)
    {
        try
        {
            _viewModel.Apply(snapshot, Config);
            _overlay?.RefreshLayout();

            if (!Config.OverlayEnabled)
            {
                _tray?.SetStatus("Оверлей выключен");
                return;
            }

            if (_hudVisible || _pinned)
            {
                ShowOverForeground();
            }
            else if (_preview)
            {
                ShowPreview(Config);
            }
            else if (_lastGame != null)
            {
                _overlay?.ShowOverGame(_lastGame, Config);
            }

            _tray?.SetStatus(StatusLine(snapshot));
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка обновления оверлея", ex);
        }
    }

    private void OnGameWindowChanged(GameWindowInfo? info)
    {
        try
        {
            _lastGame = info;
            Log.Info(info == null
                ? "Игра: активное окно не игра"
                : $"Игра: {info.Profile.Exe} rect=({info.Rect.Left},{info.Rect.Top},{info.Rect.Right},{info.Rect.Bottom}) dpi={info.DpiScale:0.##}");
            if (!Config.OverlayEnabled)
            {
                return;
            }

            if (_pinned)
            {
                // Оверлей закреплён горячей клавишей — не реагируем на смену игр.
                return;
            }

            if (info == null)
            {
                if (!_preview && !_hudVisible)
                {
                    _overlay?.HideOverlay();
                }

                return;
            }

            if (_preview)
            {
                _preview = false;
                _tray?.SetPreviewChecked(false);
            }

            _overlay?.ShowOverGame(info, Config);
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка позиционирования оверлея", ex);
        }
    }

    private void OnUserNotice(string message)
    {
        Log.Info("Уведомление: " + message);
        if (Config.NotifyOnPermissionRequest)
        {
            _tray?.ShowBalloon(message);
        }
    }

    private void OnAvailability(bool running)
    {
        if (running)
        {
            _everSawTeamSpeak = true;
            return;
        }

        if (_everSawTeamSpeak && Config.ExitWithTeamSpeak && !_shuttingDown)
        {
            Log.Info("TeamSpeak закрыт — завершаю работу");
            Application.Current.Shutdown();
        }
    }

    /// <summary>Открыть настройки (например, флагом --settings или из меню трея).</summary>
    public void ShowSettings() => OpenSettings();

    private void OpenSettings()
    {
        if (_settings != null)
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(Config, StatusLine(_monitor?.Current ?? TsSnapshot.Empty), ApplyPreviewConfig, RestoreLiveConfig);
        _settings.Saved += () =>
        {
            _configService.Save();
            ApplyConfig(restartMonitor: true);
        };
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    /// <summary>Показать оверлей на рабочем столе (флаг --preview и кнопка в настройках).</summary>
    public void ShowPreviewNow()
    {
        _preview = true;
        _tray?.SetPreviewChecked(true);
        _viewModel.Apply(_monitor?.Current ?? TsSnapshot.Empty, Config);
        ShowPreview(Config);
    }

    private void SetOverlayEnabled(bool enabled)
    {
        Config.OverlayEnabled = enabled;
        _configService.Save();
        ApplyConfig(restartMonitor: false);
        Log.Info("Оверлей " + (enabled ? "включён" : "выключен"));
    }

    private void ToggleStartup(bool enable)
    {
        StartupService.SetEnabled(enable);
        Config.StartWithWindows = enable;
        _configService.Save();
        _tray?.SetStartupChecked(enable);
    }

    private void InitHotkeys()
    {
        _hotkeys ??= new HotkeyService();
        _hotkeys.RegistrationFailed -= OnHotkeyFailed;
        _hotkeys.RegistrationFailed += OnHotkeyFailed;
        _hotkeys.UnregisterAll();

        if (!string.IsNullOrWhiteSpace(Config.HotkeyToggleOverlay))
        {
            _hotkeys.Register(Config.HotkeyToggleOverlay, TogglePinnedOverlay);
        }

        if (!string.IsNullOrWhiteSpace(Config.HotkeyTalkingHud))
        {
            _hotkeys.Register(Config.HotkeyTalkingHud, ShowTalkingHud);
        }

        _tray?.SetHotkeys(Config.HotkeyToggleOverlay, Config.HotkeyTalkingHud);
        Log.Info($"Горячие клавиши: оверлей «{Config.HotkeyToggleOverlay}», кто говорит «{Config.HotkeyTalkingHud}»");
    }

    private void OnHotkeyFailed(string gesture)
    {
        Log.Info("Горячая клавиша занята: " + gesture);
        _tray?.ShowBalloon($"Не удалось назначить {gesture}: комбинация занята другой программой.", error: true);
    }

    /// <summary>Проверка обновлений на GitHub Releases.</summary>
    private async Task CheckUpdatesAsync(bool silent)
    {
        var info = await new UpdateService().CheckAsync();
        if (info == null)
        {
            if (!silent)
            {
                _tray?.ShowBalloon("Не удалось проверить обновления. Для приватного репозитория проверка недоступна.", error: true);
            }

            return;
        }

        if (info.Version > UpdateService.CurrentVersion)
        {
            var text = $"Доступна версия {info.Tag} (у вас {UpdateService.CurrentVersion.ToString(3)}).\n\n" +
                       (info.MsiUrl != null
                           ? "Скачать и установить сейчас? Оверлей закроется, установщик обновит файлы."
                           : "Открыть страницу релиза для скачивания?");

            var result = MessageBox.Show(text, "TS Overlay", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            if (info.MsiUrl != null)
            {
                await DownloadAndRunInstallerAsync(info.MsiUrl, info.Version);
            }
            else
            {
                Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
            }

            return;
        }

        if (!silent)
        {
            _tray?.ShowBalloon($"У вас последняя версия ({UpdateService.CurrentVersion.ToString(3)}).");
        }
    }

    private async Task DownloadAndRunInstallerAsync(string url, Version version)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"TsOverlay-{version}-Setup.msi");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var bytes = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, bytes);
            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{path}\" /qb") { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось скачать обновление", ex);
            _tray?.ShowBalloon("Не удалось скачать обновление: " + ex.Message, error: true);
        }
    }

    private void TogglePinnedOverlay()
    {
        if (_pinned)
        {
            _pinned = false;
            _overlay?.HideOverlay();
            return;
        }

        _pinned = true;
        ShowOverForeground();
    }

    /// <summary>Глобальная клавиша: показать «кто говорит» на несколько секунд поверх любого окна.</summary>
    private void ShowTalkingHud()
    {
        ShowOverForeground();
        _hudVisible = true;

        if (_hudTimer == null)
        {
            _hudTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(6),
            };
            _hudTimer.Tick += (_, _) =>
            {
                _hudTimer!.Stop();
                _hudVisible = false;
                if (!_pinned && _lastGame == null)
                {
                    _overlay?.HideOverlay();
                }
            };
        }

        _hudTimer.Stop();
        _hudTimer.Start();
    }

    /// <summary>Показать HUD по флагу --hud (тест/диагностика).</summary>
    public void ShowHudNow() => ShowTalkingHud();

    private void ShowOverForeground()
    {
        if (!Config.OverlayEnabled)
        {
            return;
        }

        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var rect) || rect.Width < 80)
        {
            return;
        }

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return;
        }

        var dpi = Native.GetDpiForWindow(hwnd);
        Log.Info($"HUD: окно pid={pid} rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom}) dpi={dpi}");
        _viewModel.Apply(_monitor?.Current ?? TsSnapshot.Empty, Config);
        _overlay?.ShowPreview(rect, dpi > 0 ? dpi / 96.0 : 1.0, Config);
    }

    private void TogglePreview()
    {
        _preview = !_preview;
        _tray?.SetPreviewChecked(_preview);

        if (_preview)
        {
            ShowPreview(Config);
        }
        else if (_lastGame != null && Config.OverlayEnabled)
        {
            _overlay?.ShowOverGame(_lastGame, Config);
        }
        else
        {
            _overlay?.HideOverlay();
        }
    }

    private void ShowPreview(AppConfig config)
    {
        var screen = WinForms.Screen.PrimaryScreen;
        if (screen == null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var rect = new Native.RECT
        {
            Left = area.Left,
            Top = area.Top,
            Right = area.Right,
            Bottom = area.Bottom,
        };

        var dpi = Native.GetDpiForSystem();
        _overlay?.ShowPreview(rect, dpi > 0 ? dpi / 96.0 : 1.0, config);
    }

    private void ApplyPreviewConfig(AppConfig config)
    {
        _viewModel.Apply(_monitor?.Current ?? TsSnapshot.Empty, config);
        _overlay?.Configure(config);
        _overlay?.RefreshLayout();

        if (!config.OverlayEnabled)
        {
            _overlay?.HideOverlay();
            return;
        }

        if (_lastGame != null)
        {
            _overlay?.ShowOverGame(_lastGame, config);
            return;
        }

        _preview = true;
        _tray?.SetPreviewChecked(true);
        ShowPreview(config);
    }

    private void RestoreLiveConfig()
    {
        _viewModel.Apply(_monitor?.Current ?? TsSnapshot.Empty, Config);
        _overlay?.Configure(Config);
        _overlay?.RefreshLayout();

        if (!Config.OverlayEnabled)
        {
            _overlay?.HideOverlay();
            return;
        }

        if (_preview)
        {
            ShowPreview(Config);
        }
        else if (_lastGame != null)
        {
            _overlay?.ShowOverGame(_lastGame, Config);
        }
    }

    private void ApplyConfig(bool restartMonitor)
    {
        _tray?.SetOverlayChecked(Config.OverlayEnabled);
        _tray?.SetStartupChecked(StartupService.IsEnabled());
        _gameWatcher?.SetGames(Config.Games);
        _gameWatcher?.SetEnabled(Config.OverlayEnabled);
        InitHotkeys();

        _viewModel.Apply(_monitor?.Current ?? TsSnapshot.Empty, Config);
        _overlay?.Configure(Config);
        _overlay?.RefreshLayout();

        if (!Config.OverlayEnabled)
        {
            _overlay?.HideOverlay();
            _preview = false;
            _tray?.SetPreviewChecked(false);
        }
        else if (_preview)
        {
            ShowPreview(Config);
        }
        else if (_lastGame != null)
        {
            _overlay?.ShowOverGame(_lastGame, Config);
        }

        if (restartMonitor)
        {
            _ = _monitor?.RestartAsync();
        }
    }

    private string StatusLine(TsSnapshot snapshot)
    {
        if (!Config.OverlayEnabled)
        {
            return "Оверлей выключен";
        }

        var text = snapshot.Status;
        if (snapshot.Connected && !string.IsNullOrWhiteSpace(snapshot.ChannelName))
        {
            text += " · " + snapshot.ChannelName;
        }

        return text;
    }

    private void LaunchTeamSpeak()
    {
        var path = ResolveTeamSpeakPath();
        if (path == null)
        {
            _tray?.ShowBalloon("Не найден клиент TeamSpeak. Укажите путь в настройках (вкладка «Подключение»).", error: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            });
            Log.Info("Запущен TeamSpeak: " + path);
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось запустить TeamSpeak", ex);
            _tray?.ShowBalloon("Не удалось запустить TeamSpeak: " + ex.Message, error: true);
        }
    }

    private string? ResolveTeamSpeakPath()
    {
        var ts3 = FirstExisting(
            Config.Ts3Path,
            @"C:\Program Files\TeamSpeak 3 Client\ts3client_win64.exe",
            @"C:\Program Files (x86)\TeamSpeak 3 Client\ts3client_win32.exe");
        var ts6 = FirstExisting(
            Config.Ts6Path,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\TeamSpeak\TeamSpeak.exe"));

        return Config.ClientMode switch
        {
            "Ts3" => ts3,
            "Ts6" => ts6,
            _ => ts3 ?? ts6,
        };
    }

    private static string? FirstExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool IsTeamSpeakRunning()
    {
        try
        {
            return Process.GetProcessesByName("ts3client_win64").Length > 0
                || Process.GetProcessesByName("ts3client_win32").Length > 0
                || Process.GetProcessesByName("TeamSpeak").Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
