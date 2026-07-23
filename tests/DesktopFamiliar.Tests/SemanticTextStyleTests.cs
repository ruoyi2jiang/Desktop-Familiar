using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class SemanticTextStyleTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("SectionTitle", "{StaticResource PrimaryTextBlock}")]
    [InlineData("FieldLabel", "{StaticResource PrimaryTextBlock}")]
    [InlineData("ThemedDataGridText", "{StaticResource InputTextBlock}")]
    public void SharedTextStyle_DerivesFromSemanticSurfaceRole(string styleKey, string expectedBaseStyle)
    {
        var document = XDocument.Load(FindRepositoryFile("App.xaml"));
        var style = document
            .Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == styleKey);

        Assert.Equal(expectedBaseStyle, (string?)style.Attribute("BasedOn"));
    }

    [Fact]
    public void SurfaceContainersOwnOrdinaryText_WithoutGlobalTextBlockOverride()
    {
        var appDocument = XDocument.Load(FindRepositoryFile("App.xaml"));
        var implicitTextStyles = appDocument
            .Root!
            .Element(Presentation + "Application.Resources")!
            .Elements(Presentation + "Style")
            .Where(style =>
                (string?)style.Attribute("TargetType") == "TextBlock"
                && style.Attribute(Xaml + "Key") is null)
            .ToList();
        Assert.Empty(implicitTextStyles);

        AssertSurfaceUsesTextToken(
            Path.Combine("Views", "CalendarEventWindow.xaml"),
            "CalendarRoot",
            "{DynamicResource TextOnPageBackgroundBrush}");
        AssertSurfaceUsesTextToken(
            Path.Combine("Views", "CalendarEventWindow.xaml"),
            "EventEditorCard",
            "{DynamicResource TextOnPrimaryBackgroundBrush}");
        AssertSurfaceUsesTextToken(
            Path.Combine("Views", "SettingsWindow.xaml"),
            "SettingsRoot",
            "{DynamicResource TextOnPageBackgroundBrush}");
        AssertSurfaceUsesTextToken(
            Path.Combine("Views", "SettingsWindow.xaml"),
            "ThemeEditor",
            "{DynamicResource TextOnPrimaryBackgroundBrush}");
        AssertSurfaceUsesTextToken(
            Path.Combine("Views", "CompanionEditorWindow.xaml"),
            "CompanionEditorRoot",
            "{DynamicResource TextOnPageBackgroundBrush}");
    }

    [Theory]
    [InlineData("PrimaryButton", "{DynamicResource TextOnPrimaryAccentBrush}")]
    [InlineData("SecondaryButton", "{DynamicResource TextOnSecondaryAccentBrush}")]
    public void AccentButtonsUseMatchingTextOnAccentToken(string styleKey, string expectedForeground)
    {
        var document = XDocument.Load(FindRepositoryFile("App.xaml"));
        var style = document
            .Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == styleKey);

        Assert.Contains(style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground"
            && (string?)setter.Attribute("Value") == expectedForeground);
    }

    [Fact]
    public void PageMenuHighlightUsesPageTextOnAccentToken()
    {
        var document = XDocument.Load(FindRepositoryFile("App.xaml"));
        var style = document
            .Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "Windows11MenuItem");
        var hover = style
            .Descendants(Presentation + "Trigger")
            .Single(trigger =>
                (string?)trigger.Attribute("Property") == "IsHighlighted"
                && (string?)trigger.Attribute("Value") == "True");

        Assert.Contains(hover.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground"
            && (string?)setter.Attribute("Value") == "{DynamicResource TextOnPageAccentBrush}");
    }

    [Fact]
    public void KeyedTextBlockStyles_DeclareForegroundOrDeriveFromTokenizedStyle()
    {
        var repositoryRoot = FindRepositoryDirectory();
        var xamlFiles = new[] { Path.Combine(repositoryRoot, "App.xaml") }
            .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "Views"), "*.xaml", SearchOption.AllDirectories));

        var untokenizedStyles = xamlFiles
            .SelectMany(file => XDocument.Load(file)
                .Descendants(Presentation + "Style")
                .Where(style => (string?)style.Attribute("TargetType") == "TextBlock")
                .Where(style => style.Attribute(Xaml + "Key") is not null)
                .Where(style => style.Attribute("BasedOn") is null)
                .Where(style => !style.Elements(Presentation + "Setter").Any(setter =>
                    (string?)setter.Attribute("Property") == "Foreground"))
                .Select(style => $"{Path.GetRelativePath(repositoryRoot, file)}:{style.Attribute(Xaml + "Key")?.Value}"))
            .ToList();

        Assert.Empty(untokenizedStyles);
    }

    [Fact]
    public void ColorWheel_PageTextOverridesPrimarySurfaceStylesWithPageToken()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "ColorWheelWindow.xaml")));
        var styledText = document
            .Descendants(Presentation + "TextBlock")
            .Where(element =>
                (string?)element.Attribute("Style") is "{StaticResource SectionTitle}" or "{StaticResource FieldLabel}")
            .ToList();

        Assert.NotEmpty(styledText);
        Assert.All(styledText, element =>
            Assert.Equal(
                "{DynamicResource TextOnPageBackgroundBrush}",
                (string?)element.Attribute("Foreground")));
    }

    private static void AssertSurfaceUsesTextToken(string relativePath, string elementName, string expectedForeground)
    {
        var document = XDocument.Load(FindRepositoryFile(relativePath));
        var surface = document
            .Descendants()
            .Single(element => (string?)element.Attribute(Xaml + "Name") == elementName);

        Assert.Equal(expectedForeground, (string?)surface.Attribute("TextElement.Foreground"));
    }

    private static string FindRepositoryFile(string relativePath)
    {
        return Path.Combine(FindRepositoryDirectory(), relativePath);
    }

    private static string FindRepositoryDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "App.xaml")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing App.xaml.");
    }
}
