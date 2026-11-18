using System.Windows;
using System.Xml.Linq;

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

    [Fact]
    public void ChatInputAndSendShareOnePaddedRow()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "ChatPromptWindow.xaml")));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        static Thickness Spacing(XElement element, string attribute) =>
            (Thickness)new ThicknessConverter().ConvertFromInvariantString((string?)element.Attribute(attribute) ?? "0")!;

        var surface = Named("Surface");
        var input = Named("Input");
        var send = Named("SendButton");
        Assert.Same(input.Parent, send.Parent);
        Assert.Same(surface, input.Parent!.Parent);
        Assert.Equal(input.Attribute("Grid.Row")?.Value, send.Attribute("Grid.Row")?.Value);
        Assert.Equal("1", send.Attribute("Grid.Column")?.Value);
        Assert.All(new[] { input, send }, control =>
        {
            var margin = Spacing(control, "Margin");
            Assert.Equal(0, margin.Top);
            Assert.Equal(0, margin.Bottom);
            Assert.Equal("Auto", (string?)control.Attribute("Height") ?? "Auto");
        });
        var padding = Spacing(surface, "Padding");
        Assert.True(padding.Left >= 10 && padding.Top >= 8 && padding.Bottom >= 8);
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
