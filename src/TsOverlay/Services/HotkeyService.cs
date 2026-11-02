using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace TsOverlay.Services;

/// <summary>
/// Глобальные горячие клавиши (RegisterHotKey). Используем потоковую регистрацию:
/// WM_HOTKEY приходит в очередь потока, его ловит ComponentDispatcher WPF.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 0x4A00;
    private bool _disposed;

    public HotkeyService()
    {
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
    }

    public event Action<string>? RegistrationFailed;

    public bool Register(string gesture, Action action)
    {
        if (!TryParse(gesture, out var modifiers, out var virtualKey))
        {
            return false;
        }

        var id = _nextId++;
        if (!RegisterHotKey(IntPtr.Zero, id, modifiers, virtualKey))
        {
            Log.Info($"Горячая клавиша {gesture} не зарегистрирована (err={Marshal.GetLastWin32Error()})");
            RegistrationFailed?.Invoke(gesture);
            return false;
        }

        _handlers[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(IntPtr.Zero, id);
        }

        _handlers.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        Key? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    key = ParseKey(part);
                    break;
            }
        }

        if (key == null || key == Key.None)
        {
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static Key? ParseKey(string text)
    {
        if (text.Length == 1)
        {
            var ch = char.ToUpperInvariant(text[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return (Key)Enum.Parse(typeof(Key), ch.ToString());
            }
        }

        var aliases = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = Key.Space,
            ["tab"] = Key.Tab,
            ["enter"] = Key.Enter,
            ["return"] = Key.Enter,
            ["esc"] = Key.Escape,
            ["escape"] = Key.Escape,
            ["insert"] = Key.Insert,
            ["delete"] = Key.Delete,
            ["del"] = Key.Delete,
            ["home"] = Key.Home,
            ["end"] = Key.End,
            ["pageup"] = Key.PageUp,
            ["pagedown"] = Key.PageDown,
            ["up"] = Key.Up,
            ["down"] = Key.Down,
            ["left"] = Key.Left,
            ["right"] = Key.Right,
        };

        if (aliases.TryGetValue(text, out var alias))
        {
            return alias;
        }

        return Enum.TryParse<Key>(text, ignoreCase: true, out var parsed) ? parsed : null;
    }

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WmHotkey)
        {
            return;
        }

        var id = msg.wParam.ToInt32();
        if (!_handlers.TryGetValue(id, out var action))
        {
            return;
        }

        handled = true;
        Log.Info("Горячая клавиша нажата, id=" + id);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error("Горячая клавиша: ошибка обработчика", ex);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
