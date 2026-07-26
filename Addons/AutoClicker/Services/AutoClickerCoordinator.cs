using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Addons.AutoClicker.Models;
using DesktopFamiliar.Addons.AutoClicker.Views;

namespace DesktopFamiliar.Addons.AutoClicker.Services;

/// <summary>Coordinates the Auto Clicker's hotkeys, target picker, persisted settings, and input engine.</summary>
public sealed class AutoClickerCoordinator : IDisposable
{
    private readonly AutoClickerSettingsStore _settingsStore;
    private readonly IAddonStringProvider _strings;
    private readonly IAddonCompanionStatusPresenter _statusPresenter;
    private readonly IGlobalHotkeyService _toggleHotkeyService;
    private readonly IGlobalHotkeyService _backgroundModeHotkeyService;
    private readonly IAutoClickService _clickService;
    private AutoClickerSettings _settings;
    private AutoClickTargetWindow? _targetWindow;
    private bool _hotkeyRegistrationFailed;
    private bool _disposed;

    public AutoClickerCoordinator(
        AutoClickerSettingsStore settingsStore,
        IAddonStringProvider strings,
        IAddonCompanionStatusPresenter statusPresenter)
        : this(
            settingsStore,
            strings,
            statusPresenter,
            new GlobalHotkeyService("Desktop Familiar Auto Clicker toggle hotkey"),
            new GlobalHotkeyService("Desktop Familiar Auto Clicker input-mode hotkey"),
            new AutoClickService())
    {
    }

    internal AutoClickerCoordinator(
        AutoClickerSettingsStore settingsStore,
        IAddonStringProvider strings,
        IAddonCompanionStatusPresenter statusPresenter,
        IGlobalHotkeyService toggleHotkeyService,
        IGlobalHotkeyService backgroundModeHotkeyService,
        IAutoClickService clickService)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _statusPresenter = statusPresenter ?? throw new ArgumentNullException(nameof(statusPresenter));
        _toggleHotkeyService = toggleHotkeyService ?? throw new ArgumentNullException(nameof(toggleHotkeyService));
        _backgroundModeHotkeyService = backgroundModeHotkeyService ?? throw new ArgumentNullException(nameof(backgroundModeHotkeyService));
        _clickService = clickService ?? throw new ArgumentNullException(nameof(clickService));
        _settings = _settingsStore.Load();
        _toggleHotkeyService.Pressed += ToggleHotkeyService_Pressed;
        _backgroundModeHotkeyService.Pressed += BackgroundModeHotkeyService_Pressed;
        _clickService.ClickingChanged += ClickService_ClickingChanged;

        if (_settings.Enabled && !TryRegisterConfiguredHotkeys())
        {
            _hotkeyRegistrationFailed = true;
            _statusPresenter.Show(_strings.Get("Overlay.AutoClick.HotkeyUnavailable"));
        }
    }

    public AutoClickerSettings Settings => _settings.Clone();
    public bool IsSelectingTarget => _targetWindow is not null;
    public bool IsClicking => _clickService.IsClicking;
    public bool HotkeyRegistrationFailed => _hotkeyRegistrationFailed;
    public event EventHandler? StateChanged;

    public bool TryUpdateSettings(
        string hotkeyText,
        string backgroundModeHotkeyText,
        int intervalMilliseconds,
        int intervalMaximumMilliseconds,
        bool backgroundModeEnabled)
    {
        ThrowIfDisposed();
        if (!AutoClickHotkeyPolicy.TryParse(hotkeyText, out var hotkey)
            || !AutoClickHotkeyPolicy.TryParse(backgroundModeHotkeyText, out var backgroundModeHotkey)
            || string.Equals(hotkey.DisplayText, backgroundModeHotkey.DisplayText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var previous = _settings;
        if (previous.Enabled && !TryRegisterHotkeys(hotkey, backgroundModeHotkey))
        {
            _hotkeyRegistrationFailed = !TryRegisterSavedHotkeys(previous.Hotkey, previous.BackgroundModeHotkey);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        var next = previous.Clone();
        next.Hotkey = hotkey.DisplayText;
        next.BackgroundModeHotkey = backgroundModeHotkey.DisplayText;
        next.IntervalMilliseconds = intervalMilliseconds;
        next.IntervalMaximumMilliseconds = intervalMaximumMilliseconds;
        next.BackgroundModeEnabled = backgroundModeEnabled;
        try
        {
            _settingsStore.Save(next);
            _settings = next;
        }
        catch
        {
            if (previous.Enabled)
                _hotkeyRegistrationFailed = !TryRegisterSavedHotkeys(previous.Hotkey, previous.BackgroundModeHotkey);
            throw;
        }

        _hotkeyRegistrationFailed = false;
        _clickService.UpdateIntervals(next.IntervalMilliseconds, next.IntervalMaximumMilliseconds);
        ApplyInputModeChange(previous.BackgroundModeEnabled, next.BackgroundModeEnabled);
        return true;
    }

    public bool TrySetEnabled(bool enabled)
    {
        ThrowIfDisposed();
        if (_settings.Enabled == enabled)
            return true;

        var previous = _settings;
        if (enabled && !TryRegisterConfiguredHotkeys())
        {
            _hotkeyRegistrationFailed = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        if (!enabled)
        {
            Stop();
            UnregisterHotkeys();
        }

        var next = previous.Clone();
        next.Enabled = enabled;
        try
        {
            _settingsStore.Save(next);
            _settings = next;
        }
        catch
        {
            if (enabled)
                UnregisterHotkeys();
            else
                TryRegisterSavedHotkeys(previous.Hotkey, previous.BackgroundModeHotkey);
            throw;
        }

        _hotkeyRegistrationFailed = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Toggle()
    {
        ThrowIfDisposed();
        if (!_settings.Enabled)
            return;
        if (IsSelectingTarget || IsClicking)
        {
            Stop();
            return;
        }

        _targetWindow = new AutoClickTargetWindow(_strings);
        _targetWindow.TargetSelected += TargetWindow_TargetSelected;
        _targetWindow.Cancelled += TargetWindow_Cancelled;
        _targetWindow.BeginSelection();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleBackgroundMode()
    {
        ThrowIfDisposed();
        if (!_settings.Enabled)
            return;

        var wasClicking = IsClicking;
        var previousMode = _settings.BackgroundModeEnabled;
        var next = _settings.Clone();
        next.BackgroundModeEnabled = !next.BackgroundModeEnabled;
        _settingsStore.Save(next);
        _settings = next;
        var modeChangedForActiveSession = ApplyInputModeChange(previousMode, next.BackgroundModeEnabled);
        _statusPresenter.Show(_strings.Get(wasClicking && modeChangedForActiveSession
            ? next.BackgroundModeEnabled
                ? "Overlay.AutoClick.BackgroundModeActive"
                : "Overlay.AutoClick.CursorModeActive"
            : next.BackgroundModeEnabled
                ? "Overlay.AutoClick.BackgroundModeEnabled"
                : "Overlay.AutoClick.CursorModeEnabled"));
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

    private void ToggleHotkeyService_Pressed(object? sender, EventArgs e) => Toggle();

    private void BackgroundModeHotkeyService_Pressed(object? sender, EventArgs e) => ToggleBackgroundMode();

    private void TargetWindow_TargetSelected(object? sender, AutoClickTargetSelectedEventArgs e)
    {
        if (sender != _targetWindow)
            return;

        DetachTargetWindow();
        if (!_clickService.TryStart(
                e.TargetArea,
                _settings.IntervalMilliseconds,
                _settings.IntervalMaximumMilliseconds,
                _settings.BackgroundModeEnabled))
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
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
        _statusPresenter.Show(_strings.Get(IsClicking ? "Overlay.AutoClick.Started" : "Overlay.AutoClick.Stopped"));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool ApplyInputModeChange(bool previousMode, bool nextMode)
    {
        if (previousMode == nextMode)
            return IsClicking;

        return _clickService.TryUpdateInputMode(nextMode);
    }

    private bool TryRegisterConfiguredHotkeys() =>
        TryRegisterSavedHotkeys(_settings.Hotkey, _settings.BackgroundModeHotkey);

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
            throw new ObjectDisposedException(nameof(AutoClickerCoordinator));
    }
}
