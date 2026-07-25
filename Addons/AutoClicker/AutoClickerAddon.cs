using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Addons.AutoClicker.Models;
using DesktopFamiliar.Addons.AutoClicker.Services;
using DesktopFamiliar.Addons.AutoClicker.Views;

namespace DesktopFamiliar.Addons.AutoClicker;

/// <summary>
/// The addon package entry point. It owns the Auto Clicker runtime, settings, hotkeys, input, and target picker.
/// </summary>
public sealed class AutoClickerAddon : IAddon
{
    private readonly List<IDisposable> _registrations = [];
    private AutoClickerCoordinator? _coordinator;

    public AddonManifest Manifest { get; } = AutoClickerAddonDefinition.CreateManifest();

    public void Initialize(IAddonHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var settingsStore = host.Services.GetService(typeof(IAddonSettingsStore)) as IAddonSettingsStore
            ?? throw new InvalidOperationException("Host API 1.2 settings storage is unavailable.");
        var strings = host.Services.GetService(typeof(IAddonStringProvider)) as IAddonStringProvider
            ?? throw new InvalidOperationException("Host API 1.2 localization is unavailable.");
        var statusPresenter = host.Services.GetService(typeof(IAddonCompanionStatusPresenter)) as IAddonCompanionStatusPresenter
            ?? throw new InvalidOperationException("Host API 1.2 companion status presentation is unavailable.");
        var viewportActions = host.Services.GetService(typeof(IAddonViewportActionRegistry)) as IAddonViewportActionRegistry
            ?? throw new InvalidOperationException("Host API 1.1 viewport actions are unavailable.");
        var settingsSections = host.Services.GetService(typeof(IAddonSettingsSectionRegistry)) as IAddonSettingsSectionRegistry
            ?? throw new InvalidOperationException("Host API 1.2 settings sections are unavailable.");

        _coordinator = new AutoClickerCoordinator(new AutoClickerSettingsStore(settingsStore), strings, statusPresenter);
        _registrations.Add(viewportActions.Register(new AddonViewportAction(
            "desktopfamiliar.auto-clicker.toggle",
            strings.Get("Overlay.AutoClick"),
            _coordinator.Toggle,
            () => _coordinator.Settings.Enabled)));
        _registrations.Add(settingsSections.Register(new AutoClickerSettingsSection(_coordinator, strings)));
    }

    public void Dispose()
    {
        for (var index = _registrations.Count - 1; index >= 0; index--)
            _registrations[index].Dispose();
        _registrations.Clear();
        _coordinator?.Dispose();
        _coordinator = null;
    }
}
