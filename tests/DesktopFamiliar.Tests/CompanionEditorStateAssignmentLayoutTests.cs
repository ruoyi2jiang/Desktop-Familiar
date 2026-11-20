using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class CompanionEditorStateAssignmentLayoutTests
{
    [Fact]
    public void RandomIdleChoicesStartCollapsedWithTheirCountInTheHeader()
    {
        var layout = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "CompanionEditorWindow.xaml")));
        var ui = layout.Root!.Name.Namespace;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var section = layout.Descendants(ui + "Expander").Single(e => (string?)e.Attribute(x + "Name") == "RandomIdleExpander");
        Assert.Equal("False", (string?)section.Attribute("IsExpanded"));
        var header = section.Element(ui + "Expander.Header")!;
        Assert.Contains(header.Descendants(), e => (string?)e.Attribute(x + "Name") == "RandomIdleCount");
        var choices = section.Descendants(ui + "WrapPanel").Single();
        Assert.Equal("IdleRandomChecksPanel", (string?)choices.Attribute(x + "Name"));
        Assert.DoesNotContain(choices, header.Descendants());
    }

    [Fact]
    public void CoreStates_AppearBeforeGroupedInteractionAssignments()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "CompanionEditorWindow.xaml")));

        var idle = source.IndexOf("x:Name=\"IdleDefaultCombo\"", StringComparison.Ordinal);
        var reminder = source.IndexOf("x:Name=\"ReminderCombo\"", StringComparison.Ordinal);
        var interactionGroup = source.IndexOf("Editor.StateGroup.Interaction", StringComparison.Ordinal);
        var dragLeft = source.IndexOf("x:Name=\"DragLeftCombo\"", StringComparison.Ordinal);
        var advancedInteractions = source.IndexOf("x:Name=\"AdvancedInteractionsExpander\"", StringComparison.Ordinal);

        Assert.True(idle >= 0);
        Assert.True(reminder > idle);
        Assert.True(interactionGroup > reminder);
        Assert.True(dragLeft > interactionGroup);
        Assert.True(advancedInteractions > dragLeft);
        Assert.Contains(
            "HelpText=\"{loc:Loc Editor.StateGroup.Interaction.Help}\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"{loc:Loc Editor.ClickInteractions}\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Left\" Background=\"Transparent\"", source, StringComparison.Ordinal);
        Assert.Contains("<ToggleButton.Template>", source, StringComparison.Ordinal);
        Assert.Contains("<Border Background=\"Transparent\" BorderBrush=\"Transparent\" BorderThickness=\"0\" Padding=\"{TemplateBinding Padding}\">", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split("Background=\"{DynamicResource PrimaryAccentBrush}\" Opacity=\"0.45\"").Length - 1);
        Assert.Contains("Editor.Tab.Profile", source, StringComparison.Ordinal);
        Assert.Contains("Editor.Tab.Animations", source, StringComparison.Ordinal);
        Assert.Contains("Editor.Tab.States", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Editor.Tab.Advanced", source, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\" />", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeaderPanel\" IsItemsHost=\"True\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" Margin=\"0,4\" Panel.ZIndex=\"1\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Border.OpacityMask>", source, StringComparison.Ordinal);
        Assert.Contains("<Border Margin=\"0,48,0,0\" ClipToBounds=\"True\">", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_SelectedContentHost\" ContentSource=\"SelectedContent\"", source, StringComparison.Ordinal);
        Assert.Contains("<TabControl Grid.Row=\"1\" Margin=\"0,-49,0,0\" Panel.ZIndex=\"1\"", source, StringComparison.Ordinal);
        Assert.Contains("Padding=\"18,14,18,38\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BannerTitle\"", source, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Margin=\"0,0,48,0\"", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split("<StackPanel Margin=\"28,48,28,28\">").Length - 1);
        Assert.Contains("<Grid Width=\"128\" Height=\"88\" ClipToBounds=\"False\">", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TabBottomOutline\" Data=\"M 2,44 L 64,78.21 L 126,44\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TabBorder\" Data=\"M 64,12 L 122,44 L 64,76 L 6,44 Z\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TabRhombus\" Data=\"M 64,12 L 122,44 L 64,76 L 6,44 Z\"", source, StringComparison.Ordinal);
        Assert.Contains("Stroke=\"{DynamicResource SecondaryBackgroundBrush}\" StrokeThickness=\"4\"", source, StringComparison.Ordinal);
        Assert.Contains("Stroke=\"{DynamicResource SecondaryAccentBrush}\" StrokeThickness=\"3\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TargetName=\"TabOutline\" Property=\"Stroke\"", source, StringComparison.Ordinal);
        Assert.Contains("<TextBlock x:Name=\"TabLabel\" Text=\"{TemplateBinding Header}\"", source, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"12\" FontWeight=\"SemiBold\"", source, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"TabLabel\" Property=\"FontWeight\" Value=\"Bold\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"2\" Background=\"{DynamicResource PrimaryBackgroundBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{DynamicResource PrimaryAccentBrush}\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SaveButton\" Content=\"{loc:Loc Editor.Save.Update}\" Style=\"{StaticResource PrimaryButton}\"", source, StringComparison.Ordinal);
        var validationHint = source.IndexOf("x:Name=\"ValidationHint\"", StringComparison.Ordinal);
        var saveButton = source.IndexOf("x:Name=\"SaveButton\"", StringComparison.Ordinal);
        Assert.True(validationHint >= 0);
        Assert.True(saveButton > validationHint);
        Assert.Contains("x:Name=\"ValidationHint\" Foreground=\"{DynamicResource DangerBrush}\" TextWrapping=\"Wrap\" Visibility=\"Collapsed\" Margin=\"0,0,0,8\"", source, StringComparison.Ordinal);
        Assert.Contains("SecondaryBackgroundActiveBrush", source, StringComparison.Ordinal);
        Assert.Contains("SecondaryAccentActiveBrush", source, StringComparison.Ordinal);
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
