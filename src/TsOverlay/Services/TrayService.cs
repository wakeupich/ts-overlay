using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TsOverlay.Services;

/// <summary>Иконка в трее и её меню: включить оверлей, настройки, запуск TeamSpeak, выход.</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _versionItem;
    private readonly WinForms.ToolStripMenuItem _overlayItem;
    private readonly WinForms.ToolStripMenuItem _previewItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private readonly WinForms.ToolStripMenuItem _toggleItem;
    private readonly WinForms.ToolStripMenuItem _hudItem;
    private readonly WinForms.ToolStripMenuItem _alwaysItem;
    private readonly WinForms.ToolStripMenuItem _dragHintItem;
    private readonly WinForms.ToolStripMenuItem _settingsItem;
    private readonly WinForms.ToolStripMenuItem _launchItem;
    private readonly WinForms.ToolStripMenuItem _reconnectItem;
    private readonly WinForms.ToolStripMenuItem _updatesItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private readonly IntPtr _iconHandle;
    private string _currentToggleHotkey = "";
    private string _currentHudHotkey = "";

    public TrayService()
    {
        var icon = CreateAppIcon(out var handle);
        _iconHandle = handle;

        _statusItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_NoConnection")) { Enabled = false };
        _versionItem = new WinForms.ToolStripMenuItem($"Версия {UpdateService.CurrentVersion.ToString(3)}") { Enabled = false };
        _overlayItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_OverlayEnabled"), null, (_, _) => OverlayEnabledChanged?.Invoke(!_overlayItem!.Checked))
        {
            CheckOnClick = false,
        };
        _toggleItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_ToggleOverlay"), null, (_, _) => ToggleOverlayRequested?.Invoke());
        _hudItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_WhoTalking"), null, (_, _) => TalkingHudRequested?.Invoke());
        _alwaysItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_AlwaysOnTop"), null, (_, _) => AlwaysShowChanged?.Invoke(!_alwaysItem!.Checked))
        {
            CheckOnClick = false,
        };
        _dragHintItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_DragHint")) { Enabled = false };
        _previewItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_PreviewDesktop"), null, (_, _) => PreviewToggled?.Invoke());
        _settingsItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_Settings"), null, (_, _) => SettingsRequested?.Invoke());
        _launchItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_LaunchTs"), null, (_, _) => LaunchTeamSpeakRequested?.Invoke());
        _reconnectItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_Reconnect"), null, (_, _) => RestartRequested?.Invoke());
        _updatesItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_CheckUpdates"), null, (_, _) => CheckUpdatesRequested?.Invoke());
        _startupItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_StartWithWindows"), null, (_, _) => StartupChanged?.Invoke(!_startupItem!.Checked));
        _exitItem = new WinForms.ToolStripMenuItem(Loc.Tr("Tray_Exit"), null, (_, _) => ExitRequested?.Invoke());

        var version = UpdateService.CurrentVersion.ToString(3);
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_versionItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_overlayItem);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_hudItem);
        menu.Items.Add(_alwaysItem);
        menu.Items.Add(_dragHintItem);
        menu.Items.Add(_previewItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_launchItem);
        menu.Items.Add(_reconnectItem);
        menu.Items.Add(_updatesItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _icon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = $"TS Overlay {version}",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();

        Loc.LanguageChanged += OnLanguageChanged;
        UpdateTexts();
    }

    private void OnLanguageChanged()
    {
        UpdateTexts();
    }

    private void UpdateTexts()
    {
        var v = UpdateService.CurrentVersion.ToString(3);
        _versionItem.Text = Loc.CurrentLanguage == "en" ? $"Version {v}" : $"Версия {v}";
        _overlayItem.Text = Loc.Tr("Tray_OverlayEnabled");
        _alwaysItem.Text = Loc.Tr("Tray_AlwaysOnTop");
        _dragHintItem.Text = Loc.Tr("Tray_DragHint");
        _previewItem.Text = Loc.Tr("Tray_PreviewDesktop");
        _settingsItem.Text = Loc.Tr("Tray_Settings");
        _launchItem.Text = Loc.Tr("Tray_LaunchTs");
        _reconnectItem.Text = Loc.Tr("Tray_Reconnect");
        _updatesItem.Text = Loc.Tr("Tray_CheckUpdates");
        _startupItem.Text = Loc.Tr("Tray_StartWithWindows");
        _exitItem.Text = Loc.Tr("Tray_Exit");
        SetHotkeys(_currentToggleHotkey, _currentHudHotkey);
    }

    public event Action? SettingsRequested;

    public event Action<bool>? OverlayEnabledChanged;

    public event Action? PreviewToggled;

    public event Action? ToggleOverlayRequested;

    public event Action? TalkingHudRequested;

    public event Action<bool>? AlwaysShowChanged;

    public event Action<bool>? StartupChanged;

    public event Action? RestartRequested;

    public event Action? LaunchTeamSpeakRequested;

    public event Action? CheckUpdatesRequested;

    public event Action? ExitRequested;

    public void SetStatus(string status)
    {
        _statusItem.Text = status;
        var text = "TS Overlay — " + status;
        // NotifyIcon.Text ограничен 63 символами.
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void SetOverlayChecked(bool value) => _overlayItem.Checked = value;

    public void SetAlwaysShowChecked(bool value) => _alwaysItem.Checked = value;

    public void SetHotkeys(string toggle, string hud)
    {
        _currentToggleHotkey = toggle;
        _currentHudHotkey = hud;
        var toggleBase = Loc.Tr("Tray_ToggleOverlay");
        var hudBase = Loc.Tr("Tray_WhoTalking");
        _toggleItem.Text = string.IsNullOrWhiteSpace(toggle) ? toggleBase : $"{toggleBase} ({toggle})";
        _hudItem.Text = string.IsNullOrWhiteSpace(hud) ? hudBase : $"{hudBase} ({hud})";
    }

    public void SetPreviewChecked(bool value) => _previewItem.Checked = value;

    public void SetStartupChecked(bool value) => _startupItem.Checked = value;

    public void ShowBalloon(string message, bool error = false)
    {
        try
        {
            _icon.ShowBalloonTip(4000, "TS Overlay", message, error ? WinForms.ToolTipIcon.Warning : WinForms.ToolTipIcon.Info);
        }
        catch
        {
            // не критично
        }
    }

    public void Dispose()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        try
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        catch
        {
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }

    private static Drawing.Icon CreateAppIcon(out IntPtr handle)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
            if (resource != null)
            {
                using var stream = resource.Stream;
                handle = IntPtr.Zero;
                return new Drawing.Icon(stream);
            }
        }
        catch
        {
            // ниже сгенерируем иконку вручную
        }

        return CreateIcon(out handle);
    }

    private static Drawing.Icon CreateIcon(out IntPtr handle)
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.Transparent);

        using (var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 34, 122, 220)))
        {
            graphics.FillEllipse(brush, 1, 1, 30, 30);
        }

        using var font = new Drawing.Font("Segoe UI", 11f, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
        };
        graphics.DrawString("TS", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 0, 32, 32), format);

        handle = bitmap.GetHicon();
        return Drawing.Icon.FromHandle(handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
