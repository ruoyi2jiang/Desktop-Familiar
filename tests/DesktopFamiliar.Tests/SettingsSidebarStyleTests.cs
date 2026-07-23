using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class SettingsSidebarStyleTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HoverAndSelectedRowsUseDistinctSecondaryTokens()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml")));
        var style = document
            .Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "SettingsSidebarItem");
        var triggers = style.Descendants(Presentation + "Trigger").ToList();

        var selected = triggers.Single(trigger =>
            (string?)trigger.Attribute("Property") == "IsChecked"
            && (string?)trigger.Attribute("Value") == "True");
        var hover = triggers.Single(trigger =>
            (string?)trigger.Attribute("Property") == "IsMouseOver"
            && (string?)trigger.Attribute("Value") == "True");

        Assert.Contains(selected.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "MenuBar"
            && (string?)setter.Attribute("Property") == "Background"
            && (string?)setter.Attribute("Value") == "{DynamicResource SecondaryBackgroundActiveBrush}");
        Assert.Contains(selected.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "SelectionMark"
            && (string?)setter.Attribute("Value") == "{DynamicResource SecondaryAccentBrush}");
        Assert.Contains(hover.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "MenuBar"
            && (string?)setter.Attribute("Property") == "Background"
            && (string?)setter.Attribute("Value") == "{DynamicResource SecondaryBackgroundHoverBrush}");
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
