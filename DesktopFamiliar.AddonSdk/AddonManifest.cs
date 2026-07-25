namespace DesktopFamiliar.AddonSdk;

public sealed class AddonManifest
{
    public const int CurrentManifestVersion = 1;

    public int ManifestVersion { get; set; } = CurrentManifestVersion;
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = "0.0.0";
    public AddonApiVersion MinimumHostApi { get; set; } = AddonSdkVersion.CurrentHostApi;
    public AddonApiVersion? MaximumHostApiExclusive { get; set; }
    public string AssemblyFileName { get; set; } = string.Empty;
    public string EntryPoint { get; set; } = string.Empty;
    public List<AddonCapability> Capabilities { get; set; } = [];

    public AddonManifest Clone() => new()
    {
        ManifestVersion = ManifestVersion,
        Id = Id,
        DisplayName = DisplayName,
        Version = Version,
        MinimumHostApi = MinimumHostApi,
        MaximumHostApiExclusive = MaximumHostApiExclusive,
        AssemblyFileName = AssemblyFileName,
        EntryPoint = EntryPoint,
        Capabilities = [.. Capabilities]
    };
}

public enum AddonCapability
{
    SettingsPage,
    ViewportMenu,
    GlobalHotkeys,
    InputInjection
}
