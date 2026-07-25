using System.Text.Json;
using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>Atomically persists JSON settings beneath one addon's data directory.</summary>
public sealed class AddonSettingsStore : IAddonSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _dataDirectory;

    public AddonSettingsStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _dataDirectory = Path.Combine(Path.GetFullPath(rootDirectory), "Addons", "data");
    }

    public T? Read<T>(string addonId) where T : class
    {
        var path = GetSettingsPath(addonId);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"The saved settings for addon '{addonId}' are empty.");
    }

    public void Write<T>(string addonId, T settings) where T : class
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = GetSettingsPath(addonId);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public bool Exists(string addonId) => File.Exists(GetSettingsPath(addonId));

    private string GetSettingsPath(string addonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonId);
        if (addonId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-')
            || addonId.StartsWith(".", StringComparison.Ordinal)
            || addonId.EndsWith(".", StringComparison.Ordinal))
            throw new ArgumentException("The addon ID contains unsupported path characters.", nameof(addonId));
        return Path.Combine(_dataDirectory, addonId, "settings.json");
    }
}
