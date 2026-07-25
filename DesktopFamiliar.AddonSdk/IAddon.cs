namespace DesktopFamiliar.AddonSdk;

/// <summary>Implemented by an installed addon entry point after compatibility has been checked.</summary>
public interface IAddon : IDisposable
{
    AddonManifest Manifest { get; }

    void Initialize(IAddonHost host);
}

/// <summary>Stable host services exposed to addons. Additions are versioned through <see cref="ApiVersion"/>.</summary>
public interface IAddonHost
{
    AddonApiVersion ApiVersion { get; }

    IServiceProvider Services { get; }
}
