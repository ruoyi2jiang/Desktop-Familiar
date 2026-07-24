using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopFamiliar.Services.AutoClick;

/// <summary>Owns one message-only Windows hotkey registration.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x4450;
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _window;
    private AutoClickHotkey? _registeredHotkey;

    public GlobalHotkeyService()
    {
        _window = new HwndSource(new HwndSourceParameters("Desktop Familiar auto-click hotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _window.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public bool TryRegister(AutoClickHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        if (_registeredHotkey == hotkey)
            return true;

        var previous = _registeredHotkey;
        if (previous is not null)
            UnregisterHotKey(_window.Handle, HotkeyId);
        _registeredHotkey = null;

        if (RegisterHotKey(_window.Handle, HotkeyId, hotkey.Modifiers, hotkey.VirtualKey))
        {
            _registeredHotkey = hotkey;
            return true;
        }

        if (previous is not null && RegisterHotKey(_window.Handle, HotkeyId, previous.Modifiers, previous.VirtualKey))
            _registeredHotkey = previous;
        return false;
    }

    public void Dispose()
    {
        if (_registeredHotkey is not null)
            UnregisterHotKey(_window.Handle, HotkeyId);
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
