using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Addons.AutoClicker.Models;
using DesktopFamiliar.Addons.AutoClicker.Services;

namespace DesktopFamiliar.Tests;

public sealed class AutoClickerCoordinatorTests
{
    [Fact]
    public void StartupHotkeyConflict_KeepsTheAddonLoadedAndReportsARepairableWarning()
    {
        var status = new RecordingStatusPresenter();
        using var coordinator = new AutoClickerCoordinator(
            new AutoClickerSettingsStore(new EmptySettingsStore()),
            new KeyStringProvider(),
            status,
            new StubHotkeyService(canRegister: false),
            new StubHotkeyService(canRegister: true),
            new StubClickService());

        Assert.True(coordinator.Settings.Enabled);
        Assert.True(coordinator.HotkeyRegistrationFailed);
        Assert.Equal(["Overlay.AutoClick.HotkeyUnavailable"], status.Messages);
    }

    [Fact]
    public void SavingIntervals_UpdatesAnActiveSessionWithoutStoppingIt()
    {
        var clickService = new StubClickService { IsClicking = true };
        using var coordinator = new AutoClickerCoordinator(
            new AutoClickerSettingsStore(new EmptySettingsStore()),
            new KeyStringProvider(),
            new RecordingStatusPresenter(),
            new StubHotkeyService(canRegister: true),
            new StubHotkeyService(canRegister: true),
            clickService);

        var updated = coordinator.TryUpdateSettings(
            AutoClickerSettings.DefaultHotkey,
            AutoClickerSettings.DefaultBackgroundModeHotkey,
            250,
            400,
            backgroundModeEnabled: false);

        Assert.True(updated);
        Assert.Equal((250, 400), clickService.Intervals);
        Assert.Equal(0, clickService.StopCount);
        Assert.True(clickService.IsClicking);
    }

    [Fact]
    public void SavingAChangedInputMode_UpdatesTheActiveSessionWithoutStoppingIt()
    {
        var clickService = new StubClickService { IsClicking = true };
        using var coordinator = new AutoClickerCoordinator(
            new AutoClickerSettingsStore(new EmptySettingsStore()),
            new KeyStringProvider(),
            new RecordingStatusPresenter(),
            new StubHotkeyService(canRegister: true),
            new StubHotkeyService(canRegister: true),
            clickService);

        var updated = coordinator.TryUpdateSettings(
            AutoClickerSettings.DefaultHotkey,
            AutoClickerSettings.DefaultBackgroundModeHotkey,
            250,
            400,
            backgroundModeEnabled: true);

        Assert.True(updated);
        Assert.True(clickService.InputMode);
        Assert.Equal(0, clickService.StopCount);
        Assert.True(clickService.IsClicking);
    }

    [Fact]
    public void InputModeHotkey_UpdatesTheActiveSessionWithoutStoppingIt()
    {
        var clickService = new StubClickService { IsClicking = true };
        var status = new RecordingStatusPresenter();
        using var coordinator = new AutoClickerCoordinator(
            new AutoClickerSettingsStore(new EmptySettingsStore()),
            new KeyStringProvider(),
            status,
            new StubHotkeyService(canRegister: true),
            new StubHotkeyService(canRegister: true),
            clickService);

        coordinator.ToggleBackgroundMode();

        Assert.True(coordinator.Settings.BackgroundModeEnabled);
        Assert.True(clickService.InputMode);
        Assert.Equal(0, clickService.StopCount);
        Assert.True(clickService.IsClicking);
        Assert.Equal(["Overlay.AutoClick.BackgroundModeActive"], status.Messages);
    }

    private sealed class EmptySettingsStore : IAddonSettingsStore
    {
        public T? Read<T>(string addonId) where T : class => null;

        public void Write<T>(string addonId, T settings) where T : class
        {
        }
    }

    private sealed class KeyStringProvider : IAddonStringProvider
    {
        public string Get(string key) => key;

        public string Format(string key, params object[] arguments) => key;
    }

    private sealed class RecordingStatusPresenter : IAddonCompanionStatusPresenter
    {
        public List<string> Messages { get; } = [];

        public void Show(string message) => Messages.Add(message);
    }

    private sealed class StubHotkeyService(bool canRegister) : IGlobalHotkeyService
    {
        public event EventHandler? Pressed
        {
            add { }
            remove { }
        }

        public bool TryRegister(AutoClickHotkey hotkey) => canRegister;

        public void Unregister()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubClickService : IAutoClickService
    {
        public bool IsClicking { get; set; }
        public int StopCount { get; private set; }
        public (int Minimum, int Maximum)? Intervals { get; private set; }
        public bool? InputMode { get; private set; }

        public event EventHandler? ClickingChanged
        {
            add { }
            remove { }
        }

        public bool TryStart(
            System.Drawing.Rectangle targetArea,
            int intervalMinimumMilliseconds,
            int intervalMaximumMilliseconds,
            bool useBackgroundClick) => true;

        public void UpdateIntervals(int intervalMinimumMilliseconds, int intervalMaximumMilliseconds) =>
            Intervals = (intervalMinimumMilliseconds, intervalMaximumMilliseconds);

        public bool TryUpdateInputMode(bool useBackgroundClick)
        {
            InputMode = useBackgroundClick;
            return true;
        }

        public void Stop()
        {
            StopCount++;
            IsClicking = false;
        }

        public void Dispose()
        {
        }
    }
}
