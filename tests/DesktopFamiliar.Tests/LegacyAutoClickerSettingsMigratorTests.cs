using DesktopFamiliar.Addons.AutoClicker.Models;
using DesktopFamiliar.Services.Addons;

namespace DesktopFamiliar.Tests;

public sealed class LegacyAutoClickerSettingsMigratorTests
{
    [Fact]
    public void AddonSettingsStore_RejectsAnAddonIdThatEscapesItsScopedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopFamiliarTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AddonSettingsStore(root);

            Assert.Throws<ArgumentException>(() =>
                store.Write("..", new AutoClickerSettings()));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyCorePreferences_AreTransferredToAddonOwnedSettingsOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopFamiliarTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "desktop-familiar.json"), """
                {
                  "schemaVersion": 7,
                  "settings": {
                    "autoClickIntervalMilliseconds": 250,
                    "autoClickIntervalMaximumMilliseconds": 480,
                    "autoClickHotkey": "Ctrl+Alt+F8",
                    "autoClickBackgroundModeHotkey": "Ctrl+Alt+F9",
                    "autoClickEnabled": false,
                    "autoClickBackgroundModeEnabled": true
                  }
                }
                """);
            var store = new AddonSettingsStore(root);
            var migrator = new LegacyAutoClickerSettingsMigrator(root, store);

            Assert.True(migrator.MigrateIfNeeded());
            Assert.False(migrator.MigrateIfNeeded());

            var settings = store.Read<AutoClickerSettings>(LegacyAutoClickerSettingsMigrator.AutoClickerAddonId);
            Assert.NotNull(settings);
            Assert.Equal(250, settings.IntervalMilliseconds);
            Assert.Equal(480, settings.IntervalMaximumMilliseconds);
            Assert.Equal("Ctrl+Alt+F8", settings.Hotkey);
            Assert.Equal("Ctrl+Alt+F9", settings.BackgroundModeHotkey);
            Assert.False(settings.Enabled);
            Assert.True(settings.BackgroundModeEnabled);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
