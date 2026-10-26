using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace TsOverlay.Services;

/// <summary>Иконка в трее и её меню: включить оверлей, настройки, запуск TeamSpeak, выход.</summary>
public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _overlayItem;
    private readonly WinForms.ToolStripMenuItem _previewItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private readonly WinForms.ToolStripMenuItem _toggleItem;
    private readonly WinForms.ToolStripMenuItem _hudItem;
    private readonly WinForms.ToolStripMenuItem _alwaysItem;
    private readonly IntPtr _iconHandle;

    public TrayService()
    {
        var icon = CreateAppIcon(out var handle);
        _iconHandle = handle;

        _statusItem = new WinForms.ToolStripMenuItem("Нет подключения") { Enabled = false };
        _overlayItem = new WinForms.ToolStripMenuItem("Оверлей включён", null, (_, _) => OverlayEnabledChanged?.Invoke(!_overlayItem!.Checked))
        {
            CheckOnClick = false,
        };
        _toggleItem = new WinForms.ToolStripMenuItem("Показать/скрыть оверлей", null, (_, _) => ToggleOverlayRequested?.Invoke());
        _hudItem = new WinForms.ToolStripMenuItem("Кто говорит (поверх любого окна)", null, (_, _) => TalkingHudRequested?.Invoke());
        _alwaysItem = new WinForms.ToolStripMenuItem("Поверх любых окон (пока игра запущена)", null, (_, _) => AlwaysShowChanged?.Invoke(!_alwaysItem!.Checked))
        {
            CheckOnClick = false,
        };
        _previewItem = new WinForms.ToolStripMenuItem("Показать оверлей на рабочем столе", null, (_, _) => PreviewToggled?.Invoke());
        _startupItem = new WinForms.ToolStripMenuItem("Запускать с Windows", null, (_, _) => StartupChanged?.Invoke(!_startupItem!.Checked));

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_overlayItem);
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_hudItem);
        menu.Items.Add(_alwaysItem);
        menu.Items.Add(_previewItem);
        menu.Items.Add(new WinForms.ToolStripMenuItem("Настройки…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Запустить TeamSpeak", null, (_, _) => LaunchTeamSpeakRequested?.Invoke()));
        menu.Items.Add(new WinForms.ToolStripMenuItem("Переподключиться", null, (_, _) => RestartRequested?.Invoke()));
        menu.Items.Add(new WinForms.ToolStripMenuItem("Проверить обновления", null, (_, _) => CheckUpdatesRequested?.Invoke()));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(new WinForms.ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()));

        _icon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "TS Overlay",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke();
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
        _toggleItem.Text = string.IsNullOrWhiteSpace(toggle) ? "Показать/скрыть оверлей" : $"Показать/скрыть оверлей ({toggle})";
        _hudItem.Text = string.IsNullOrWhiteSpace(hud) ? "Кто говорит (поверх любого окна)" : $"Кто говорит ({hud})";
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


