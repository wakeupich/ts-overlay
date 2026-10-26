using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TsOverlay.Models;
using TsOverlay.Services;
using TsOverlay.ViewModels;

namespace TsOverlay.Views;

/// <summary>
/// Небольшое прозрачное окно поверх игры. Клики проходят насквозь, фокус не забирает,
/// показывается только когда на переднем плане игра из списка.
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

    public OverlayWindow(OverlayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThrough();
        };

        Loaded += (_, _) => ApplyClickThrough();
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
    }

    /// <summary>Разрешить настоящее закрытие окна при выходе из приложения.</summary>
    public void AllowClose() => _allowClose = true;

    public OverlayViewModel ViewModel { get; }

    public IntPtr Handle => _hwnd;

    public bool IsShown => IsVisible;

    public void Configure(AppConfig config)
    {
        _config = config;
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

        var o = _config.Overlay;
        var anchor = profile?.Anchor ?? o.Anchor;
        var marginX = profile?.MarginX ?? o.MarginX;
        var marginY = profile?.MarginY ?? o.MarginY;

        var widthPx = Math.Max(120, (int)Math.Round(Width * dpiScale));
        var heightPx = Math.Max(40, (int)Math.Round(Height * dpiScale));
        var mx = (int)Math.Round(marginX * dpiScale);
        var my = (int)Math.Round(marginY * dpiScale);

        var x = anchor switch
        {
            "TopCenter" or "BottomCenter" => area.Left + (area.Width - widthPx) / 2,
            "TopRight" or "RightCenter" or "BottomRight" => area.Right - widthPx - mx,
            _ => area.Left + mx,
        };

        var y = anchor switch
        {
            "LeftCenter" or "RightCenter" => area.Top + (area.Height - heightPx) / 2,
            "BottomLeft" or "BottomCenter" or "BottomRight" => area.Bottom - heightPx - my,
            _ => area.Top + my,
        };

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

    private void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE).ToInt64();
        exStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE, new IntPtr(exStyle));
    }
}
