using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopFamiliar.Views;

/// <summary>Refreshes the overlay after Windows has finished updating monitor geometry.</summary>
internal sealed class OverlayDisplayObserver : IDisposable
{
    private const int WmDisplayChange = 0x007E;
    private readonly HwndSource _source;
    private readonly Action _refresh;
    private DispatcherOperation? _pending;
    private bool _disposed;

    internal OverlayDisplayObserver(HwndSource source, Action refresh)
    {
        _source = source;
        _refresh = refresh;
        _source.AddHook(WindowMessage);
    }

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmDisplayChange) QueueRefresh();
        return 0;
    }

    internal void QueueRefresh()
    {
        if (_disposed || _pending != null || _source.Dispatcher.HasShutdownStarted) return;
        _pending = _source.Dispatcher.InvokeAsync(() =>
        {
            _pending = null;
            _refresh();
        }, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending?.Abort();
        if (!_source.IsDisposed) _source.RemoveHook(WindowMessage);
    }
}
