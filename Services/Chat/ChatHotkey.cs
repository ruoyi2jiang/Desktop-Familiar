using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopFamiliar.Services.Chat;

public sealed class ChatHotkey : IDisposable
{
    private readonly HwndSource _window = new(new HwndSourceParameters("DesktopFamiliar.Chat")
        { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
    private string? _current;
    private int _id = 1;
    private int _key;
    private ModifierKeys _modifiers;
    internal bool IsHeld => _current != null && IsDown(_key)
        && (!_modifiers.HasFlag(ModifierKeys.Control) || IsDown(0x11))
        && (!_modifiers.HasFlag(ModifierKeys.Shift) || IsDown(0x10))
        && (!_modifiers.HasFlag(ModifierKeys.Alt) || IsDown(0x12))
        && (!_modifiers.HasFlag(ModifierKeys.Windows) || IsDown(0x5B) || IsDown(0x5C));
    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public event EventHandler? Pressed;
    public ChatHotkey() => _window.AddHook(Receive);

    public bool Set(string? shortcut)
    {
        if (_current == shortcut) return true;
        var next = 3 - _id;
        if (shortcut != null)
        {
            KeyGesture? gesture;
            try { gesture = new KeyGestureConverter().ConvertFromInvariantString(shortcut) as KeyGesture; }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or ArgumentException)
            { AppLogger.Warning("Invalid saved chat shortcut.", ex); return false; }
            if (gesture == null || gesture.Modifiers == ModifierKeys.None ||
                !RegisterHotKey(_window.Handle, next, 0x4000u | (uint)gesture.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
                return false;
            _key = KeyInterop.VirtualKeyFromKey(gesture.Key);
            _modifiers = gesture.Modifiers;
        }
        if (_current != null) UnregisterHotKey(_window.Handle, _id);
        _id = next;
        _current = shortcut;
        return true;
    }

    private IntPtr Receive(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x312 && wParam.ToInt32() == _id && _current != null)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose() { Set(null); _window.Dispose(); }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
