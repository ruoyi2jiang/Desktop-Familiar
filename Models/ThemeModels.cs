namespace DesktopFamiliar.Models;

public sealed class CustomThemePalette
{
    public string PageBackground { get; set; } = "#FFFFF6F7";
    public string PageTextOnBackground { get; set; } = "#FF422229";
    public string PageBackgroundHover { get; set; } = "#FFFFFAFA";
    public string PageBackgroundActive { get; set; } = "#FFFFFAFA";
    public string PageAccent { get; set; } = "#FFFFD6D9";
    public string PageTextOnAccent { get; set; } = "#FF422229";
    public string PageAccentHover { get; set; } = "#FFF5B9C0";
    public string PageAccentActive { get; set; } = "#FFF5B9C0";

    public string PrimaryBackground { get; set; } = "#FFFFFFFF";
    public string PrimaryTextOnBackground { get; set; } = "#FF422229";
    public string PrimaryBackgroundHover { get; set; } = "#FFFCE1E4";
    public string PrimaryBackgroundActive { get; set; } = "#FFFCE1E4";
    public string PrimaryAccent { get; set; } = "#FFC95764";
    public string PrimaryTextOnAccent { get; set; } = "#FFFFFFFF";
    public string PrimaryAccentHover { get; set; } = "#FF9E2F3E";
    public string PrimaryAccentActive { get; set; } = "#FF9E2F3E";

    public string SecondaryBackground { get; set; } = "#FFC95764";
    public string SecondaryTextOnBackground { get; set; } = "#FFFFFFFF";
    public string SecondaryBackgroundHover { get; set; } = "#FF9E2F3E";
    public string SecondaryBackgroundActive { get; set; } = "#FF9E2F3E";
    public string SecondaryAccent { get; set; } = "#FFFFFFFF";
    public string SecondaryTextOnAccent { get; set; } = "#FF422229";
    public string SecondaryAccentHover { get; set; } = "#FFFCE1E4";
    public string SecondaryAccentActive { get; set; } = "#FFFCE1E4";

    public string InputBackground { get; set; } = "#FFFFFAFA";
    public string InputTextOnBackground { get; set; } = "#FF422229";
    public string InputBackgroundHover { get; set; } = "#FFFFD6D9";
    public string InputBackgroundActive { get; set; } = "#FFFFD6D9";
    public string InputAccent { get; set; } = "#FFC95764";
    public string InputTextOnAccent { get; set; } = "#FFFFFFFF";
    public string InputAccentHover { get; set; } = "#FF9E2F3E";
    public string InputAccentActive { get; set; } = "#FF9E2F3E";

    public string Danger { get; set; } = "#FFA33A56";
    public string DangerHover { get; set; } = "#FF8E2948";
    public string DangerActive { get; set; } = "#FF8E2948";
    public string TextOnDanger { get; set; } = "#FFFFFFFF";
    public string OverlaySurface { get; set; } = "#C8000000";
    public string OverlaySurfaceHover { get; set; } = "#DC000000";
    public string OverlayBorder { get; set; } = "#66FFFFFF";
    public string TextOnOverlay { get; set; } = "#FFFFFFFF";

    public CustomThemePalette Clone() => (CustomThemePalette)MemberwiseClone();
}

public sealed class SavedTheme
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Theme";
    public bool IsBuiltIn { get; set; }
    public CustomThemePalette Palette { get; set; } = new();

    public SavedTheme Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsBuiltIn = IsBuiltIn,
        Palette = Palette.Clone()
    };
}
