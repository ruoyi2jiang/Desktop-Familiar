using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Addons.AutoClicker.Models;

namespace DesktopFamiliar.Addons.AutoClicker.Services;

/// <summary>Normalizes and persists the Auto Clicker's independent settings document.</summary>
public sealed class AutoClickerSettingsStore
{
    private readonly IAddonSettingsStore _store;

    public AutoClickerSettingsStore(IAddonSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public AutoClickerSettings Load()
    {
        var settings = _store.Read<AutoClickerSettings>(AutoClickerAddonDefinition.Id) ?? new AutoClickerSettings();
        Normalize(settings);
        return settings;
    }

    public void Save(AutoClickerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        _store.Write(AutoClickerAddonDefinition.Id, settings.Clone());
    }

    public static void Normalize(AutoClickerSettings settings)
    {
        settings.SchemaVersion = AutoClickerSettings.CurrentSchemaVersion;
        settings.IntervalMilliseconds = Math.Clamp(
            settings.IntervalMilliseconds,
            AutoClickerSettings.MinimumIntervalMilliseconds,
            AutoClickerSettings.MaximumIntervalMilliseconds);
        settings.IntervalMaximumMilliseconds = Math.Clamp(
            settings.IntervalMaximumMilliseconds,
            settings.IntervalMilliseconds,
            AutoClickerSettings.MaximumIntervalMilliseconds);
        if (!AutoClickHotkeyPolicy.TryParse(settings.Hotkey, out var toggle))
            settings.Hotkey = AutoClickerSettings.DefaultHotkey;
        else
            settings.Hotkey = toggle.DisplayText;

        if (!AutoClickHotkeyPolicy.TryParse(settings.BackgroundModeHotkey, out var background)
            || string.Equals(background.DisplayText, settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            settings.BackgroundModeHotkey = string.Equals(
                settings.Hotkey,
                AutoClickerSettings.DefaultBackgroundModeHotkey,
                StringComparison.OrdinalIgnoreCase)
                ? AutoClickerSettings.FallbackBackgroundModeHotkey
                : AutoClickerSettings.DefaultBackgroundModeHotkey;
        }
        else
        {
            settings.BackgroundModeHotkey = background.DisplayText;
        }
    }
}
