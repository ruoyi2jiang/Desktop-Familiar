using System.Runtime.InteropServices;
using System.Threading;
using DesktopFamiliar.Addons.AutoClicker.Models;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace DesktopFamiliar.Addons.AutoClicker.Services;

internal interface IAutoClickService : IDisposable
{
    bool IsClicking { get; }

    event EventHandler? ClickingChanged;

    bool TryStart(
        DrawingRectangle targetArea,
        int intervalMinimumMilliseconds,
        int intervalMaximumMilliseconds,
        bool useBackgroundClick);

    void UpdateIntervals(int intervalMinimumMilliseconds, int intervalMaximumMilliseconds);

    bool TryUpdateInputMode(bool useBackgroundClick);

    void Stop();
}

/// <summary>Performs scheduled cursor input or targeted background window clicks.</summary>
public sealed class AutoClickService : IAutoClickService
{
    private const uint WmMouseMove = 0x0200;
    private const uint WmLeftButtonDown = 0x0201;
    private const uint WmLeftButtonUp = 0x0202;
    private const nuint MkLeftButton = 0x0001;

    private readonly object _gate = new();
    private readonly Random _random = new();
    private Timer? _timer;
    private DrawingRectangle _targetArea;
    private BackgroundClickTarget? _backgroundTarget;
    private int _intervalMinimumMilliseconds;
    private int _intervalMaximumMilliseconds;
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

    public bool TryStart(
        DrawingRectangle targetArea,
        int intervalMinimumMilliseconds,
        int intervalMaximumMilliseconds,
        bool useBackgroundClick)
    {
        targetArea = AutoClickRandomizationPolicy.NormalizeTargetArea(targetArea);
        intervalMinimumMilliseconds = Math.Clamp(
            intervalMinimumMilliseconds,
            AutoClickerSettings.MinimumIntervalMilliseconds,
            AutoClickerSettings.MaximumIntervalMilliseconds);
        intervalMaximumMilliseconds = Math.Clamp(
            intervalMaximumMilliseconds,
            intervalMinimumMilliseconds,
            AutoClickerSettings.MaximumIntervalMilliseconds);

        var backgroundTarget = useBackgroundClick ? TryCreateBackgroundTarget(targetArea) : null;
        if (useBackgroundClick && backgroundTarget is null)
            return false;

        lock (_gate)
        {
            ThrowIfDisposed();
            StopTimer();
            _targetArea = targetArea;
            _backgroundTarget = backgroundTarget;
            _intervalMinimumMilliseconds = intervalMinimumMilliseconds;
            _intervalMaximumMilliseconds = intervalMaximumMilliseconds;
            try
            {
                ClickOnce();
                _timer = new Timer(_ => ClickSafely(), null, Timeout.Infinite, Timeout.Infinite);
                ScheduleNextClick();
            }
            catch
            {
                StopTimer();
                return false;
            }
        }
        ClickingChanged?.Invoke(this, EventArgs.Empty);
        return true;
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

    public void UpdateIntervals(int intervalMinimumMilliseconds, int intervalMaximumMilliseconds)
    {
        intervalMinimumMilliseconds = Math.Clamp(
            intervalMinimumMilliseconds,
            AutoClickerSettings.MinimumIntervalMilliseconds,
            AutoClickerSettings.MaximumIntervalMilliseconds);
        intervalMaximumMilliseconds = Math.Clamp(
            intervalMaximumMilliseconds,
            intervalMinimumMilliseconds,
            AutoClickerSettings.MaximumIntervalMilliseconds);

        lock (_gate)
        {
            ThrowIfDisposed();
            _intervalMinimumMilliseconds = intervalMinimumMilliseconds;
            _intervalMaximumMilliseconds = intervalMaximumMilliseconds;
            if (_timer is not null)
                ScheduleNextClick();
        }
    }

    public bool TryUpdateInputMode(bool useBackgroundClick)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_timer is null)
                return true;

            if (!useBackgroundClick)
            {
                _backgroundTarget = null;
                return true;
            }

            var backgroundTarget = TryCreateBackgroundTarget(_targetArea);
            if (backgroundTarget is null)
                return false;

            _backgroundTarget = backgroundTarget;
            return true;
        }
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
                {
                    ClickOnce();
                    ScheduleNextClick();
                }
            }
        }
        catch
        {
            Stop();
        }
    }

    private void ClickOnce()
    {
        var target = GetRandomTarget();
        if (_backgroundTarget is { } backgroundTarget)
        {
            SendBackgroundClick(backgroundTarget, target);
            return;
        }

        if (!SetCursorPos(target.X, target.Y))
            throw new InvalidOperationException("Windows could not position the pointer for the auto-clicker.");

        var inputs = new[]
        {
            new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 0x0002 } } },
            new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { Flags = 0x0004 } } }
        };
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != (uint)inputs.Length)
            throw new InvalidOperationException("Windows rejected auto-click input.");
    }

    private DrawingPoint GetRandomTarget() =>
        AutoClickRandomizationPolicy.GetRandomTarget(_targetArea, _random);

    private void ScheduleNextClick() => _timer?.Change(
        AutoClickRandomizationPolicy.GetRandomInterval(
            _intervalMinimumMilliseconds,
            _intervalMaximumMilliseconds,
            _random),
        Timeout.Infinite);

    private static BackgroundClickTarget? TryCreateBackgroundTarget(DrawingRectangle targetArea)
    {
        var target = new DrawingPoint(
            targetArea.Left + targetArea.Width / 2,
            targetArea.Top + targetArea.Height / 2);
        var windowHandle = WindowFromPoint(new NativePoint(target.X, target.Y));
        return windowHandle == IntPtr.Zero ? null : new BackgroundClickTarget(windowHandle);
    }

    private static void SendBackgroundClick(BackgroundClickTarget target, DrawingPoint screenPoint)
    {
        var clientPoint = new NativePoint(screenPoint.X, screenPoint.Y);
        if (!ScreenToClient(target.WindowHandle, ref clientPoint))
            throw new InvalidOperationException("Windows could not resolve the background click location.");

        var coordinates = MakeMouseLParam(clientPoint.X, clientPoint.Y);
        if (!PostMessage(target.WindowHandle, WmMouseMove, IntPtr.Zero, coordinates)
            || !PostMessage(target.WindowHandle, WmLeftButtonDown, (IntPtr)MkLeftButton, coordinates)
            || !PostMessage(target.WindowHandle, WmLeftButtonUp, IntPtr.Zero, coordinates))
        {
            throw new InvalidOperationException("Windows rejected the background click messages.");
        }
    }

    private static IntPtr MakeMouseLParam(int horizontal, int vertical) =>
        (IntPtr)((vertical << 16) | (horizontal & 0xFFFF));

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
        _backgroundTarget = null;
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

    private readonly record struct BackgroundClickTarget(IntPtr WindowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, [In] Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr windowHandle, ref NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);
}
