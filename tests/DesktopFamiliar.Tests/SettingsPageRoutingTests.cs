namespace DesktopFamiliar.Tests;

public sealed class SettingsPageRoutingTests
{
    [Fact]
    public void AutoClickPage_IsIncludedInSettingsPageVisibilityRouting()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml.cs")));

        Assert.Contains(
            "AutoClickPanel.Visibility = page == SettingsPage.AutoClick ? Visibility.Visible : Visibility.Collapsed;",
            source,
            StringComparison.Ordinal);
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
