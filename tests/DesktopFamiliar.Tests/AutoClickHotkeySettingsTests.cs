namespace DesktopFamiliar.Tests;

public sealed class AutoClickHotkeySettingsTests
{
    [Fact]
    public void AutoClickSettings_ExposeSeparateStartStopAndInputModeHotkeys()
    {
        var settingsSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Addons", "AutoClicker", "Views", "AutoClickerSettingsSection.cs")));
        var coordinatorSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Addons", "AutoClicker", "Services", "AutoClickerCoordinator.cs")));

        Assert.Contains("_backgroundModeHotkeyButton", settingsSource, StringComparison.Ordinal);
        Assert.Contains("AutoClickBackgroundModeHotkey_Click", settingsSource, StringComparison.Ordinal);
        Assert.Contains("_backgroundModeHotkeyService", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("public void ToggleBackgroundMode()", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("Overlay.AutoClick.BackgroundModeEnabled", coordinatorSource, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
