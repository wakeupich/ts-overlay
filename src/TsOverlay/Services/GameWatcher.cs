using System.Diagnostics;
using System.Windows.Threading;
using TsOverlay.Models;

namespace TsOverlay.Services;

/// <summary>
/// Следит за активным окном и сообщает, когда на переднем плане игра из списка.
/// Оверлей показываем только поверх выбранных игр, чтобы не мешать в остальных приложениях.
/// </summary>
public sealed class GameWatcher
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<uint, (string Name, DateTime At)> _processNames = new();
    private List<GameProfile> _games = new();
    private bool _enabled = true;
    private IntPtr _lastHwnd;
    private Native.RECT _lastRect;

    public GameWatcher()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _timer.Tick += (_, _) => Tick();
    }

    public event Action<GameWindowInfo?>? ActiveWindowChanged;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void SetGames(IEnumerable<GameProfile> games)
    {
        _games = games.Where(g => g.Enabled && !string.IsNullOrWhiteSpace(g.Exe)).ToList();
        _lastHwnd = IntPtr.Zero;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            Reset();
        }
    }

    private void Tick()
    {
        if (!_enabled)
        {
            return;
        }

        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            Reset();
            return;
        }

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId)
        {
            // Свои окна (настройки, диалоги) игрой не считаем.
            Reset();
            return;
        }

        var name = GetProcessName(pid);
        if (name.Length == 0)
        {
            Reset();
            return;
        }

        GameProfile? profile = null;
        foreach (var game in _games)
        {
            if (game.Matches(name))
            {
                profile = game;
                break;
            }
        }

        if (profile == null || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
        {
            Reset();
            return;
        }

        if (!Native.GetWindowRect(hwnd, out var rect) || rect.Width < 120 || rect.Height < 80)
        {
            Reset();
            return;
        }

        var dpi = Native.GetDpiForWindow(hwnd);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var changed = hwnd != _lastHwnd ||
                      rect.Left != _lastRect.Left || rect.Top != _lastRect.Top ||
                      rect.Right != _lastRect.Right || rect.Bottom != _lastRect.Bottom;

        if (!changed)
        {
            return;
        }

        _lastHwnd = hwnd;
        _lastRect = rect;
        ActiveWindowChanged?.Invoke(new GameWindowInfo(profile, hwnd, rect, scale));
    }

    private void Reset()
    {
        if (_lastHwnd == IntPtr.Zero)
        {
            return;
        }

        _lastHwnd = IntPtr.Zero;
        _lastRect = default;
        ActiveWindowChanged?.Invoke(null);
    }

    private string GetProcessName(uint pid)
    {
        if (pid == 0)
        {
            return "";
        }

        var now = DateTime.UtcNow;
        if (_processNames.TryGetValue(pid, out var cached) && now - cached.At < TimeSpan.FromSeconds(10))
        {
            return cached.Name;
        }

        var name = "";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch
        {
            name = "";
        }

        _processNames[pid] = (name, now);
        if (_processNames.Count > 256)
        {
            _processNames.Clear();
        }

        return name;
    }
}
