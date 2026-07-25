namespace DesktopFamiliar.Tests;

public sealed class AutoClickBackgroundModeTests
{
    [Fact]
    public void BackgroundMode_UsesTheSelectedWindowWithoutMovingTheSystemCursor()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Services", "AutoClick", "AutoClickService.cs")));

        Assert.Contains("DrawingRectangle targetArea,", source, StringComparison.Ordinal);
        Assert.Contains("var backgroundTarget = useBackgroundClick ? TryCreateBackgroundTarget(targetArea) : null;", source, StringComparison.Ordinal);
        Assert.Contains("if (_backgroundTarget is { } backgroundTarget)", source, StringComparison.Ordinal);
        Assert.Contains("SendBackgroundClick(backgroundTarget, target);", source, StringComparison.Ordinal);
        Assert.Contains("WindowFromPoint", source, StringComparison.Ordinal);
        Assert.Contains("PostMessage", source, StringComparison.Ordinal);
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
