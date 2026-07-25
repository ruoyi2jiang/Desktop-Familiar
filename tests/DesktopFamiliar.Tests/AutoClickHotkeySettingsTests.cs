namespace DesktopFamiliar.Tests;

public sealed class AutoClickHotkeySettingsTests
{
    [Fact]
    public void AutoClickSettings_ExposeSeparateStartStopAndInputModeHotkeys()
    {
        var markup = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml")));
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "AutoClick", "AutoClickCoordinator.cs")));

        Assert.Contains("AutoClickBackgroundModeHotkeyButton", markup, StringComparison.Ordinal);
        Assert.Contains("AutoClickBackgroundModeHotkey_Click", markup, StringComparison.Ordinal);
        Assert.Contains("private readonly GlobalHotkeyService _backgroundModeHotkeyService;", source, StringComparison.Ordinal);
        Assert.Contains("public void ToggleBackgroundMode()", source, StringComparison.Ordinal);
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
