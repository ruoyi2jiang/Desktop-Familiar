namespace DesktopFamiliar.Tests;

public sealed class AutoClickTargetAreaSelectionTests
{
    [Fact]
    public void TargetPicker_DrawsAndReturnsTheDragSelectedArea()
    {
        var markup = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "AutoClickTargetWindow.xaml")));
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "AutoClickTargetWindow.xaml.cs")));

        Assert.Contains("MouseMove=\"Window_MouseMove\" MouseLeftButtonUp=\"Window_MouseLeftButtonUp\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionRectangle\"", markup, StringComparison.Ordinal);
        Assert.Contains("private static DrawingRectangle CreateTargetArea", source, StringComparison.Ordinal);
        Assert.Contains("new AutoClickTargetSelectedEventArgs(targetArea)", source, StringComparison.Ordinal);
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
