namespace DesktopFamiliar.Tests;

public sealed class CompanionOverlayPointerInteractionTests
{
    [Fact]
    public void MovingModelHasASmallLayeredWindowHitTargetInTheViewportCenter()
    {
        var xaml = System.Xml.Linq.XDocument.Load(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml")));
        var name = System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
        var target = xaml.Descendants().Single(element => (string?)element.Attribute(name) == "ModelMenuTarget");
        Assert.Equal("CompanionViewport", (string?)target.Parent!.Attribute(name));
        Assert.Equal("Center", (string?)target.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)target.Attribute("VerticalAlignment"));
        Assert.Equal(36, (double)target.Attribute("Width")!);
        Assert.Equal(36, (double)target.Attribute("Height")!);
        Assert.InRange((double)target.Attribute("Opacity")!, 1d / 255, 0.01);
        Assert.StartsWith("{DynamicResource ", (string?)target.Attribute("Background"));
        Assert.Equal("{Binding Visibility, ElementName=PetModel}", (string?)target.Attribute("Visibility"));
    }

    [Fact]
    public void DragRelease_RestoresPlaybackOnlyAfterAnAssignedDragSceneStarted()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml.cs")));

        Assert.Contains("private bool _dragInteractionPlaybackStarted;", source, StringComparison.Ordinal);
        Assert.Contains("_dragInteractionPlaybackStarted = true;", source, StringComparison.Ordinal);
        Assert.Equal(2, source.Split("var restorePlayback = _dragInteractionPlaybackStarted;", StringSplitOptions.None).Length - 1);
        Assert.Contains("if (!restorePlayback || _playbackCompanion is not { } companion)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DraggingViewport_UsesHandCursorAndResetsItAfterRelease()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml.cs")));

        Assert.Contains("Cursor = Cursors.Hand;", source, StringComparison.Ordinal);
        Assert.Contains("private void EndPointerInteraction()", source, StringComparison.Ordinal);
        Assert.Contains("Cursor = null;", source, StringComparison.Ordinal);
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
