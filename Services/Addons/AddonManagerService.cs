using System.Globalization;
using System.Net.Http;
using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>
/// Application-facing facade for the first-party addon catalogue and local installation store.
/// It performs no network work until the caller explicitly refreshes the catalogue or installs a release.
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

    private sealed class AddonVersionComparer : IComparer<string>
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
            if (parsedLeft.IsPrerelease != parsedRight.IsPrerelease)
                return parsedLeft.IsPrerelease ? -1 : 1;
            return string.Compare(left, right, CultureInfo.InvariantCulture, CompareOptions.Ordinal);
        }

        private static (Version Numeric, bool IsPrerelease) Parse(string version)
        {
            var dashIndex = version.IndexOf('-');
            var numericText = dashIndex >= 0 ? version[..dashIndex] : version;
            return (Version.TryParse(numericText, out var parsed) ? parsed : new Version(0, 0), dashIndex >= 0);
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
        && !string.Equals(Installed.InstalledVersion, AvailableRelease.Version, StringComparison.Ordinal);
}
