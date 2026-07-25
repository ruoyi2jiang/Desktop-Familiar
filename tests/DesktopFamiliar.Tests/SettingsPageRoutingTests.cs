namespace DesktopFamiliar.Tests;

public sealed class SettingsPageRoutingTests
{
    [Fact]
    public void ExtraPage_ContainsTheAutoClickerSettings()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml.cs")));
        var markup = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml")));

        Assert.Contains(
            "ExtraPanel.Visibility = page == SettingsPage.Extra ? Visibility.Visible : Visibility.Collapsed;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("AutoClickEnabledToggle", source, StringComparison.Ordinal);
        Assert.Contains("AutoClickBackgroundModeCheck", source, StringComparison.Ordinal);
        Assert.Contains("AutoClickOptionsPanel.Visibility", source, StringComparison.Ordinal);
        Assert.Contains("<controls:FieldHelpLabel Text=\"\" HelpText=\"{loc:Loc Settings.AutoClick.BackgroundMode.Description}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"{loc:Loc Settings.AutoClick.BackgroundMode.Description}\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.AutoClick.TargetHelp", markup, StringComparison.Ordinal);
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
