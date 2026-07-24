using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.AutoClick;
using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Repairs invalid current-format values without performing I/O.</summary>
public sealed class CompanionDataNormalizer
{
    public void Normalize(DesktopCompanionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Migrate(data);
        data.SchemaVersion = DesktopCompanionData.CurrentSchemaVersion;
        data.Companions ??= [];
        data.CalendarEvents ??= [];
        data.Settings ??= new AppSettings();

        NormalizeSettings(data.Settings);
        foreach (var calendarEvent in data.CalendarEvents)
            NormalizeCalendarEvent(calendarEvent);
        foreach (var companion in data.Companions)
            NormalizeCompanion(companion);
    }

    private static void NormalizeSettings(AppSettings settings)
    {
        settings.IdleDelaySeconds = Math.Clamp(
            settings.IdleDelaySeconds,
            AppSettings.MinimumIdleDelaySeconds,
            AppSettings.MaximumIdleDelaySeconds);
        settings.UserBirthday = settings.UserBirthday?.Date;
        if (!Enum.IsDefined(settings.Theme))
            settings.Theme = AppTheme.Crimson;

        settings.CustomTheme ??= new CustomThemePalette();
        EnsureSavedThemes(settings);
        settings.ViewportWidth = Math.Clamp(
            settings.ViewportWidth,
            AppSettings.MinimumViewportSize,
            AppSettings.MaximumViewportSize);
        settings.ViewportHeight = Math.Clamp(
            settings.ViewportHeight,
            AppSettings.MinimumViewportSize,
            AppSettings.MaximumViewportSize);
        settings.AutoClickIntervalMilliseconds = Math.Clamp(
            settings.AutoClickIntervalMilliseconds,
            AppSettings.MinimumAutoClickIntervalMilliseconds,
            AppSettings.MaximumAutoClickIntervalMilliseconds);
        if (!AutoClickHotkeyPolicy.TryParse(settings.AutoClickHotkey, out _))
            settings.AutoClickHotkey = AppSettings.DefaultAutoClickHotkey;
    }

    private static void Migrate(DesktopCompanionData data)
    {
        if (data.SchemaVersion >= 2)
            return;

        // Schema 2 adds persisted auto-click preferences. Deserialization supplies
        // the model defaults for absent schema-1 properties; this makes the step
        // explicit before the normalized document is written back.
        data.Settings ??= new AppSettings();
        data.Settings.AutoClickIntervalMilliseconds = AppSettings.DefaultAutoClickIntervalMilliseconds;
        data.Settings.AutoClickHotkey = AppSettings.DefaultAutoClickHotkey;
        data.SchemaVersion = 2;
    }

    private static void EnsureSavedThemes(AppSettings settings)
    {
        settings.SavedThemes ??= [];
        settings.SavedThemes = settings.SavedThemes.Where(theme => theme is not null).ToList();
        foreach (var theme in settings.SavedThemes)
        {
            theme.Name = string.IsNullOrWhiteSpace(theme.Name) ? "Theme" : theme.Name.Trim();
            theme.Palette ??= new CustomThemePalette();
        }

        var crimson = EnsureBuiltInTheme(
            settings.SavedThemes,
            LocalizationService.Get("Settings.Theme.Crimson"),
            AppTheme.Crimson);
        var seaBlue = EnsureBuiltInTheme(
            settings.SavedThemes,
            LocalizationService.Get("Settings.Theme.SeaBlue"),
            AppTheme.SeaBlue);
        var stellaDark = EnsureBuiltInTheme(
            settings.SavedThemes,
            LocalizationService.Get("Settings.Theme.StellaDark"),
            AppTheme.StellaDark);

        var selectedCustomThemeExists = settings.SelectedThemeId is Guid selectedCustomId
            && settings.SavedThemes.Any(theme => theme.Id == selectedCustomId && !theme.IsBuiltIn);
        if (settings.Theme == AppTheme.Custom && !selectedCustomThemeExists)
        {
            var custom = new SavedTheme
            {
                Name = "Custom",
                Palette = settings.CustomTheme.Clone()
            };
            settings.SavedThemes.Add(custom);
            settings.SelectedThemeId = custom.Id;
        }

        if (settings.SelectedThemeId is Guid selectedId
            && settings.SavedThemes.Any(theme => theme.Id == selectedId))
        {
            return;
        }

        settings.SelectedThemeId = settings.Theme switch
        {
            AppTheme.SeaBlue => seaBlue.Id,
            AppTheme.StellaDark => stellaDark.Id,
            _ => crimson.Id
        };
    }

    private static SavedTheme EnsureBuiltInTheme(
        ICollection<SavedTheme> themes,
        string name,
        AppTheme appTheme)
    {
        var existing = themes.FirstOrDefault(theme =>
            theme.IsBuiltIn
            && string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var created = new SavedTheme
        {
            Name = name,
            IsBuiltIn = true,
            Palette = ThemeService.CreateEditablePalette(appTheme)
        };
        themes.Add(created);
        return created;
    }

    private static void NormalizeCalendarEvent(CalendarEvent calendarEvent)
    {
        calendarEvent.Title ??= string.Empty;
        calendarEvent.Notes ??= string.Empty;
        calendarEvent.Date = calendarEvent.Date == default ? DateTime.Today : calendarEvent.Date.Date;
        calendarEvent.EndDate = (calendarEvent.EndDate ?? calendarEvent.Date).Date;
        if (calendarEvent.EndDate < calendarEvent.Date)
            calendarEvent.EndDate = calendarEvent.Date;

        calendarEvent.RecurringDays ??= [];
        calendarEvent.RecurringDays = calendarEvent.RecurringDays
            .Where(Enum.IsDefined)
            .Distinct()
            .ToList();
        calendarEvent.ExcludedOccurrenceDates ??= [];
        calendarEvent.ExcludedOccurrenceDates = calendarEvent.ExcludedOccurrenceDates
            .Select(date => date.Date)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        if (!Enum.IsDefined(calendarEvent.Recurrence))
            calendarEvent.Recurrence = CalendarEventRecurrence.OneTime;
        if (!Enum.IsDefined(calendarEvent.Category))
            calendarEvent.Category = CalendarEventCategory.Life;

        if (calendarEvent.Recurrence == CalendarEventRecurrence.Weekly)
        {
            calendarEvent.EndDate = calendarEvent.Date;
            calendarEvent.ExcludedOccurrenceDates = calendarEvent.ExcludedOccurrenceDates
                .Where(date => date >= calendarEvent.Date)
                .ToList();
            calendarEvent.IsCompleted = false;
            calendarEvent.IsExpired = false;
        }
        else
        {
            calendarEvent.ExcludedOccurrenceDates.Clear();
            if (calendarEvent.IsCompleted)
                calendarEvent.IsExpired = false;
        }

        if (calendarEvent.IsCompleted || calendarEvent.IsExpired)
            calendarEvent.SnoozedUntil = null;
        CalendarOccurrenceStatusTracker.Normalize(calendarEvent);

        calendarEvent.ReminderAdvanceMinutes = Math.Clamp(calendarEvent.ReminderAdvanceMinutes, 0, 10080);
        calendarEvent.SnoozeMinutes = Math.Clamp(calendarEvent.SnoozeMinutes, 1, 1440);
        if (calendarEvent.IsAllDay)
        {
            calendarEvent.StartTime = TimeSpan.Zero;
            calendarEvent.EndTime = TimeSpan.Zero;
            return;
        }

        calendarEvent.StartTime = ClampTime(calendarEvent.StartTime);
        calendarEvent.EndTime = ClampEndTime(calendarEvent.EndTime);
        calendarEvent.EndTime = CalendarTimeRangePolicy.EnsureEndAfterStart(
            calendarEvent.StartTime,
            calendarEvent.EndTime);
    }

    private static void NormalizeCompanion(CompanionDefinition companion)
    {
        companion.Name ??= string.Empty;
        companion.Birthday ??= string.Empty;
        companion.UserNickname ??= string.Empty;
        companion.Animations ??= [];
        companion.IdleRandomAnimationIds ??= [];

        foreach (var scene in companion.Animations)
        {
            scene.Name ??= string.Empty;
            scene.AnimationPath ??= string.Empty;
            scene.PngFrames ??= [];
            scene.DialogLines ??= [];
            scene.DialogCharactersPerSecond = Math.Clamp(scene.DialogCharactersPerSecond, 5, 40);
            if (!Enum.IsDefined(scene.PlaybackMode))
                scene.PlaybackMode = ScenePlaybackMode.LoopUntilDialogEnds;
            if (!Enum.IsDefined(scene.FrameLoopMode))
                scene.FrameLoopMode = AnimationFrameLoopMode.EntireAnimation;
            if (scene.PngFrames.Count == 0)
            {
                AnimationFrameSettingsPolicy.ResetIfUnsupported(scene);
                continue;
            }

            scene.LoopStartFrameIndex = Math.Clamp(scene.LoopStartFrameIndex, 0, scene.PngFrames.Count - 1);
            scene.LoopEndFrameIndex = Math.Clamp(scene.LoopEndFrameIndex, 0, scene.PngFrames.Count - 1);
            if (scene.FrameLoopMode == AnimationFrameLoopMode.SingleFrame)
                scene.LoopEndFrameIndex = scene.LoopStartFrameIndex;
            else if (scene.LoopStartFrameIndex > scene.LoopEndFrameIndex)
                scene.LoopEndFrameIndex = scene.LoopStartFrameIndex;
            scene.DialogStartFrameIndex = Math.Clamp(
                scene.DialogStartFrameIndex,
                0,
                scene.GetLastFrameReachableBeforeLoopRepeats(scene.PngFrames.Count));
        }

        companion.IdleRandomAnimationIds = companion.IdleRandomAnimationIds
            .Where(id => companion.Animations.Any(scene => scene.Id == id))
            .Distinct()
            .ToList();
    }

    private static TimeSpan ClampTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;
        return value >= TimeSpan.FromDays(1)
            ? TimeSpan.FromHours(23.5)
            : value;
    }

    private static TimeSpan ClampEndTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;
        return value > TimeSpan.FromDays(1)
            ? TimeSpan.FromDays(1)
            : value;
    }
}
