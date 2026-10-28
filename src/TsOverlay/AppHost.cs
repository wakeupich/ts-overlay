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
    private System.Windows.Threading.DispatcherTimer? _modifierTimer;
    private System.Windows.Threading.DispatcherTimer? _alwaysTimer;

    private GameWindowInfo? _lastGame;
    private bool _preview;
    private bool _pinned;
    private bool _suppressed;
    private bool _hudVisible;
    private bool _settingsOpen;
    private AppConfig? _previewConfig;
    private bool _previewBeforeSettings;
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
        _tray.AlwaysShowChanged += SetAlwaysShow;
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
        _tray.SetAlwaysShowChecked(Config.AlwaysShowOverlay);
        _tray.SetStatus(Config.OverlayEnabled ? "Подключение к TeamSpeak…" : "Оверлей выключен");
        InitHotkeys();

        _overlay.Moved += OnOverlayMoved;
        _modifierTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _modifierTimer.Tick += (_, _) => UpdateOverlayInteractive();
        _modifierTimer.Start();

        _alwaysTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _alwaysTimer.Tick += (_, _) => EnsureAlwaysShowState();
        _alwaysTimer.Start();

        if (Config.AutoLaunchTeamSpeak && !IsTeamSpeakRunning())
        {
            LaunchTeamSpeak();
        }

        _monitor.Start();
        Log.Info($"TS Overlay {UpdateService.CurrentVersion.ToString(3)} запущен");

        if (Config.AlwaysShowOverlay && Config.OverlayEnabled)
        {
            _viewModel.Apply(TsSnapshot.Empty, Config);
            if (ShouldShowAlways())
            {
                ShowPreview(Config);
            }
        }

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
            // Пока настройки открыты, события канала не должны откатывать оверлей
            // к сохранённому конфигу — рисуем текущий предпросмотр.
            var effective = _settingsOpen && _previewConfig != null ? _previewConfig : Config;
            _viewModel.Apply(snapshot, effective);
            _overlay?.RefreshLayout();

            if (!effective.OverlayEnabled)
            {
                _tray?.SetStatus("Оверлей выключен");
                return;
            }

            // Пока открыты настройки — не дёргаем оверлей, чтобы он не перескакивал.
            if (_settingsOpen)
            {
                if (!_overlay!.IsShown)
                {
                    ShowPreview(effective);
                }

                _tray?.SetStatus(StatusLine(snapshot));
                return;
            }

            if (_suppressed && !_hudVisible)
            {
                // Пользователь скрыл оверлей горячей клавишей — не показываем до повторного нажатия.
                _tray?.SetStatus("Оверлей скрыт");
                return;
            }

            if (_hudVisible || _pinned)
            {
                ShowOverForeground();
            }
            else if (Config.AlwaysShowOverlay)
            {
                if (ShouldShowAlways())
                {
                    ShowPreview(Config);
                }
                else
                {
                    _overlay?.HideOverlay();
                }
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

            if (_settingsOpen)
            {
                // В настройках оверлей всегда на виду и не прыгает.
                return;
            }

            if (!Config.OverlayEnabled)
            {
                return;
            }

            if (_suppressed)
            {
                // Скрыто пользователем: смена окон не должна возвращать оверлей.
                return;
            }

            if (Config.AlwaysShowOverlay)
            {
                // Висим поверх любых окон, но только пока запущена игра из списка.
                if (ShouldShowAlways())
                {
                    ShowPreview(Config);
                }
                else
                {
                    _overlay?.HideOverlay();
                }

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

        _settingsOpen = true;
        _previewBeforeSettings = _preview;
        ShowPreviewNow();

        _settings = new SettingsWindow(Config, StatusLine(_monitor?.Current ?? TsSnapshot.Empty), ApplyPreviewConfig, RestoreLiveConfig);
        _settings.Saved += () =>
        {
            _configService.Save();
            ApplyConfig(restartMonitor: true);
        };
        _settings.Closed += (_, _) =>
        {
            _settings = null;
            _settingsOpen = false;
            _previewConfig = null;
            _preview = _previewBeforeSettings;
            _tray?.SetPreviewChecked(_preview);
            RestoreLiveConfig();
        };
        _settings.Show();
    }

    /// <summary>Показать оверлей на рабочем столе (флаг --preview и кнопка в настройках).</summary>
    public void ShowPreviewNow()
    {
        _suppressed = false;
        _preview = true;
        _tray?.SetPreviewChecked(true);
        _viewModel.Apply(_monitor?.Current ?? TsSnapshot.Empty, Config);
        ShowPreview(Config);
    }

    private void SetOverlayEnabled(bool enabled)
    {
        Config.OverlayEnabled = enabled;
        if (enabled)
        {
            _suppressed = false;
        }

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

    private void SetAlwaysShow(bool enabled)
    {
        Config.AlwaysShowOverlay = enabled;
        _configService.Save();
        _tray?.SetAlwaysShowChecked(enabled);
        ApplyConfig(restartMonitor: false);
        Log.Info("Оверлей поверх любых окон: " + (enabled ? "включён" : "выключен"));
    }

    /// <summary>Запущен ли хотя бы один процесс из списка игр (не обязательно в фокусе).</summary>
    private bool AnyGameRunning()
    {
        foreach (var game in Config.Games)
        {
            if (!game.Enabled)
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(game.Exe);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    return true;
                }
            }
            catch
            {
                // процесс мог завершиться между вызовом и проверкой — не страшно
            }
        }

        return false;
    }

    /// <summary>Есть ли в настройках хотя бы одна включённая игра.</summary>
    private bool HasConfiguredGames()
    {
        foreach (var game in Config.Games)
        {
            if (game.Enabled && !string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(game.Exe)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Показывать ли оверлей в режиме «поверх любых окон»:
    /// если список игр пуст — всегда; иначе только пока запущена игра из списка.</summary>
    private bool ShouldShowAlways() => !HasConfiguredGames() || AnyGameRunning();

    /// <summary>Режим «поверх любых окон»: держим оверлей на экране, пока игра запущена
    /// (или всегда, если список игр пуст).</summary>
    private void EnsureAlwaysShowState()
    {
        if (_overlay == null || !Config.OverlayEnabled || !Config.AlwaysShowOverlay || _settingsOpen || _suppressed)
        {
            return;
        }

        if (ShouldShowAlways())
        {
            if (!_overlay.IsShown)
            {
                ShowPreview(Config);
            }
        }
        else if (!_pinned && !_hudVisible)
        {
            _overlay.HideOverlay();
        }
    }

    private void UpdateOverlayInteractive()
    {
        if (_overlay == null)
        {
            return;
        }

        var ctrl = (Native.GetAsyncKeyState(Native.VkControl) & 0x8000) != 0;
        var alt = (Native.GetAsyncKeyState(Native.VkMenu) & 0x8000) != 0;
        _overlay.SetInteractive(ctrl && alt && Config.OverlayEnabled);
    }

    private void OnOverlayMoved(int offsetX, int offsetY)
    {
        Config.Overlay.Anchor = "Custom";
        Config.Overlay.CustomOffsetX = offsetX;
        Config.Overlay.CustomOffsetY = offsetY;
        _configService.Save();
        _settings?.SyncCustomPosition(offsetX, offsetY);
        Log.Info($"Оверлей перетащен: смещение {offsetX},{offsetY}");
    }

    private void InitHotkeys()
    {        _hotkeys ??= new HotkeyService();
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

            // Ставим MSI и сразу поднимаем установленную версию: иначе после обновления
            // оверлей остаётся закрытым и выглядит как «пропал».
            var installedExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "TsOverlay", "TsOverlay.exe");
            var command = $"/c msiexec.exe /i \"{path}\" /qb & ping -n 4 127.0.0.1 >nul & start \"\" \"{installedExe}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            });
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
        if (_overlay == null)
        {
            return;
        }

        if (_overlay.IsShown && !_suppressed)
        {
            _suppressed = true;
            _pinned = false;
            _overlay.HideOverlay();
            _tray?.SetStatus("Оверлей скрыт (Ctrl+Alt+O — показать)");
            return;
        }

        _suppressed = false;
        _pinned = false;
        ShowConfiguredOverlay();
    }

    /// <summary>Показать оверлей в обычной позиции (не поверх случайного активного окна).</summary>
    private void ShowConfiguredOverlay()
    {
        if (_settingsOpen)
        {
            ShowPreview(Config);
            return;
        }

        if (Config.AlwaysShowOverlay)
        {
            ShowPreview(Config);
            return;
        }

        if (_lastGame != null)
        {
            _overlay?.ShowOverGame(_lastGame, Config);
            return;
        }

        ShowPreview(Config);
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

                if (_suppressed)
                {
                    _overlay?.HideOverlay();
                }
                else if (_pinned)
                {
                    // остаётся на месте
                }
                else if (Config.AlwaysShowOverlay)
                {
                    if (ShouldShowAlways())
                    {
                        ShowPreview(Config);
                    }
                    else
                    {
                        _overlay?.HideOverlay();
                    }
                }
                else if (_preview)
                {
                    ShowPreview(Config);
                }
                else if (_lastGame != null)
                {
                    _overlay?.ShowOverGame(_lastGame, Config);
                }
                else
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
        _suppressed = false;
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

        // DPI берём у самого окна оверлея: GetDpiForSystem врёт при масштабировании монитора.
        _overlay?.ShowPreview(rect, 0, config);
    }

    private void ApplyPreviewConfig(AppConfig config)
    {
        _previewConfig = config;
        _viewModel.Apply(_monitor?.Current ?? TsSnapshot.Empty, config);
        _overlay?.Configure(config);
        _overlay?.RefreshLayout();

        if (!config.OverlayEnabled)
        {
            _overlay?.HideOverlay();
            return;
        }

        // Настройки всегда показывают оверлей на рабочем столе, не над игрой.
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

        if (Config.AlwaysShowOverlay || _preview)
        {
            ShowPreview(Config);
        }
        else if (_lastGame != null)
        {
            _overlay?.ShowOverGame(_lastGame, Config);
        }
        else
        {
            _overlay?.HideOverlay();
        }
    }

    private void ApplyConfig(bool restartMonitor)
    {
        _tray?.SetOverlayChecked(Config.OverlayEnabled);
        _tray?.SetStartupChecked(StartupService.IsEnabled());
        _tray?.SetAlwaysShowChecked(Config.AlwaysShowOverlay);
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
        else if (_suppressed && !_settingsOpen)
        {
            _overlay?.HideOverlay();
        }
        else if (Config.AlwaysShowOverlay)
        {
            if (ShouldShowAlways())
            {
                ShowPreview(Config);
            }
            else
            {
                _overlay?.HideOverlay();
            }
        }
        else if (_preview)
        {
            ShowPreview(Config);
        }
        else if (_lastGame != null)
        {
            _overlay?.ShowOverGame(_lastGame, Config);
        }
        else
        {
            _overlay?.HideOverlay();
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

