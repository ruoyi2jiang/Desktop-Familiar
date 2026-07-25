using System.Text.Json;

namespace DesktopFamiliar.Services.Addons;

/// <summary>
/// Transfers the retired core Auto Clicker fields to the addon's data directory before the core
/// configuration is normalized and rewritten without those fields.
/// </summary>
public sealed class LegacyAutoClickerSettingsMigrator
{
    public const string AutoClickerAddonId = "desktopfamiliar.auto-clicker";
    private readonly string _coreSettingsPath;
    private readonly AddonSettingsStore _addonSettings;

    public LegacyAutoClickerSettingsMigrator(string rootDirectory, AddonSettingsStore addonSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _coreSettingsPath = Path.Combine(Path.GetFullPath(rootDirectory), "desktop-familiar.json");
        _addonSettings = addonSettings ?? throw new ArgumentNullException(nameof(addonSettings));
    }

    public bool MigrateIfNeeded()
    {
        if (_addonSettings.Exists(AutoClickerAddonId) || !File.Exists(_coreSettingsPath))
            return false;

        using var document = JsonDocument.Parse(File.ReadAllText(_coreSettingsPath));
        if (!TryGetProperty(document.RootElement, "settings", out var legacySettings)
            || legacySettings.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var migrated = new LegacyAutoClickerSettings();
        var foundLegacyValue = false;
        if (TryReadInt(legacySettings, "autoClickIntervalMilliseconds", out var interval))
        {
            migrated.IntervalMilliseconds = interval;
            foundLegacyValue = true;
        }
        if (TryReadInt(legacySettings, "autoClickIntervalMaximumMilliseconds", out var maximumInterval))
        {
            migrated.IntervalMaximumMilliseconds = maximumInterval;
            foundLegacyValue = true;
        }
        if (TryReadString(legacySettings, "autoClickHotkey", out var hotkey))
        {
            migrated.Hotkey = hotkey;
            foundLegacyValue = true;
        }
        if (TryReadString(legacySettings, "autoClickBackgroundModeHotkey", out var backgroundModeHotkey))
        {
            migrated.BackgroundModeHotkey = backgroundModeHotkey;
            foundLegacyValue = true;
        }
        if (TryReadBool(legacySettings, "autoClickEnabled", out var enabled))
        {
            migrated.Enabled = enabled;
            foundLegacyValue = true;
        }
        if (TryReadBool(legacySettings, "autoClickBackgroundModeEnabled", out var backgroundModeEnabled))
        {
            migrated.BackgroundModeEnabled = backgroundModeEnabled;
            foundLegacyValue = true;
        }
        if (!foundLegacyValue)
            return false;

        _addonSettings.Write(AutoClickerAddonId, migrated);
        return true;
    }

    private static bool TryReadInt(JsonElement source, string name, out int value)
    {
        value = 0;
        return TryGetProperty(source, name, out var property) && property.TryGetInt32(out value);
    }

    private static bool TryReadString(JsonElement source, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(source, name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadBool(JsonElement source, string name, out bool value)
    {
        value = false;
        if (!TryGetProperty(source, name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetProperty(JsonElement source, string name, out JsonElement property)
    {
        foreach (var candidate in source.EnumerateObject())
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private sealed class LegacyAutoClickerSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public int IntervalMilliseconds { get; set; } = 1000;
        public int IntervalMaximumMilliseconds { get; set; } = 1000;
        public string Hotkey { get; set; } = "Ctrl+Alt+C";
        public string BackgroundModeHotkey { get; set; } = "Ctrl+Alt+B";
        public bool Enabled { get; set; } = true;
        public bool BackgroundModeEnabled { get; set; }
    }
}
