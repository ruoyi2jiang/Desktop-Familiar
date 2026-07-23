using System.Windows;
using System.Windows.Media;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services;

public static class ThemeService
{
    public sealed record ThemePreview(string Background, string Accent, string Hover);

    /// <summary>
    /// A self-contained palette for one component family.  A surface and an
    /// accent each have their own readable text colour and hover treatment.
    /// </summary>
    private sealed record ColorSet(
        string Background,
        string TextOnBackground,
        string BackgroundHover,
        string BackgroundActive,
        string Accent,
        string TextOnAccent,
        string AccentHover,
        string AccentActive);

    private sealed record ThemePalette(ColorSet Page, ColorSet Primary, ColorSet Secondary, ColorSet Input)
    {
        public string Danger { get; init; } = "#FFA33A56";
        public string DangerHover { get; init; } = "#FF8E2948";
        public string DangerActive { get; init; } = "#FF8E2948";
        public string TextOnDanger { get; init; } = "#FFFFFFFF";
        public string OverlaySurface { get; init; } = "#C8000000";
        public string OverlaySurfaceHover { get; init; } = "#DC000000";
        public string OverlayBorder { get; init; } = "#66FFFFFF";
        public string TextOnOverlay { get; init; } = "#FFFFFFFF";
    }

    private static readonly IReadOnlyDictionary<AppTheme, ThemePalette> Palettes = new Dictionary<AppTheme, ThemePalette>
    {
        [AppTheme.Crimson] = CreateBuiltInPalette(
            Set("#FFFCFAF8", "#FF21191A", "#FFF6E8EA", "#FFEBCFD5", "#FFA30F2D", "#FFFFFFFF", "#FF7D0A22", "#FF590717"),
            Set("#FFFFFFFF", "#FF21191A", "#FFF8ECEE", "#FFEFD9DE", "#FFAA1232", "#FFFFFFFF", "#FF830B24", "#FF5E0718"),
            Set("#FFAA1232", "#FFFFFFFF", "#FF7D0B24", "#FF21191A", "#FFD6B66A", "#FF21191A", "#FFE6CB86", "#FFB99242"),
            Set("#FFFFFFFF", "#FF21191A", "#FFF8ECEE", "#FFF0D8DD", "#FFAA1232", "#FFFFFFFF", "#FF830B24", "#FF5E0718"),
            "#FF791026", "#FF4B0916"),
        [AppTheme.SeaBlue] = CreateBuiltInPalette(
            Set("#FFF5FBFD", "#FF123847", "#FFDCEFF5", "#FFC6E5EF", "#FFB7DFEB", "#FF123847", "#FF88C7DA", "#FF6CB7CD"),
            Set("#FFFFFFFF", "#FF123847", "#FFDAEEF4", "#FFC3E3ED", "#FF177B99", "#FFFFFFFF", "#FF12637E", "#FF0B4C63"),
            Set("#FFFCFEFF", "#FF123847", "#FFE0F1F6", "#FFC6E5EF", "#FF177B99", "#FFFFFFFF", "#FF12637E", "#FF0B4C63"),
            "#FF963450", "#FF76263F"),
        [AppTheme.StellaDark] = CreateBuiltInPalette(
            Set("#FF15121F", "#FFF8F1FF", "#FF242034", "#FF332B48", "#FFC7A4F4", "#FF1B1525", "#FFD7BCF8", "#FFA981D8"),
            Set("#FF211C2E", "#FFF8F1FF", "#FF302840", "#FF403553", "#FFD9B9FF", "#FF1B1525", "#FFE6D1FF", "#FFBA8FE6"),
            Set("#FF2A2338", "#FFF8F1FF", "#FF3A304C", "#FF4A3C60", "#FFE3C39B", "#FF241B2D", "#FFF0D6B4", "#FFC79F70"),
            Set("#FF191520", "#FFF8F1FF", "#FF272131", "#FF342A42", "#FFC39AEF", "#FF1B1525", "#FFD7B8F8", "#FFA97AD8"),
            "#FFC85070", "#FFA93A5A") with
        {
            OverlaySurface = "#E61A1525",
            OverlaySurfaceHover = "#F0272038",
            OverlayBorder = "#99D9B9FF",
            TextOnOverlay = "#FFF8F1FF"
        }
    };

    public static ThemePreview GetPreview(AppTheme theme, CustomThemePalette? customTheme = null)
    {
        var palette = GetPalette(theme, customTheme);
        return new ThemePreview(palette.Primary.Background, palette.Primary.Accent, palette.Primary.BackgroundHover);
    }

    public static CustomThemePalette CreateEditablePalette(AppTheme theme) => ToEditablePalette(GetPalette(theme, null));

    public static void Apply(AppTheme theme, CustomThemePalette? customTheme = null) => Apply(GetPalette(theme, customTheme));

    public static void Apply(CustomThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Apply(CreateCustomPalette(palette, Palettes[AppTheme.Crimson]));
    }

    private static void Apply(ThemePalette palette)
    {
        var resources = Application.Current.Resources;

        SetColorSet(resources,
            ThemeTokens.PageBackground, ThemeTokens.PageTextOnBackground, ThemeTokens.PageBackgroundHover, ThemeTokens.PageBackgroundActive,
            ThemeTokens.PageAccent, ThemeTokens.PageTextOnAccent, ThemeTokens.PageAccentHover, ThemeTokens.PageAccentActive, palette.Page);
        SetColorSet(resources,
            ThemeTokens.PrimaryBackground, ThemeTokens.PrimaryTextOnBackground, ThemeTokens.PrimaryBackgroundHover, ThemeTokens.PrimaryBackgroundActive,
            ThemeTokens.PrimaryAccent, ThemeTokens.PrimaryTextOnAccent, ThemeTokens.PrimaryAccentHover, ThemeTokens.PrimaryAccentActive, palette.Primary);
        SetColorSet(resources,
            ThemeTokens.SecondaryBackground, ThemeTokens.SecondaryTextOnBackground, ThemeTokens.SecondaryBackgroundHover, ThemeTokens.SecondaryBackgroundActive,
            ThemeTokens.SecondaryAccent, ThemeTokens.SecondaryTextOnAccent, ThemeTokens.SecondaryAccentHover, ThemeTokens.SecondaryAccentActive, palette.Secondary);
        SetColorSet(resources,
            ThemeTokens.InputBackground, ThemeTokens.InputTextOnBackground, ThemeTokens.InputBackgroundHover, ThemeTokens.InputBackgroundActive,
            ThemeTokens.InputAccent, ThemeTokens.InputTextOnAccent, ThemeTokens.InputAccentHover, ThemeTokens.InputAccentActive, palette.Input);

        SetBrush(resources, ThemeTokens.Danger, palette.Danger);
        SetBrush(resources, ThemeTokens.DangerHover, palette.DangerHover);
        SetBrush(resources, ThemeTokens.DangerActive, palette.DangerActive);
        SetBrush(resources, ThemeTokens.TextOnDanger, palette.TextOnDanger);
        SetBrush(resources, ThemeTokens.OverlaySurface, palette.OverlaySurface);
        SetBrush(resources, ThemeTokens.OverlaySurfaceHover, palette.OverlaySurfaceHover);
        SetBrush(resources, ThemeTokens.OverlayBorder, palette.OverlayBorder);
        SetBrush(resources, ThemeTokens.TextOnOverlay, palette.TextOnOverlay);

    }

    private static CustomThemePalette ToEditablePalette(ThemePalette palette) => new()
    {
        PageBackground = palette.Page.Background,
        PageTextOnBackground = palette.Page.TextOnBackground,
        PageBackgroundHover = palette.Page.BackgroundHover,
        PageBackgroundActive = palette.Page.BackgroundActive,
        PageAccent = palette.Page.Accent,
        PageTextOnAccent = palette.Page.TextOnAccent,
        PageAccentHover = palette.Page.AccentHover,
        PageAccentActive = palette.Page.AccentActive,
        PrimaryBackground = palette.Primary.Background,
        PrimaryTextOnBackground = palette.Primary.TextOnBackground,
        PrimaryBackgroundHover = palette.Primary.BackgroundHover,
        PrimaryBackgroundActive = palette.Primary.BackgroundActive,
        PrimaryAccent = palette.Primary.Accent,
        PrimaryTextOnAccent = palette.Primary.TextOnAccent,
        PrimaryAccentHover = palette.Primary.AccentHover,
        PrimaryAccentActive = palette.Primary.AccentActive,
        SecondaryBackground = palette.Secondary.Background,
        SecondaryTextOnBackground = palette.Secondary.TextOnBackground,
        SecondaryBackgroundHover = palette.Secondary.BackgroundHover,
        SecondaryBackgroundActive = palette.Secondary.BackgroundActive,
        SecondaryAccent = palette.Secondary.Accent,
        SecondaryTextOnAccent = palette.Secondary.TextOnAccent,
        SecondaryAccentHover = palette.Secondary.AccentHover,
        SecondaryAccentActive = palette.Secondary.AccentActive,
        InputBackground = palette.Input.Background,
        InputTextOnBackground = palette.Input.TextOnBackground,
        InputBackgroundHover = palette.Input.BackgroundHover,
        InputBackgroundActive = palette.Input.BackgroundActive,
        InputAccent = palette.Input.Accent,
        InputTextOnAccent = palette.Input.TextOnAccent,
        InputAccentHover = palette.Input.AccentHover,
        InputAccentActive = palette.Input.AccentActive,
        Danger = palette.Danger,
        DangerHover = palette.DangerHover,
        DangerActive = palette.DangerActive,
        TextOnDanger = palette.TextOnDanger,
        OverlaySurface = palette.OverlaySurface,
        OverlaySurfaceHover = palette.OverlaySurfaceHover,
        OverlayBorder = palette.OverlayBorder,
        TextOnOverlay = palette.TextOnOverlay
    };

    private static ThemePalette GetPalette(AppTheme theme, CustomThemePalette? customTheme) =>
        theme == AppTheme.Custom
            ? CreateCustomPalette(customTheme ?? new CustomThemePalette(), Palettes[AppTheme.Crimson])
            : Palettes.TryGetValue(theme, out var palette) ? palette : Palettes[AppTheme.Crimson];

    private static ColorSet Set(
        string background, string textOnBackground, string backgroundHover,
        string accent, string textOnAccent, string accentHover) =>
        Set(background, textOnBackground, backgroundHover, backgroundHover, accent, textOnAccent, accentHover, accentHover);

    private static ColorSet Set(
        string background, string textOnBackground, string backgroundHover, string backgroundActive,
        string accent, string textOnAccent, string accentHover, string accentActive) =>
        new(background, textOnBackground, backgroundHover, backgroundActive, accent, textOnAccent, accentHover, accentActive);

    private static ThemePalette CreateBuiltInPalette(ColorSet page, ColorSet primary, ColorSet input, string danger, string dangerHover) =>
        new(page, primary, Reverse(primary), input)
        {
            Danger = danger,
            DangerHover = dangerHover,
            DangerActive = dangerHover
        };

    private static ThemePalette CreateBuiltInPalette(
        ColorSet page,
        ColorSet primary,
        ColorSet secondary,
        ColorSet input,
        string danger,
        string dangerHover) =>
        new(page, primary, secondary, input)
        {
            Danger = danger,
            DangerHover = dangerHover,
            DangerActive = dangerHover
        };

    private static ThemePalette CreateCustomPalette(CustomThemePalette custom, ThemePalette fallback) =>
        new(
            CreateCustomSet(
                custom.PageBackground, custom.PageTextOnBackground, custom.PageBackgroundHover, custom.PageBackgroundActive,
                custom.PageAccent, custom.PageTextOnAccent, custom.PageAccentHover, custom.PageAccentActive, fallback.Page),
            CreateCustomSet(
                custom.PrimaryBackground, custom.PrimaryTextOnBackground, custom.PrimaryBackgroundHover, custom.PrimaryBackgroundActive,
                custom.PrimaryAccent, custom.PrimaryTextOnAccent, custom.PrimaryAccentHover, custom.PrimaryAccentActive, fallback.Primary),
            CreateCustomSet(
                custom.SecondaryBackground, custom.SecondaryTextOnBackground, custom.SecondaryBackgroundHover, custom.SecondaryBackgroundActive,
                custom.SecondaryAccent, custom.SecondaryTextOnAccent, custom.SecondaryAccentHover, custom.SecondaryAccentActive, fallback.Secondary),
            CreateCustomSet(
                custom.InputBackground, custom.InputTextOnBackground, custom.InputBackgroundHover, custom.InputBackgroundActive,
                custom.InputAccent, custom.InputTextOnAccent, custom.InputAccentHover, custom.InputAccentActive, fallback.Input))
        {
            Danger = ColorOrFallback(custom.Danger, fallback.Danger),
            DangerHover = ColorOrFallback(custom.DangerHover, fallback.DangerHover),
            DangerActive = ColorOrFallback(custom.DangerActive, fallback.DangerActive),
            TextOnDanger = ColorOrFallback(custom.TextOnDanger, fallback.TextOnDanger),
            OverlaySurface = ColorOrFallback(custom.OverlaySurface, fallback.OverlaySurface),
            OverlaySurfaceHover = ColorOrFallback(custom.OverlaySurfaceHover, fallback.OverlaySurfaceHover),
            OverlayBorder = ColorOrFallback(custom.OverlayBorder, fallback.OverlayBorder),
            TextOnOverlay = ColorOrFallback(custom.TextOnOverlay, fallback.TextOnOverlay)
        };

    private static ColorSet Reverse(ColorSet primary) =>
        Set(
            primary.Accent,
            primary.TextOnAccent,
            primary.AccentHover,
            primary.AccentActive,
            primary.Background,
            primary.TextOnBackground,
            primary.BackgroundHover,
            primary.BackgroundActive);

    private static ColorSet CreateCustomSet(
        string background, string textOnBackground, string backgroundHover, string backgroundActive,
        string accent, string textOnAccent, string accentHover, string accentActive, ColorSet fallback) =>
        Set(
            ColorOrFallback(background, fallback.Background),
            ColorOrFallback(textOnBackground, fallback.TextOnBackground),
            ColorOrFallback(backgroundHover, fallback.BackgroundHover),
            ColorOrFallback(backgroundActive, fallback.BackgroundActive),
            ColorOrFallback(accent, fallback.Accent),
            ColorOrFallback(textOnAccent, fallback.TextOnAccent),
            ColorOrFallback(accentHover, fallback.AccentHover),
            ColorOrFallback(accentActive, fallback.AccentActive));

    private static void SetColorSet(
        ResourceDictionary resources,
        string backgroundKey, string textOnBackgroundKey, string backgroundHoverKey, string backgroundActiveKey,
        string accentKey, string textOnAccentKey, string accentHoverKey, string accentActiveKey, ColorSet set)
    {
        SetBrush(resources, backgroundKey, set.Background);
        SetBrush(resources, textOnBackgroundKey, set.TextOnBackground);
        SetBrush(resources, backgroundHoverKey, set.BackgroundHover);
        SetBrush(resources, backgroundActiveKey, set.BackgroundActive);
        SetBrush(resources, accentKey, set.Accent);
        SetBrush(resources, textOnAccentKey, set.TextOnAccent);
        SetBrush(resources, accentHoverKey, set.AccentHover);
        SetBrush(resources, accentActiveKey, set.AccentActive);
    }

    private static string ColorOrFallback(string? value, string fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : ((Color)ColorConverter.ConvertFromString(value)).ToString();
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, string color)
    {
        // Brushes from XAML can be frozen by WPF before the first window opens.
        // Replacing the resource is safe in both states and refreshes DynamicResource uses.
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
}
