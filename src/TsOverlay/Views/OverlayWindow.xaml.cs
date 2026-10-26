using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TsOverlay.Models;
using TsOverlay.Services;
using TsOverlay.ViewModels;

namespace TsOverlay.Views;

/// <summary>
/// Небольшое прозрачное окно поверх игры. Клики проходят насквозь, фокус не забирает,
/// показывается только когда на переднем плане игра из списка. По Ctrl+Alt становится
/// интерактивным, чтобы его можно было перетащить мышью.
/// </summary>
public partial class OverlayWindow : Window
{
    private AppConfig? _config;
    private IntPtr _hwnd;
    private bool _allowClose;
    private int _lastX = int.MinValue;
    private int _lastY;
    private int _lastW;
    private int _lastH;

    private bool _interactive;
    private bool _dragging;
    private int _dragCursorX;
    private int _dragCursorY;
    private int _dragWindowX;
    private int _dragWindowY;
    private Native.RECT _currentArea;
    private double _currentDpi = 1.0;
    private Brush? _normalBorderBrush;
    private Thickness _normalBorderThickness;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        _normalBorderBrush = RootBorder.BorderBrush;
        _normalBorderThickness = RootBorder.BorderThickness;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThrough(true);
        };

        Loaded += (_, _) => ApplyClickThrough(!_interactive);

        Closing += (_, e) =>
        {
            if (_allowClose)
            {
                return;
            }

            // Окно живёт всё время работы приложения, «закрытие» = скрытие.
            e.Cancel = true;
            Hide();
        };

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
    }

    /// <summary>Пользователь перетащил оверлей: смещение в DIP от левого верхнего угла окна/рабочей области.</summary>
    public event Action<int, int>? Moved;

    public OverlayViewModel ViewModel { get; }

    public IntPtr Handle => _hwnd;

    public bool IsShown => IsVisible;

    public void Configure(AppConfig config)
    {
        _config = config;
    }

    /// <summary>Разрешить настоящее закрытие окна при выходе из приложения.</summary>
    public void AllowClose() => _allowClose = true;

    /// <summary>Включить/выключить режим перетаскивания (пока зажаты Ctrl+Alt).</summary>
    public void SetInteractive(bool enabled)
    {
        if (_dragging && !enabled)
        {
            return;
        }

        if (_interactive == enabled)
        {
            return;
        }

        _interactive = enabled;
        IsHitTestVisible = enabled;
        Cursor = enabled ? Cursors.SizeAll : null;
        ApplyClickThrough(!enabled);

        if (RootBorder != null)
        {
            var accent = new SolidColorBrush(Color.FromRgb(0x4C, 0x8B, 0xF5));
            accent.Freeze();
            RootBorder.BorderBrush = enabled ? accent : _normalBorderBrush;
            RootBorder.BorderThickness = enabled ? new Thickness(2) : _normalBorderThickness;
        }
    }

    /// <summary>Пересчитать размеры панели (после смены настроек или состава канала).</summary>
    public void RefreshLayout()
    {
        if (_config == null)
        {
            return;
        }

        var width = _config.Overlay.Width * _config.Overlay.Scale;
        Width = Math.Max(120, width);
        Height = Math.Max(40, ViewModel.PanelHeight);
        RootBorder.Padding = new Thickness(ViewModel.PanelPadding);
        RootBorder.CornerRadius = new CornerRadius(ViewModel.CornerRadius);
    }

    public void ShowOverGame(GameWindowInfo info, AppConfig config)
    {
        _config = config;
        RefreshLayout();
        if (!IsVisible)
        {
            Show();
        }

        Position(info.Rect, info.DpiScale, info.Profile);
    }

    public void ShowPreview(Native.RECT screenArea, double dpiScale, AppConfig config)
    {
        _config = config;
        RefreshLayout();
        if (!IsVisible)
        {
            Show();
        }

        Position(screenArea, dpiScale, null);
    }

    public void HideOverlay()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void Position(Native.RECT area, double dpiScale, GameProfile? profile)
    {
        if (_config == null || _hwnd == IntPtr.Zero)
        {
            return;
        }

        if (dpiScale <= 0)
        {
            var windowDpi = Native.GetDpiForWindow(_hwnd);
            dpiScale = windowDpi > 0 ? windowDpi / 96.0 : 1.0;
        }

        _currentArea = area;
        _currentDpi = dpiScale <= 0 ? 1.0 : dpiScale;

        var o = _config.Overlay;
        var anchor = profile?.Anchor ?? o.Anchor;
        var marginX = profile?.MarginX ?? o.MarginX;
        var marginY = profile?.MarginY ?? o.MarginY;

        var widthPx = Math.Max(120, (int)Math.Round(Width * dpiScale));
        var heightPx = Math.Max(40, (int)Math.Round(Height * dpiScale));
        var mx = (int)Math.Round(marginX * dpiScale);
        var my = (int)Math.Round(marginY * dpiScale);

        int x;
        int y;
        if (anchor == "Custom")
        {
            x = area.Left + (int)Math.Round(o.CustomOffsetX * dpiScale);
            y = area.Top + (int)Math.Round(o.CustomOffsetY * dpiScale);
        }
        else
        {
            x = anchor switch
            {
                "TopCenter" or "BottomCenter" => area.Left + (area.Width - widthPx) / 2,
                "TopRight" or "RightCenter" or "BottomRight" => area.Right - widthPx - mx,
                _ => area.Left + mx,
            };

            y = anchor switch
            {
                "LeftCenter" or "RightCenter" => area.Top + (area.Height - heightPx) / 2,
                "BottomLeft" or "BottomCenter" or "BottomRight" => area.Bottom - heightPx - my,
                _ => area.Top + my,
            };
        }

        // Не вылезаем за пределы окна игры, насколько это возможно.
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - widthPx));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - heightPx));

        if (IsVisible && x == _lastX && y == _lastY && widthPx == _lastW && heightPx == _lastH)
        {
            return;
        }

        _lastX = x;
        _lastY = y;
        _lastW = widthPx;
        _lastH = heightPx;

        Native.SetWindowPos(
            _hwnd,
            Native.HWND_TOPMOST,
            x,
            y,
            widthPx,
            heightPx,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_interactive || _hwnd == IntPtr.Zero || !Native.GetCursorPos(out var cursor))
        {
            return;
        }

        _dragging = true;
        _dragCursorX = cursor.X;
        _dragCursorY = cursor.Y;

        if (Native.GetWindowRect(_hwnd, out var rect))
        {
            _dragWindowX = rect.Left;
            _dragWindowY = rect.Top;
        }

        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _hwnd == IntPtr.Zero || !Native.GetCursorPos(out var cursor))
        {
            return;
        }

        var x = _dragWindowX + (cursor.X - _dragCursorX);
        var y = _dragWindowY + (cursor.Y - _dragCursorY);
        _lastX = x;
        _lastY = y;

        Native.SetWindowPos(
            _hwnd,
            Native.HWND_TOPMOST,
            x,
            y,
            0,
            0,
            Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;

        if (Native.GetWindowRect(_hwnd, out var rect))
        {
            var offsetX = (int)Math.Round((rect.Left - _currentArea.Left) / _currentDpi);
            var offsetY = (int)Math.Round((rect.Top - _currentArea.Top) / _currentDpi);
            Moved?.Invoke(offsetX, offsetY);
        }

        SetInteractive(false);
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        exStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        if (clickThrough)
        {
            exStyle |= Native.WS_EX_TRANSPARENT;
        }
        else
        {
            exStyle &= ~Native.WS_EX_TRANSPARENT;
        }

        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle));
    }
}
