namespace DesktopFamiliar.Tests;

public sealed class AutoClickIntervalRangeInputTests
{
    [Fact]
    public void RaisingMinimumInterval_RaisesLowerMaximumImmediately()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Addons", "AutoClicker", "Views", "AutoClickerSettingsSection.cs")));

        Assert.Contains("maximum >= minimum", source, StringComparison.Ordinal);
        Assert.Contains("_intervalMaximumBox.Text = FormatInterval(minimum, _intervalUnit);", source, StringComparison.Ordinal);
        Assert.Contains("_updatingIntervalInputs", source, StringComparison.Ordinal);
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
