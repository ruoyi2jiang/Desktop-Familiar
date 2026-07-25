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
    public const int MinimumAutoClickIntervalMilliseconds = 10;
    public const int MaximumAutoClickIntervalMilliseconds = 60000;
    public const int DefaultAutoClickIntervalMilliseconds = 1000;
    public const string DefaultAutoClickHotkey = "Ctrl+Alt+C";
    public const string DefaultAutoClickBackgroundModeHotkey = "Ctrl+Alt+B";
    public const string FallbackAutoClickBackgroundModeHotkey = "Ctrl+Alt+V";

    public Guid? CurrentCompanionId { get; set; }

    public bool FocusModeEnabled { get; set; }
    public bool RunAtStartup { get; set; }
    public DateTime? UserBirthday { get; set; }
    public double IdleDelaySeconds { get; set; } = DefaultIdleDelaySeconds;
    public AppTheme Theme { get; set; } = AppTheme.Crimson;
    public CustomThemePalette CustomTheme { get; set; } = new();
    public List<SavedTheme> SavedThemes { get; set; } = [];
    public Guid? SelectedThemeId { get; set; }
    public int ViewportWidth { get; set; } = 240;
    public int ViewportHeight { get; set; } = 240;
    public bool TruncateLongDialogLines { get; set; }
    public bool DialogBubbleHidden { get; set; }
    public int AutoClickIntervalMilliseconds { get; set; } = DefaultAutoClickIntervalMilliseconds;
    public int AutoClickIntervalMaximumMilliseconds { get; set; } = DefaultAutoClickIntervalMilliseconds;
    public string AutoClickHotkey { get; set; } = DefaultAutoClickHotkey;
    public string AutoClickBackgroundModeHotkey { get; set; } = DefaultAutoClickBackgroundModeHotkey;
    public bool AutoClickEnabled { get; set; } = true;
    public bool AutoClickBackgroundModeEnabled { get; set; }

    public double? CompanionLeft { get; set; }

    public double? CompanionTop { get; set; }

    public AppSettings Clone() => new()
    {
        CurrentCompanionId = CurrentCompanionId,
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
        TruncateLongDialogLines = TruncateLongDialogLines,
        DialogBubbleHidden = DialogBubbleHidden,
        AutoClickIntervalMilliseconds = AutoClickIntervalMilliseconds,
        AutoClickIntervalMaximumMilliseconds = AutoClickIntervalMaximumMilliseconds,
        AutoClickHotkey = AutoClickHotkey,
        AutoClickBackgroundModeHotkey = AutoClickBackgroundModeHotkey,
        AutoClickEnabled = AutoClickEnabled,
        AutoClickBackgroundModeEnabled = AutoClickBackgroundModeEnabled,
        CompanionLeft = CompanionLeft,
        CompanionTop = CompanionTop
    };
}
