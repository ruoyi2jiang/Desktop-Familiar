namespace DesktopFamiliar.Tests;

public sealed class AutoClickStatusDialogueTests
{
    [Fact]
    public void AutoClickStatus_UsesTemporaryBubbleLayerWithoutStartingAnAnimation()
    {
        var overlaySource = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml.cs")));
        var appSource = File.ReadAllText(FindRepositoryFile("App.xaml.cs"));

        Assert.Contains("public void ShowAutoClickStatus(bool isClicking)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("_interruptedDialogText = DialogText.Text;", overlaySource, StringComparison.Ordinal);
        Assert.Contains("Overlay.AutoClick.Started", overlaySource, StringComparison.Ordinal);
        Assert.Contains("Overlay.AutoClick.Stopped", overlaySource, StringComparison.Ordinal);
        Assert.Contains("if (_showingAutoClickStatus)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("ShowDialogLine();", overlaySource, StringComparison.Ordinal);
        Assert.Contains("_overlay?.ShowAutoClickStatus(args.IsClicking)", appSource, StringComparison.Ordinal);
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
