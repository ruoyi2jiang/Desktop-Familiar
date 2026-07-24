using DesktopFamiliar.Models;
using DesktopFamiliar.Views;

namespace DesktopFamiliar.Services.AutoClick;

/// <summary>Coordinates the global toggle, target-selection window, and click engine.</summary>
public sealed class AutoClickCoordinator : IDisposable
{
    private readonly CompanionManager _manager;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly AutoClickService _clickService;
    private AutoClickTargetWindow? _targetWindow;
    private bool _disposed;

    public AutoClickCoordinator(CompanionManager manager)
    {
        _manager = manager;
        _hotkeyService = new GlobalHotkeyService();
        _clickService = new AutoClickService();
        _hotkeyService.Pressed += HotkeyService_Pressed;
        _clickService.ClickingChanged += ClickService_ClickingChanged;

        if (AutoClickHotkeyPolicy.TryParse(_manager.Data.Settings.AutoClickHotkey, out var hotkey)
            && !_hotkeyService.TryRegister(hotkey))
        {
            AppLogger.Warning("The configured auto-click hotkey could not be registered.");
        }
    }

    public bool IsSelectingTarget => _targetWindow is not null;
    public bool IsClicking => _clickService.IsClicking;
    public event EventHandler? StateChanged;

    public bool TryUpdateSettings(string hotkeyText, int intervalMilliseconds)
    {
        if (!AutoClickHotkeyPolicy.TryParse(hotkeyText, out var hotkey))
            return false;
        if (!_hotkeyService.TryRegister(hotkey))
            return false;

        try
        {
            _manager.SetAutoClickSettings(hotkey.DisplayText, intervalMilliseconds);
        }
        catch
        {
            if (AutoClickHotkeyPolicy.TryParse(_manager.Data.Settings.AutoClickHotkey, out var previous))
                _hotkeyService.TryRegister(previous);
            throw;
        }

        Stop();
        return true;
    }

    public void Toggle()
    {
        ThrowIfDisposed();
        if (IsSelectingTarget || IsClicking)
        {
            Stop();
            return;
        }

        _targetWindow = new AutoClickTargetWindow();
        _targetWindow.TargetSelected += TargetWindow_TargetSelected;
        _targetWindow.Cancelled += TargetWindow_Cancelled;
        _targetWindow.BeginSelection();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        var targetWindow = _targetWindow;
        _targetWindow = null;
        if (targetWindow is not null)
        {
            targetWindow.TargetSelected -= TargetWindow_TargetSelected;
            targetWindow.Cancelled -= TargetWindow_Cancelled;
            targetWindow.Close();
        }
        _clickService.Stop();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _hotkeyService.Pressed -= HotkeyService_Pressed;
        _clickService.ClickingChanged -= ClickService_ClickingChanged;
        _clickService.Dispose();
        _hotkeyService.Dispose();
    }

    private void HotkeyService_Pressed(object? sender, EventArgs e) => Toggle();

    private void TargetWindow_TargetSelected(object? sender, AutoClickTargetSelectedEventArgs e)
    {
        if (sender != _targetWindow)
            return;

        DetachTargetWindow();
        _clickService.Start(e.Target, _manager.Data.Settings.AutoClickIntervalMilliseconds);
        AppLogger.Info($"Auto-clicking started at {e.Target.X}, {e.Target.Y}.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TargetWindow_Cancelled(object? sender, EventArgs e)
    {
        if (sender != _targetWindow)
            return;

        DetachTargetWindow();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DetachTargetWindow()
    {
        if (_targetWindow is null)
            return;
        _targetWindow.TargetSelected -= TargetWindow_TargetSelected;
        _targetWindow.Cancelled -= TargetWindow_Cancelled;
        _targetWindow = null;
    }

    private void ClickService_ClickingChanged(object? sender, EventArgs e)
    {
        if (!IsClicking)
            AppLogger.Info("Auto-clicking stopped.");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AutoClickCoordinator));
    }
}
