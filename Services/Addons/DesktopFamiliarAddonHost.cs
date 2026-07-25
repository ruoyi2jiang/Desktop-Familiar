using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>
/// Narrow host-service provider available to loaded addons. New services are added only with a documented SDK API bump.
/// </summary>
public sealed class DesktopFamiliarAddonHost : IAddonHost, IServiceProvider
{
    private readonly IAddonViewportActionRegistry _viewportActions;
    private readonly IAddonSettingsSectionRegistry _settingsSections;
    private readonly IAddonSettingsStore _settingsStore;
    private readonly IAddonStringProvider _strings;
    private readonly IAddonCompanionStatusPresenter _statusPresenter;

    public DesktopFamiliarAddonHost(
        IAddonViewportActionRegistry viewportActions,
        IAddonSettingsSectionRegistry settingsSections,
        IAddonSettingsStore settingsStore,
        IAddonStringProvider strings,
        IAddonCompanionStatusPresenter statusPresenter)
    {
        _viewportActions = viewportActions ?? throw new ArgumentNullException(nameof(viewportActions));
        _settingsSections = settingsSections ?? throw new ArgumentNullException(nameof(settingsSections));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _statusPresenter = statusPresenter ?? throw new ArgumentNullException(nameof(statusPresenter));
    }

    public AddonApiVersion ApiVersion => AddonSdkVersion.CurrentHostApi;
    public IServiceProvider Services => this;

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(IAddonViewportActionRegistry))
            return _viewportActions;
        if (serviceType == typeof(IAddonSettingsSectionRegistry))
            return _settingsSections;
        if (serviceType == typeof(IAddonSettingsStore))
            return _settingsStore;
        if (serviceType == typeof(IAddonStringProvider))
            return _strings;
        return serviceType == typeof(IAddonCompanionStatusPresenter) ? _statusPresenter : null;
    }
}
