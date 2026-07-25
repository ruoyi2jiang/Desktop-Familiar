using System.Net.Http;
using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>
/// Downloads a release selected from the already-validated first-party catalogue,
/// bounds the download size, then delegates checksum and package validation to the store.
/// </summary>
public sealed class FirstPartyAddonPackageInstaller
{
    private const long MaximumPackageDownloadBytes = 256L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly AddonInstallationStore _installationStore;
    private readonly FirstPartyAddonCataloguePolicy _cataloguePolicy;

    public FirstPartyAddonPackageInstaller(
        HttpClient httpClient,
        AddonInstallationStore installationStore,
        FirstPartyAddonCataloguePolicy? cataloguePolicy = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _installationStore = installationStore ?? throw new ArgumentNullException(nameof(installationStore));
        _cataloguePolicy = cataloguePolicy ?? new FirstPartyAddonCataloguePolicy();
    }

    public async Task<InstalledAddon> InstallAsync(
        AddonPackageRelease release,
        AddonApiVersion hostApi,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var compatibility = AddonPackageValidationPolicy.EvaluateRelease(release, hostApi);
        if (!compatibility.IsCompatible)
            throw new InvalidDataException(compatibility.Reason);
        var trust = _cataloguePolicy.Evaluate(new AddonPackageCatalogue
        {
            Publisher = FirstPartyAddonCataloguePolicy.Publisher,
            Releases = [release]
        }, hostApi);
        if (!trust.IsCompatible)
            throw new InvalidDataException(trust.Reason);
        if (!Uri.TryCreate(release.PackageUrl, UriKind.Absolute, out var packageUri))
            throw new InvalidDataException("The addon package URL is invalid.");

        var downloadDirectory = Path.Combine(_installationStore.AddonsDirectory, ".downloads");
        Directory.CreateDirectory(downloadDirectory);
        var downloadPath = Path.Combine(downloadDirectory, $"{Guid.NewGuid():N}.zip");
        try
        {
            using var response = await _httpClient.GetAsync(
                packageUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumPackageDownloadBytes)
                throw new InvalidDataException("The addon package download is larger than the supported size.");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await CopyWithLimitAsync(source, destination, cancellationToken).ConfigureAwait(false);
            }

            return _installationStore.Install(downloadPath, release, hostApi);
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
        }
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return;

            totalBytes = checked(totalBytes + read);
            if (totalBytes > MaximumPackageDownloadBytes)
                throw new InvalidDataException("The addon package download is larger than the supported size.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
