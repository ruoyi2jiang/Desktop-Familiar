using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopFamiliar.AddonSdk;

/// <summary>Canonical JSON options for addon manifests and future package catalogues.</summary>
public static class AddonManifestJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AddonManifest Deserialize(string json) =>
        JsonSerializer.Deserialize<AddonManifest>(json, Options)
        ?? throw new JsonException("The addon manifest did not contain an object.");
}
