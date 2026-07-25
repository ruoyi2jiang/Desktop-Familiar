using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Addons.AutoClicker.Models;

/// <summary>First-party identity and declared capabilities for the Auto Clicker addon.</summary>
public static class AutoClickerAddonDefinition
{
    public const string Id = "desktopfamiliar.auto-clicker";

    public static AddonManifest CreateManifest() => new()
    {
        Id = Id,
        DisplayName = "Auto clicker",
        Version = "1.0.0",
        MinimumHostApi = new AddonApiVersion(1, 2),
        MaximumHostApiExclusive = new AddonApiVersion(2, 0),
        AssemblyFileName = "DesktopFamiliar.Addons.AutoClicker.dll",
        EntryPoint = "DesktopFamiliar.Addons.AutoClicker.AutoClickerAddon",
        Capabilities =
        [
            AddonCapability.SettingsPage,
            AddonCapability.ViewportMenu,
            AddonCapability.GlobalHotkeys,
            AddonCapability.InputInjection
        ]
    };
}
