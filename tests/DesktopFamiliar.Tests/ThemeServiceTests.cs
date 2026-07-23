using DesktopFamiliar.Models;
using DesktopFamiliar.Services;

namespace DesktopFamiliar.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public void CrimsonMoonPaletteUsesCrimsonWhiteGoldAndCharcoalRoles()
    {
        var palette = ThemeService.CreateEditablePalette(AppTheme.Crimson);

        Assert.Equal("#FFFCFAF8", palette.PageBackground);
        Assert.Equal("#FFFFFFFF", palette.PrimaryBackground);
        Assert.Equal("#FFAA1232", palette.PrimaryAccent);
        Assert.Equal("#FFAA1232", palette.SecondaryBackground);
        Assert.Equal("#FF7D0B24", palette.SecondaryBackgroundHover);
        Assert.Equal("#FF21191A", palette.SecondaryBackgroundActive);
        Assert.Equal("#FFD6B66A", palette.SecondaryAccent);
        Assert.Equal("#FF21191A", palette.SecondaryTextOnAccent);
        Assert.True(ContrastRatio(palette.SecondaryTextOnBackground, palette.SecondaryBackground) >= 4.5);
        Assert.True(ContrastRatio(palette.SecondaryTextOnBackground, palette.SecondaryBackgroundHover) >= 4.5);
        Assert.True(ContrastRatio(palette.SecondaryTextOnBackground, palette.SecondaryBackgroundActive) >= 4.5);
        Assert.True(ContrastRatio(palette.SecondaryTextOnAccent, palette.SecondaryAccent) >= 4.5);
    }

    [Fact]
    public void StellaDarkPaletteUsesReadableCelestialColorSets()
    {
        var palette = ThemeService.CreateEditablePalette(AppTheme.StellaDark);

        Assert.Equal("#FF15121F", palette.PageBackground);
        Assert.Equal("#FFD9B9FF", palette.PrimaryAccent);
        Assert.Equal("#FFE3C39B", palette.SecondaryAccent);
        Assert.Equal("#FF1B1525", palette.PageTextOnAccent);
        Assert.Equal("#FF1B1525", palette.PrimaryTextOnAccent);
        Assert.Equal("#FF241B2D", palette.SecondaryTextOnAccent);
        Assert.Equal("#FF1B1525", palette.InputTextOnAccent);
        Assert.Equal("#E61A1525", palette.OverlaySurface);
        AssertReadable(palette.PageTextOnBackground, palette.PageBackground);
        AssertReadable(palette.PageTextOnBackground, palette.PageBackgroundHover);
        AssertReadable(palette.PageTextOnAccent, palette.PageAccent);
        AssertReadable(palette.PrimaryTextOnBackground, palette.PrimaryBackground);
        AssertReadable(palette.PrimaryTextOnAccent, palette.PrimaryAccent);
        AssertReadable(palette.SecondaryTextOnBackground, palette.SecondaryBackground);
        AssertReadable(palette.SecondaryTextOnAccent, palette.SecondaryAccent);
        AssertReadable(palette.InputTextOnBackground, palette.InputBackground);
        AssertReadable(palette.InputTextOnAccent, palette.InputAccent);
    }

    private static void AssertReadable(string foreground, string background) =>
        Assert.True(
            ContrastRatio(foreground, background) >= 4.5,
            $"Expected {foreground} on {background} to meet WCAG AA contrast.");

    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        return (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05)
            / (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
    }

    private static double RelativeLuminance(string argb)
    {
        var red = Convert.ToByte(argb.Substring(3, 2), 16) / 255d;
        var green = Convert.ToByte(argb.Substring(5, 2), 16) / 255d;
        var blue = Convert.ToByte(argb.Substring(7, 2), 16) / 255d;
        return (0.2126 * Linearize(red)) + (0.7152 * Linearize(green)) + (0.0722 * Linearize(blue));
    }

    private static double Linearize(double channel) => channel <= 0.04045
        ? channel / 12.92
        : Math.Pow((channel + 0.055) / 1.055, 2.4);
}
