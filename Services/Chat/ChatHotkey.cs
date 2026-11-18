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
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
