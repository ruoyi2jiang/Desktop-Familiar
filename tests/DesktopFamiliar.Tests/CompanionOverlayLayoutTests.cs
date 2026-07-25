namespace DesktopFamiliar.Tests;

public sealed class CompanionOverlayLayoutTests
{
    [Fact]
    public void DialogControls_AreCenteredInTheDialogWidthRatherThanTheCharacterViewport()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml")));
        var controlRootIndex = source.IndexOf("x:Name=\"DialogControlRoot\"", StringComparison.Ordinal);
        var hideButtonIndex = source.IndexOf("x:Name=\"HideDialogButton\"", StringComparison.Ordinal);
        var showButtonIndex = source.IndexOf("x:Name=\"ShowDialogButton\"", StringComparison.Ordinal);

        Assert.True(controlRootIndex >= 0);
        Assert.Contains("x:Name=\"DialogControlRoot\" Grid.Row=\"1\" HorizontalAlignment=\"Center\"", source, StringComparison.Ordinal);
        Assert.True(hideButtonIndex > controlRootIndex);
        Assert.True(showButtonIndex > controlRootIndex);
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
