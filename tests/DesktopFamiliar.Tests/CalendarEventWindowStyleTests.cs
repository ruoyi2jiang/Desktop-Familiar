using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class CalendarEventWindowStyleTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void StatusBadgeAndEventDetailsTitleShareVerticalAlignmentWithoutTitleSpacing()
    {
        var document = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "CalendarEventWindow.xaml")));
        var title = document
            .Descendants(Presentation + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{loc:Loc Calendar.EventDetails}");
        var badge = document
            .Descendants(Presentation + "Border")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "EventStatusBadge");

        Assert.Equal("0", (string?)title.Attribute("Margin"));
        Assert.Equal("Center", (string?)title.Attribute("VerticalAlignment"));
        Assert.Equal("Center", (string?)badge.Attribute("VerticalAlignment"));
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
