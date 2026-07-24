using System.Runtime.InteropServices;
using System.Threading;
using DesktopFamiliar.Models;
using DrawingPoint = System.Drawing.Point;

namespace DesktopFamiliar.Services.AutoClick;

/// <summary>Performs scheduled pointer positioning and left-button input.</summary>
public sealed class AutoClickService : IDisposable
{
    private readonly object _gate = new();
    private Timer? _timer;
    private DrawingPoint _target;
    private int _intervalMilliseconds;
    private bool _disposed;

    public bool IsClicking
    {
        get
        {
            lock (_gate)
                return _timer is not null;
        }
    }

    public event EventHandler? ClickingChanged;

    public void Start(DrawingPoint target, int intervalMilliseconds)
    {
        intervalMilliseconds = Math.Clamp(
            intervalMilliseconds,
            AppSettings.MinimumAutoClickIntervalMilliseconds,
            AppSettings.MaximumAutoClickIntervalMilliseconds);

        lock (_gate)
        {
            ThrowIfDisposed();
            StopTimer();
            _target = target;
            _intervalMilliseconds = intervalMilliseconds;
            ClickOnce();
            _timer = new Timer(_ => ClickSafely(), null, _intervalMilliseconds, _intervalMilliseconds);
        }
        ClickingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        var changed = false;
        lock (_gate)
        {
            changed = _timer is not null;
            StopTimer();
        }
        if (changed)
            ClickingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            StopTimer();
        }
    }

    private void ClickSafely()
    {
        try
        {
            lock (_gate)
            {
                if (_timer is not null)
                    ClickOnce();
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error("The auto-clicker could not send mouse input.", exception);
        }
    }

    private void ClickOnce()
    {
        if (!SetCursorPos(_target.X, _target.Y))
            throw new InvalidOperationException("Windows could not position the pointer for the auto-clicker.");

        var inputs = new[]
        {
            new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 0x0002 } } },
            new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 0x0004 } } }
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != (uint)inputs.Length)
            throw new InvalidOperationException("Windows rejected auto-click input.");
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AutoClickService));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, [In] Input[] inputs, int size);
}
