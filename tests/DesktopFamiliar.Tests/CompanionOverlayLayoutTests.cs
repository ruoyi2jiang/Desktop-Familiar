using System.Windows;
using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class CompanionOverlayLayoutTests
{
    [Theory]
    [InlineData("DialogText")]
    [InlineData("ChatReplyText")]
    public void DialogueBubblesWrapAndShowTheFullText(string name)
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml")));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var block = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        Assert.Equal("Wrap", (string?)block.Attribute("TextWrapping"));
        Assert.Equal("None", (string?)block.Attribute("TextTrimming"));
    }

    [Fact]
    public void ModelDrawsOnItsOwnSurfaceBehindDialogueAndTheLogicalCharacterArea()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "CompanionOverlayWindow.xaml")));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        var model = Named("PetModel");
        var overlay = Named("OverlayContent");
        Assert.Equal("Canvas", model.Parent!.Name.LocalName);
        Assert.Contains(overlay, model.ElementsAfterSelf());
        Assert.Contains(Named("DialogArea"), overlay.Elements());
        Assert.Contains(Named("CompanionViewport"), overlay.Elements());
    }

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
    public void ChatPromptKeepsItsActionsSquareAndCentersCloseOnTheInputRow()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "ChatPromptWindow.xaml")));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        static Thickness Spacing(XElement element, string attribute) =>
            (Thickness)new ThicknessConverter().ConvertFromInvariantString((string?)element.Attribute(attribute) ?? "0")!;

        var surface = Named("Surface");
        var input = Named("Input");
        var talk = Named("TalkButton");
        var send = Named("SendButton");
        var close = Named("CloseButton");
        Assert.Same(surface, input.Parent!.Parent);
        Assert.Null(input.Attribute("Height")); // Inherit the shared single-line field height.
        Assert.Equal(new Thickness(0), Spacing(input, "Margin"));
        Assert.Equal("Center", close.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("1", talk.Attribute("Grid.Column")?.Value);
        Assert.Equal("2", send.Attribute("Grid.Column")?.Value);
        Assert.Equal("3", close.Attribute("Grid.Column")?.Value);
        Assert.All(new[] { talk, send, close }, control =>
        {
            Assert.Same(input.Parent, control.Parent);
            Assert.Equal(input.Attribute("Grid.Row")?.Value, control.Attribute("Grid.Row")?.Value);
        });
        var style = document.Descendants().Single(e => (string?)e.Attribute(x + "Key") == "PromptAction");
        Assert.Contains(style.Elements(), setter => (string?)setter.Attribute("Property") == "Width"
            && setter.Attribute("Value")!.Value.Contains("ActualHeight", StringComparison.Ordinal));
        Assert.All(new[] { talk, send }, action =>
        {
            Assert.Equal("{StaticResource PromptAction}", action.Attribute("Style")?.Value);
            Assert.Null(action.Attribute("Width"));
            Assert.Null(action.Attribute("Height"));
            Assert.Null(action.Attribute("Margin"));
        });
        var padding = Spacing(surface, "Padding");
        Assert.Equal(padding.Left, padding.Right);
        Assert.Equal(padding.Top, padding.Bottom);
        Assert.True(padding.Left >= 10 && padding.Top >= 8 && padding.Bottom >= 8);
    }

    [Fact]
    public void VoiceStatusWrapsBelowTheFieldAndThePromptSizesToItsContent()
    {
        var window = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "ChatPromptWindow.xaml"))).Root!;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var status = window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "VoiceStatus");
        var input = window.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "Input");
        Assert.Same(input.Parent, status.Parent);
        Assert.Equal("2", status.Attribute("Grid.Row")?.Value);
        Assert.Equal("4", status.Attribute("Grid.ColumnSpan")?.Value);
        Assert.Equal("Wrap", status.Attribute("TextWrapping")?.Value);
        Assert.Equal("Collapsed", status.Attribute("Visibility")?.Value);
        Assert.Equal("Height", window.Attribute("SizeToContent")?.Value);
        Assert.Null(window.Attribute("Width"));
        Assert.Null(window.Attribute("Height"));
    }

    [Fact]
    public void SentMessageWrapsAboveTheInputAndLongMessagesCanScroll()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "ChatPromptWindow.xaml")));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == name);
        var bubble = Named("SentBubble");
        var scroll = Named("SentScroll");
        var text = Named("SentText");

        Assert.Same(Named("Input").Parent, bubble.Parent);
        Assert.Equal("0", (string?)bubble.Attribute("Grid.Row") ?? "0");
        Assert.Equal("1", (string?)Named("Input").Attribute("Grid.Row"));
        Assert.Equal("Right", (string?)bubble.Attribute("HorizontalAlignment"));
        Assert.Equal("Collapsed", (string?)bubble.Attribute("Visibility"));
        Assert.Contains(bubble, scroll.Ancestors());
        Assert.Same(scroll, text.Parent);
        Assert.NotNull(scroll.Attribute("MaxHeight"));
        Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
        Assert.Equal("Wrap", (string?)text.Attribute("TextWrapping"));
        Assert.Equal("None", (string?)text.Attribute("TextTrimming"));
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
