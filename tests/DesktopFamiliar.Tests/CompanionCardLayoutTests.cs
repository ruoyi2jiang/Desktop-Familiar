using System.Xml.Linq;
using DesktopFamiliar.Models;
using DesktopFamiliar.Views.Controls;

namespace DesktopFamiliar.Tests;

public sealed class CompanionCardLayoutTests
{
    [Fact]
    public void CardCountsConfiguredAnimationsForTheActiveAppearance()
    {
        var pet = new CompanionDefinition();
        Assert.Equal("2D · 0 animations", CompanionCard.FormatSummary(pet));
        var sprite = new AnimationScene { PngFrames = [new(), new()] };
        pet.Animations.Add(sprite);
        pet.IdleRandomAnimationIds.Add(sprite.Id);
        var combined = new AnimationScene { ModelParts = ["start", "loop"] };
        pet.Model3D.Animations = [combined, new() { PartOfSceneId = combined.Id, ModelClip = "loop" }, new()];

        Assert.Equal("2D · 1 animation", CompanionCard.FormatSummary(pet));
        pet.Appearance = CompanionAppearance.Model3D;
        Assert.Equal("3D · 2 animations", CompanionCard.FormatSummary(pet));
    }

    [Fact]
    public void ConfigurePreviewFitsTheWholePictureInItsOriginalPortraitFrame()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DesktopFamiliar.csproj")))
            root = root.Parent;
        Assert.NotNull(root);
        var card = XDocument.Load(Path.Combine(root.FullName, "Views", "Controls", "CompanionCard.xaml"));
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var frame = card.Descendants(ui + "Border").Single(b => (string?)b.Attribute(x + "Name") == "PreviewBorder");
        var column = frame.Parent!.Element(ui + "Grid.ColumnDefinitions")!.Elements().First();

        Assert.Equal("118", (string?)column.Attribute("Width"));
        Assert.Equal("152", (string?)frame.Attribute("Height"));
        Assert.Equal("10", (string?)frame.Attribute("CornerRadius"));
        Assert.Equal("Uniform", (string?)frame.Descendants(ui + "Image").Single().Attribute("Stretch"));
    }
}
