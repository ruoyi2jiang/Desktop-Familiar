namespace DesktopFamiliar.Tests;

public sealed class AutoClickIntervalRangeInputTests
{
    [Fact]
    public void RaisingMinimumInterval_RaisesLowerMaximumImmediately()
    {
        var markup = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml")));
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml.cs")));

        Assert.Contains("TextChanged=\"AutoClickIntervalMinimum_TextChanged\"", markup, StringComparison.Ordinal);
        Assert.Contains("maximumIntervalMilliseconds >= intervalMilliseconds", source, StringComparison.Ordinal);
        Assert.Contains("AutoClickIntervalMaximumBox.Text = FormatAutoClickInterval(intervalMilliseconds, _autoClickIntervalUnit);", source, StringComparison.Ordinal);
        Assert.Contains("_updatingAutoClickIntervalInputs", source, StringComparison.Ordinal);
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
