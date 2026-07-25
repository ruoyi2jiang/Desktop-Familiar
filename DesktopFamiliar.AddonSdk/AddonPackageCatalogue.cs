using System.Security.Cryptography;
using System.Text.Json;
using System.IO;

namespace DesktopFamiliar.AddonSdk;

/// <summary>
/// A signed-in-the-repository index of first-party addon package releases.
/// Package bytes are verified against <see cref="Sha256"/> before installation.
/// </summary>
public sealed class AddonPackageCatalogue
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Publisher { get; set; } = string.Empty;
    public List<AddonPackageRelease> Releases { get; set; } = [];
}

/// <summary>One installable addon release advertised by a trusted catalogue.</summary>
public sealed class AddonPackageRelease
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public AddonApiVersion MinimumHostApi { get; set; } = AddonSdkVersion.CurrentHostApi;
    public AddonApiVersion? MaximumHostApiExclusive { get; set; }
    public string PackageUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string ReleaseNotesUrl { get; set; } = string.Empty;
}

/// <summary>Canonical JSON options for first-party addon catalogues.</summary>
public static class AddonPackageCatalogueJson
{
    public static AddonPackageCatalogue Deserialize(string json) =>
        JsonSerializer.Deserialize<AddonPackageCatalogue>(json, AddonManifestJson.Options)
        ?? throw new JsonException("The addon catalogue did not contain an object.");
}

/// <summary>Pure validation for package metadata and package checksum comparisons.</summary>
public static class AddonPackageValidationPolicy
{
    public static AddonCompatibilityResult EvaluateRelease(AddonPackageRelease release, AddonApiVersion hostApi)
    {
        ArgumentNullException.ThrowIfNull(release);

        var metadataResult = ValidateReleaseMetadata(release);
        if (!metadataResult.IsCompatible)
            return metadataResult;

        var manifestResult = AddonCompatibilityPolicy.Evaluate(new AddonManifest
        {
            Id = release.Id,
            DisplayName = release.DisplayName,
            AssemblyFileName = "catalogue-release.dll",
            EntryPoint = "catalogue-release",
            MinimumHostApi = release.MinimumHostApi,
            MaximumHostApiExclusive = release.MaximumHostApiExclusive
        }, hostApi);
        if (!manifestResult.IsCompatible)
            return manifestResult;
        return manifestResult;
    }

    public static AddonCompatibilityResult ValidateReleaseMetadata(AddonPackageRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (string.IsNullOrWhiteSpace(release.Id))
            return AddonCompatibilityResult.Incompatible("The addon package ID is required.");
        if (string.IsNullOrWhiteSpace(release.DisplayName))
            return AddonCompatibilityResult.Incompatible("The addon package display name is required.");
        if (string.IsNullOrWhiteSpace(release.Description))
            return AddonCompatibilityResult.Incompatible("The addon package description is required.");
        if (string.IsNullOrWhiteSpace(release.Version))
            return AddonCompatibilityResult.Incompatible("The addon package version is required.");
        if (!Uri.TryCreate(release.PackageUrl, UriKind.Absolute, out var packageUri)
            || packageUri.Scheme != Uri.UriSchemeHttps)
        {
            return AddonCompatibilityResult.Incompatible("The addon package URL must use HTTPS.");
        }
        if (!Uri.TryCreate(release.ReleaseNotesUrl, UriKind.Absolute, out var notesUri)
            || notesUri.Scheme != Uri.UriSchemeHttps)
        {
            return AddonCompatibilityResult.Incompatible("The addon release-notes URL must use HTTPS.");
        }
        if (!IsSha256(release.Sha256))
            return AddonCompatibilityResult.Incompatible("The addon package SHA-256 checksum is invalid.");

        return AddonCompatibilityResult.Compatible;
    }

    public static bool MatchesSha256(Stream packageStream, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        if (!IsSha256(expectedSha256))
            return false;

        var actual = Convert.ToHexString(SHA256.HashData(packageStream));
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSha256(string? checksum) =>
        checksum is { Length: 64 }
        && checksum.All(character => char.IsAsciiHexDigit(character));
}
