using System.Net.Http;
using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>Downloads and validates the pinned first-party GitHub addon catalogue.</summary>
public sealed class FirstPartyAddonCatalogueClient
{
    private readonly HttpClient _httpClient;
    private readonly FirstPartyAddonCataloguePolicy _policy;

    public FirstPartyAddonCatalogueClient(HttpClient httpClient, FirstPartyAddonCataloguePolicy? policy = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _policy = policy ?? new FirstPartyAddonCataloguePolicy();
    }

    public async Task<AddonPackageCatalogue> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            FirstPartyAddonCataloguePolicy.CatalogueUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LastFetchWasNotFound = true;
            return new AddonPackageCatalogue { Publisher = FirstPartyAddonCataloguePolicy.Publisher };
        }

        LastFetchWasNotFound = false;
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var catalogue = AddonPackageCatalogueJson.Deserialize(json);
        var compatibility = _policy.Evaluate(catalogue, AddonSdkVersion.CurrentHostApi);
        if (!compatibility.IsCompatible)
            throw new InvalidDataException(compatibility.Reason);
        return catalogue;
    }

    public bool LastFetchWasNotFound { get; private set; }
}
