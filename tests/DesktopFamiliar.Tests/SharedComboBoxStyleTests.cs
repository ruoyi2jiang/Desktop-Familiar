using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class SharedComboBoxStyleTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SharedTemplate_PassesInputTextTokenToSelectedValueAndItems()
    {
        var document = XDocument.Load(FindRepositoryFile("App.xaml"));
        var comboStyle = FindStyle(document, "ThemedComboBox");
        var itemStyle = FindStyle(document, "ThemedComboBoxItem");

        Assert.Contains(comboStyle.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Foreground"
            && (string?)setter.Attribute("Value") == "{DynamicResource TextOnInputBackgroundBrush}");

        var selectionPresenter = comboStyle
            .Descendants(Presentation + "ContentPresenter")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "SelectionPresenter");
        Assert.Equal("{TemplateBinding Foreground}", (string?)selectionPresenter.Attribute("TextElement.Foreground"));
        Assert.Equal(
            "{StaticResource InputTextBlock}",
            (string?)selectionPresenter.Descendants(Presentation + "Style").Single().Attribute("BasedOn"));

        var itemPresenter = itemStyle.Descendants(Presentation + "ContentPresenter").Single();
        Assert.Equal("{TemplateBinding Foreground}", (string?)itemPresenter.Attribute("TextElement.Foreground"));
        Assert.Equal(
            "{StaticResource InputTextBlock}",
            (string?)itemPresenter.Descendants(Presentation + "Style").Single().Attribute("BasedOn"));
    }

    [Fact]
    public void EditorComboBox_DerivesFromSharedTemplateInsteadOfDuplicatingIt()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "CompanionEditorWindow.xaml")));
        var style = FindStyle(document, "EditorComboInput");

        Assert.Equal("{StaticResource ThemedComboBox}", (string?)style.Attribute("BasedOn"));
        Assert.DoesNotContain(style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Margin");
        Assert.DoesNotContain(style.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Template");
    }

    private static XElement FindStyle(XDocument document, string key)
    {
        return document
            .Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == key);
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
