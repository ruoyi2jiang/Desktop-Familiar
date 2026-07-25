using DesktopFamiliar.Models;
using DesktopFamiliar.Views;

namespace DesktopFamiliar.Services.AutoClick;

public sealed class AutoClickStatusChangedEventArgs(bool isClicking) : EventArgs
{
    public bool IsClicking { get; } = isClicking;
}

/// <summary>Coordinates the global toggle, target-selection window, and click engine.</summary>
public sealed class AutoClickCoordinator : IDisposable
{
    private readonly CompanionManager _manager;
    private readonly GlobalHotkeyService _toggleHotkeyService;
    private readonly GlobalHotkeyService _backgroundModeHotkeyService;
    private readonly AutoClickService _clickService;
    private AutoClickTargetWindow? _targetWindow;
    private bool _disposed;

    public AutoClickCoordinator(CompanionManager manager)
    {
        _manager = manager;
        _toggleHotkeyService = new GlobalHotkeyService();
        _backgroundModeHotkeyService = new GlobalHotkeyService();
        _clickService = new AutoClickService();
        _toggleHotkeyService.Pressed += ToggleHotkeyService_Pressed;
        _backgroundModeHotkeyService.Pressed += BackgroundModeHotkeyService_Pressed;
        _clickService.ClickingChanged += ClickService_ClickingChanged;

        if (_manager.Data.Settings.AutoClickEnabled
            && !TryRegisterConfiguredHotkeys())
        {
            AppLogger.Warning("The configured auto-click hotkeys could not be registered.");
        }
    }

    public bool IsSelectingTarget => _targetWindow is not null;
    public bool IsClicking => _clickService.IsClicking;
    public event EventHandler? StateChanged;
    public event EventHandler<AutoClickStatusChangedEventArgs>? ClickingChanged;

    public bool TryUpdateSettings(
        string hotkeyText,
        string backgroundModeHotkeyText,
        int intervalMilliseconds,
        int intervalMaximumMilliseconds,
        bool backgroundModeEnabled)
    {
        if (!AutoClickHotkeyPolicy.TryParse(hotkeyText, out var hotkey))
            return false;
        if (!AutoClickHotkeyPolicy.TryParse(backgroundModeHotkeyText, out var backgroundModeHotkey)
            || string.Equals(hotkey.DisplayText, backgroundModeHotkey.DisplayText, StringComparison.OrdinalIgnoreCase))
            return false;

        var previousToggleHotkey = _manager.Data.Settings.AutoClickHotkey;
        var previousBackgroundModeHotkey = _manager.Data.Settings.AutoClickBackgroundModeHotkey;
        if (_manager.Data.Settings.AutoClickEnabled
            && !TryRegisterHotkeys(hotkey, backgroundModeHotkey))
        {
            TryRegisterSavedHotkeys(previousToggleHotkey, previousBackgroundModeHotkey);
            return false;
        }

        try
        {
            _manager.SetAutoClickSettings(
                hotkey.DisplayText,
                backgroundModeHotkey.DisplayText,
                intervalMilliseconds,
                intervalMaximumMilliseconds,
                backgroundModeEnabled);
        }
        catch
        {
            if (_manager.Data.Settings.AutoClickEnabled)
                TryRegisterSavedHotkeys(previousToggleHotkey, previousBackgroundModeHotkey);
            throw;
        }

        Stop();
        return true;
    }

    public bool TrySetEnabled(bool enabled)
    {
        ThrowIfDisposed();
        if (_manager.Data.Settings.AutoClickEnabled == enabled)
            return true;

        if (enabled)
        {
            if (!TryRegisterConfiguredHotkeys())
            {
                return false;
            }

            try
            {
                _manager.SetAutoClickEnabled(true);
            }
            catch
            {
                UnregisterHotkeys();
                throw;
            }

            return true;
        }

        Stop();
        UnregisterHotkeys();
        try
        {
            _manager.SetAutoClickEnabled(false);
        }
        catch
        {
            TryRegisterConfiguredHotkeys();
            throw;
        }

        return true;
    }

    public void Toggle()
    {
        ThrowIfDisposed();
        if (!_manager.Data.Settings.AutoClickEnabled)
            return;
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

    public void ToggleBackgroundMode()
    {
        ThrowIfDisposed();
        if (!_manager.Data.Settings.AutoClickEnabled)
            return;

        _manager.SetAutoClickBackgroundModeEnabled(!_manager.Data.Settings.AutoClickBackgroundModeEnabled);
        if (IsSelectingTarget || IsClicking)
            Stop();
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
        _toggleHotkeyService.Pressed -= ToggleHotkeyService_Pressed;
        _backgroundModeHotkeyService.Pressed -= BackgroundModeHotkeyService_Pressed;
        _clickService.ClickingChanged -= ClickService_ClickingChanged;
        _clickService.Dispose();
        _toggleHotkeyService.Dispose();
        _backgroundModeHotkeyService.Dispose();
    }

    private void ToggleHotkeyService_Pressed(object? sender, EventArgs e)
    {
        if (_manager.Data.Settings.AutoClickEnabled)
            Toggle();
    }

    private void BackgroundModeHotkeyService_Pressed(object? sender, EventArgs e)
    {
        if (_manager.Data.Settings.AutoClickEnabled)
            ToggleBackgroundMode();
    }

    private void TargetWindow_TargetSelected(object? sender, AutoClickTargetSelectedEventArgs e)
    {
        if (sender != _targetWindow)
            return;

        DetachTargetWindow();
        var backgroundModeEnabled = _manager.Data.Settings.AutoClickBackgroundModeEnabled;
        if (!_clickService.TryStart(
                e.TargetArea,
                _manager.Data.Settings.AutoClickIntervalMilliseconds,
                _manager.Data.Settings.AutoClickIntervalMaximumMilliseconds,
                backgroundModeEnabled))
        {
            AppLogger.Warning($"Auto-clicking could not start because no window accepted the target area at {e.TargetArea.Left}, {e.TargetArea.Top}.");
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        AppLogger.Info($"Auto-clicking started in a {e.TargetArea.Width}x{e.TargetArea.Height} area at {e.TargetArea.Left}, {e.TargetArea.Top} using {(backgroundModeEnabled ? "background" : "cursor")} input.");
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
        if (IsClicking)
            AppLogger.Info("Auto-clicking started.");
        else
            AppLogger.Info("Auto-clicking stopped.");
        ClickingChanged?.Invoke(this, new AutoClickStatusChangedEventArgs(IsClicking));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryRegisterConfiguredHotkeys() =>
        TryRegisterSavedHotkeys(
            _manager.Data.Settings.AutoClickHotkey,
            _manager.Data.Settings.AutoClickBackgroundModeHotkey);

    private bool TryRegisterSavedHotkeys(string toggleHotkeyText, string backgroundModeHotkeyText) =>
        AutoClickHotkeyPolicy.TryParse(toggleHotkeyText, out var toggleHotkey)
        && AutoClickHotkeyPolicy.TryParse(backgroundModeHotkeyText, out var backgroundModeHotkey)
        && TryRegisterHotkeys(toggleHotkey, backgroundModeHotkey);

    private bool TryRegisterHotkeys(AutoClickHotkey toggleHotkey, AutoClickHotkey backgroundModeHotkey)
    {
        if (string.Equals(toggleHotkey.DisplayText, backgroundModeHotkey.DisplayText, StringComparison.OrdinalIgnoreCase)
            || !_toggleHotkeyService.TryRegister(toggleHotkey))
        {
            return false;
        }

        if (_backgroundModeHotkeyService.TryRegister(backgroundModeHotkey))
            return true;

        _toggleHotkeyService.Unregister();
        return false;
    }

    private void UnregisterHotkeys()
    {
        _toggleHotkeyService.Unregister();
        _backgroundModeHotkeyService.Unregister();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AutoClickCoordinator));
    }
}
