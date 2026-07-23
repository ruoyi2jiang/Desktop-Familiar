using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class SharedDatePickerStyleTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CalendarIconButton_HasNoHoverTriggerAndIsUsedByDatePickerTemplates()
    {
        var appDocument = XDocument.Load(FindRepositoryFile("App.xaml"));
        var iconStyle = appDocument
            .Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "DatePickerIconButton");

        Assert.DoesNotContain(iconStyle.Descendants(Presentation + "Trigger"),
            trigger => (string?)trigger.Attribute("Property") == "IsMouseOver");

        var templateDocuments = new[]
        {
            appDocument,
            XDocument.Load(FindRepositoryFile(Path.Combine("Views", "CompanionEditorWindow.xaml")))
        };
        var calendarButtons = templateDocuments
            .SelectMany(document => document.Descendants(Presentation + "Button"))
            .Where(element => (string?)element.Attribute(Xaml + "Name") == "PART_Button")
            .ToList();

        Assert.NotEmpty(calendarButtons);
        Assert.All(calendarButtons, button =>
            Assert.Equal("{StaticResource DatePickerIconButton}", (string?)button.Attribute("Style")));
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
