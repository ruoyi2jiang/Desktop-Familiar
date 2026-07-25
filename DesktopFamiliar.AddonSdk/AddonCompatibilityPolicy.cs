namespace DesktopFamiliar.AddonSdk;

/// <summary>Validates that an addon package can run against a host API version.</summary>
public static class AddonCompatibilityPolicy
{
    public static AddonCompatibilityResult Evaluate(AddonManifest manifest, AddonApiVersion hostApi)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.ManifestVersion != AddonManifest.CurrentManifestVersion)
            return AddonCompatibilityResult.Incompatible("The addon manifest format is not supported.");
        if (string.IsNullOrWhiteSpace(manifest.Id))
            return AddonCompatibilityResult.Incompatible("The addon ID is required.");
        if (string.IsNullOrWhiteSpace(manifest.AssemblyFileName))
            return AddonCompatibilityResult.Incompatible("The addon assembly file name is required.");
        if (string.IsNullOrWhiteSpace(manifest.EntryPoint))
            return AddonCompatibilityResult.Incompatible("The addon entry point is required.");
        if (hostApi.CompareTo(manifest.MinimumHostApi) < 0)
            return AddonCompatibilityResult.Incompatible(
                $"This addon requires host API {manifest.MinimumHostApi} or later.");
        if (manifest.MaximumHostApiExclusive is { } maximumHostApi
            && hostApi.CompareTo(maximumHostApi) >= 0)
        {
            return AddonCompatibilityResult.Incompatible(
                $"This addon supports host APIs earlier than {maximumHostApi}.");
        }

        return AddonCompatibilityResult.Compatible;
    }
}

public sealed record AddonCompatibilityResult(bool IsCompatible, string? Reason)
{
    public static AddonCompatibilityResult Compatible { get; } = new(true, null);

    public static AddonCompatibilityResult Incompatible(string reason) => new(false, reason);
}
