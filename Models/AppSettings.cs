namespace DesktopFamiliar.Models;

public sealed class AppSettings
{
    public const int MinimumViewportSize = 120;
    public const int MaximumViewportSize = 1200;
    public const int MinimumDialogBubbleWidth = 240;
    public const int MaximumDialogBubbleWidth = 480;
    public const int MinimumIdleDelaySeconds = 5;
    public const int MaximumIdleDelaySeconds = 3600;
    public const int DefaultIdleDelaySeconds = 30;

    public Guid? CurrentCompanionId { get; set; }

    public ChatPreferences Chat { get; set; } = new();
    public bool FocusModeEnabled { get; set; }
    public bool RunAtStartup { get; set; } = true;
    public DateTime? UserBirthday { get; set; }
    public double IdleDelaySeconds { get; set; } = DefaultIdleDelaySeconds;
    public AppTheme Theme { get; set; } = AppTheme.Crimson;
    public CustomThemePalette CustomTheme { get; set; } = new();
    public List<SavedTheme> SavedThemes { get; set; } = [];
    public Guid? SelectedThemeId { get; set; }
    public int ViewportWidth { get; set; } = 240;
    public int ViewportHeight { get; set; } = 240;
    public bool DialogBubbleHidden { get; set; }

    public double? CompanionLeft { get; set; }

    public double? CompanionTop { get; set; }

    public AppSettings Clone() => new()
    {
        CurrentCompanionId = CurrentCompanionId,
        Chat = Chat,
        FocusModeEnabled = FocusModeEnabled,
        RunAtStartup = RunAtStartup,
        UserBirthday = UserBirthday,
        IdleDelaySeconds = IdleDelaySeconds,
        Theme = Theme,
        CustomTheme = CustomTheme.Clone(),
        SavedThemes = SavedThemes.Select(theme => theme.Clone()).ToList(),
        SelectedThemeId = SelectedThemeId,
        ViewportWidth = ViewportWidth,
        ViewportHeight = ViewportHeight,
        DialogBubbleHidden = DialogBubbleHidden,
        CompanionLeft = CompanionLeft,
        CompanionTop = CompanionTop
    };
}
