using System.Globalization;
using System.Net.Http;
using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>
/// Application-facing facade for the first-party addon catalogue and local installation store.
/// The settings page may fetch the first-party catalogue when no releases are cached; installing always requires an explicit user action.
/// </summary>
public sealed class AddonManagerService
{
    private readonly AddonInstallationStore _installationStore;
    private readonly FirstPartyAddonCatalogueClient _catalogueClient;
    private readonly FirstPartyAddonPackageInstaller _packageInstaller;
    private AddonPackageCatalogue? _catalogue;

    public AddonManagerService(HttpClient httpClient, AddonInstallationStore installationStore)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _installationStore = installationStore ?? throw new ArgumentNullException(nameof(installationStore));
        _catalogueClient = new FirstPartyAddonCatalogueClient(httpClient);
        _packageInstaller = new FirstPartyAddonPackageInstaller(httpClient, installationStore);
    }

    public event EventHandler? Changed;

    public bool HasCatalogue => _catalogue is not null;
    public bool NeedsCatalogueRefresh => _catalogue is null || _catalogue.Releases.Count == 0;
    public bool LastCatalogueFetchWasNotFound => _catalogueClient.LastFetchWasNotFound;

    public async Task RefreshCatalogueAsync(CancellationToken cancellationToken = default)
    {
        _catalogue = await _catalogueClient.GetAsync(cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<AddonManagerEntry> GetEntries()
    {
        var installed = _installationStore.GetInstalled();
        var releases = _catalogue?.Releases
            .Where(release => AddonPackageValidationPolicy.EvaluateRelease(release, AddonSdkVersion.CurrentHostApi).IsCompatible)
            .GroupBy(release => release.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(release => release.Version, AddonVersionComparer.Instance).First())
            .ToDictionary(release => release.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, AddonPackageRelease>(StringComparer.OrdinalIgnoreCase);

        var entries = new List<AddonManagerEntry>();
        foreach (var addon in installed)
        {
            releases.TryGetValue(addon.Id, out var release);
            if (release is not null)
                releases.Remove(addon.Id);
            entries.Add(new AddonManagerEntry(
                addon.Id,
                addon.Manifest.DisplayName,
                release?.Description ?? string.Empty,
                addon,
                release));
        }

        entries.AddRange(releases.Values.Select(release =>
            new AddonManagerEntry(
                release.Id,
                release.DisplayName,
                release.Description,
                Installed: null,
                AvailableRelease: release)));
        return entries
            .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task InstallAsync(AddonPackageRelease release, CancellationToken cancellationToken = default)
    {
        await _packageInstaller.InstallAsync(release, AddonSdkVersion.CurrentHostApi, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetEnabled(string addonId, bool enabled)
    {
        _installationStore.SetEnabled(addonId, enabled);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Uninstall(string addonId, bool deleteSavedData)
    {
        _installationStore.Uninstall(addonId, deleteSavedData);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal sealed class AddonVersionComparer : IComparer<string>
    {
        public static AddonVersionComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            var parsedLeft = Parse(left);
            var parsedRight = Parse(right);
            var numeric = parsedLeft.Numeric.CompareTo(parsedRight.Numeric);
            if (numeric != 0)
                return numeric;
            if (parsedLeft.Prerelease.Length == 0 || parsedRight.Prerelease.Length == 0)
            {
                if (parsedLeft.Prerelease.Length == parsedRight.Prerelease.Length)
                    return 0;
                return parsedLeft.Prerelease.Length == 0 ? 1 : -1;
            }

            for (var index = 0; index < Math.Min(parsedLeft.Prerelease.Length, parsedRight.Prerelease.Length); index++)
            {
                var identifier = ComparePrereleaseIdentifier(
                    parsedLeft.Prerelease[index],
                    parsedRight.Prerelease[index]);
                if (identifier != 0)
                    return identifier;
            }

            return parsedLeft.Prerelease.Length.CompareTo(parsedRight.Prerelease.Length);
        }

        private static (Version Numeric, string[] Prerelease) Parse(string version)
        {
            var dashIndex = version.IndexOf('-');
            var numericText = dashIndex >= 0 ? version[..dashIndex] : version;
            var numeric = Version.TryParse(numericText, out var parsed)
                ? new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0))
                : new Version(0, 0, 0, 0);
            var prerelease = dashIndex >= 0
                ? version[(dashIndex + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries)
                : [];
            return (numeric, prerelease);
        }

        private static int ComparePrereleaseIdentifier(string left, string right)
        {
            var leftIsNumeric = left.All(char.IsAsciiDigit);
            var rightIsNumeric = right.All(char.IsAsciiDigit);
            if (leftIsNumeric != rightIsNumeric)
                return leftIsNumeric ? -1 : 1;
            if (!leftIsNumeric)
                return string.Compare(left, right, CultureInfo.InvariantCulture, CompareOptions.Ordinal);

            var length = left.TrimStart('0').Length.CompareTo(right.TrimStart('0').Length);
            return length != 0
                ? length
                : string.Compare(left.TrimStart('0'), right.TrimStart('0'), StringComparison.Ordinal);
        }
    }
}

public sealed record AddonManagerEntry(
    string Id,
    string DisplayName,
    string Description,
    InstalledAddon? Installed,
    AddonPackageRelease? AvailableRelease)
{
    public bool HasUpdate => Installed is not null
        && AvailableRelease is not null
        && AddonManagerService.AddonVersionComparer.Instance.Compare(
            AvailableRelease.Version,
            Installed.InstalledVersion) > 0;
}
