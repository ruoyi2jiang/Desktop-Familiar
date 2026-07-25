namespace DesktopFamiliar.Tests;

public sealed class AutoClickStatusDialogueTests
{
    [Fact]
    public void AddonStatus_UsesTemporaryBubbleLayerWithoutStartingAnAnimation()
    {
        var overlaySource = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml.cs")));
        var appSource = File.ReadAllText(FindRepositoryFile("App.xaml.cs"));

        Assert.Contains("public void ShowAddonStatus(string message)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("_interruptedDialogText = DialogText.Text;", overlaySource, StringComparison.Ordinal);
        Assert.Contains("private string? _queuedAddonStatusMessage;", overlaySource, StringComparison.Ordinal);
        Assert.Contains("_queuedAddonStatusMessage = message;", overlaySource, StringComparison.Ordinal);
        Assert.Contains("if (_queuedAddonStatusMessage is { } queuedMessage)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("if (_showingAddonStatus)", overlaySource, StringComparison.Ordinal);
        Assert.Contains("ShowDialogLine();", overlaySource, StringComparison.Ordinal);
        Assert.Contains("new DispatcherAddonCompanionStatusPresenter(Dispatcher, message => _overlay?.ShowAddonStatus(message))", appSource, StringComparison.Ordinal);
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
