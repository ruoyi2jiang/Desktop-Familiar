namespace DesktopFamiliar.AddonSdk;

/// <summary>
/// Trust boundary for the initial first-party-only addon channel.
/// Third-party repositories are intentionally rejected until a separately designed trust model exists.
/// </summary>
public sealed class FirstPartyAddonCataloguePolicy
{
    public const string Publisher = "Desktop Familiar";
    public const string GitHubOwner = "ruoyi2jiang";
    public const string GitHubRepository = "Desktop-Familiar";

    public static readonly Uri CatalogueUri = new(
        $"https://raw.githubusercontent.com/{GitHubOwner}/{GitHubRepository}/main/Addons/catalogue.json");

    public AddonCompatibilityResult Evaluate(AddonPackageCatalogue catalogue, AddonApiVersion hostApi)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        if (catalogue.SchemaVersion != AddonPackageCatalogue.CurrentSchemaVersion)
            return AddonCompatibilityResult.Incompatible("The addon catalogue format is not supported.");
        if (!string.Equals(catalogue.Publisher, Publisher, StringComparison.Ordinal))
            return AddonCompatibilityResult.Incompatible("The addon catalogue publisher is not trusted.");

        var seenReleaseKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var release in catalogue.Releases)
        {
            var key = $"{release.Id}@{release.Version}";
            if (!seenReleaseKeys.Add(key))
                return AddonCompatibilityResult.Incompatible("The addon catalogue contains duplicate releases.");

            var releaseResult = AddonPackageValidationPolicy.ValidateReleaseMetadata(release);
            if (!releaseResult.IsCompatible)
                return releaseResult;
            if (!IsFirstPartyReleaseUrl(release.PackageUrl)
                || !IsFirstPartyReleaseUrl(release.ReleaseNotesUrl))
            {
                return AddonCompatibilityResult.Incompatible("The addon catalogue contains a package outside the trusted first-party repository.");
            }
        }

        return AddonCompatibilityResult.Compatible;
    }

    private static bool IsFirstPartyReleaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsolutePath.StartsWith($"/{GitHubOwner}/{GitHubRepository}/releases/", StringComparison.OrdinalIgnoreCase);
    }
}
