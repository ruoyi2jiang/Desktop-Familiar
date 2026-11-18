using DesktopFamiliar.Models;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services.Calendar;
using DesktopFamiliar.Services.Focus;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Services.Validation;

namespace DesktopFamiliar.Services;

public sealed class CompanionManager
{
    private readonly ICompanionRepository _storage;
    private DesktopCompanionData _lastPersistedData;

    public DesktopCompanionData Data { get; }
    public CompanionDefinition? CurrentCompanion => Data.Settings.CurrentCompanionId is Guid id
        ? Data.Companions.FirstOrDefault(companion => companion.Id == id)
        : null;
    public CompanionActivityState ActivityState { get; private set; }
    public bool ShouldDisplayCompanion => FocusModeVisibilityPolicy.ShouldDisplay(
        hasCurrentCompanion: CurrentCompanion is not null,
        focusModeEnabled: Data.Settings.FocusModeEnabled,
        activityState: ActivityState);

    public event EventHandler? CurrentCompanionChanged;
    public event EventHandler? CompanionCollectionChanged;
    public event EventHandler? ChatSettingsChanged;
    public event EventHandler? FocusModeChanged;
    public event EventHandler? ActivityStateChanged;
    public event EventHandler? ViewportChanged;
    public event EventHandler? DialogPresentationChanged;
    public event EventHandler? IdleIntervalChanged;
    public event EventHandler? UserBirthdayChanged;
    public event EventHandler? ThemeChanged;
    public event EventHandler? CalendarEventsChanged;

    public CompanionManager(ICompanionRepository storage)
    {
        _storage = storage;
        Data = storage.Load();
        Data.Settings.RunAtStartup = StartupService.IsEnabled();

        if (CurrentCompanion is null && Data.Companions.Count > 0)
            Data.Settings.CurrentCompanionId = Data.Companions[0].Id;

        _lastPersistedData = Data.Clone();
    }

    public void SaveCompanion(CompanionDefinition companion)
    {
        var errors = CompanionValidator.Validate(companion);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

        var index = Data.Companions.FindIndex(existing => existing.Id == companion.Id);
        if (index >= 0)
            Data.Companions[index] = companion.Clone();
        else
            Data.Companions.Add(companion.Clone());

        Data.Settings.CurrentCompanionId ??= companion.Id;
        Persist();
        CompanionCollectionChanged?.Invoke(this, EventArgs.Empty);
        if (Data.Settings.CurrentCompanionId == companion.Id)
            CurrentCompanionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteCompanion(Guid companionId)
    {
        var removed = Data.Companions.RemoveAll(companion => companion.Id == companionId) > 0;
        if (!removed)
            return;

        var currentChanged = Data.Settings.CurrentCompanionId == companionId;
        if (currentChanged)
            Data.Settings.CurrentCompanionId = Data.Companions.FirstOrDefault()?.Id;

        Persist();
        CompanionCollectionChanged?.Invoke(this, EventArgs.Empty);
        if (currentChanged)
            CurrentCompanionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectCompanion(Guid companionId)
    {
        if (Data.Companions.All(companion => companion.Id != companionId) || Data.Settings.CurrentCompanionId == companionId)
            return;
        Data.Settings.CurrentCompanionId = companionId;
        Persist();
        CurrentCompanionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetChatPreferences(ChatPreferences preferences)
    {
        if (preferences == Data.Settings.Chat) return;
        Data.Settings.Chat = preferences;
        Persist();
        ChatSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFocusMode(bool enabled)
    {
        if (Data.Settings.FocusModeEnabled == enabled)
            return;

        Data.Settings.FocusModeEnabled = enabled;
        Persist();
        FocusModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActivityState(CompanionActivityState state)
    {
        if (ActivityState == state)
            return;
        ActivityState = state;
        ActivityStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetUserBirthday(DateTime? birthday)
    {
        birthday = birthday?.Date;
        if (Data.Settings.UserBirthday == birthday)
            return;

        Data.Settings.UserBirthday = birthday;
        Persist();
        UserBirthdayChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetRunAtStartup(bool enabled)
    {
        var previous = Data.Settings.RunAtStartup;
        StartupService.SetEnabled(enabled);
        Data.Settings.RunAtStartup = enabled;
        try
        {
            Persist();
        }
        catch
        {
            try
            {
                StartupService.SetEnabled(previous);
            }
            catch (Exception rollbackException)
            {
                AppLogger.Error("The Windows startup setting could not be rolled back after a save failure.", rollbackException);
            }
            throw;
        }
    }

    public void SetViewportSize(int width, int height)
    {
        width = Math.Clamp(width, AppSettings.MinimumViewportSize, AppSettings.MaximumViewportSize);
        height = Math.Clamp(height, AppSettings.MinimumViewportSize, AppSettings.MaximumViewportSize);
        if (Data.Settings.ViewportWidth == width && Data.Settings.ViewportHeight == height)
            return;

        Data.Settings.ViewportWidth = width;
        Data.Settings.ViewportHeight = height;
        Persist();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTruncateLongDialogLines(bool enabled)
    {
        if (Data.Settings.TruncateLongDialogLines == enabled)
            return;

        Data.Settings.TruncateLongDialogLines = enabled;
        Persist();
        DialogPresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDialogBubbleHidden(bool hidden)
    {
        if (Data.Settings.DialogBubbleHidden == hidden)
            return;

        Data.Settings.DialogBubbleHidden = hidden;
        Persist();
        DialogPresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetIdleDelaySeconds(double seconds)
    {
        seconds = Math.Clamp(
            seconds,
            AppSettings.MinimumIdleDelaySeconds,
            AppSettings.MaximumIdleDelaySeconds);
        if (Math.Abs(Data.Settings.IdleDelaySeconds - seconds) < 0.001)
            return;

        Data.Settings.IdleDelaySeconds = seconds;
        Persist();
        IdleIntervalChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTheme(AppTheme theme)
    {
        if (Data.Settings.Theme == theme)
            return;

        Data.Settings.Theme = theme;
        Persist();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public SavedTheme CurrentSavedTheme => Data.Settings.SavedThemes.FirstOrDefault(theme => theme.Id == Data.Settings.SelectedThemeId)
        ?? Data.Settings.SavedThemes.First();

    public void SelectSavedTheme(Guid themeId)
    {
        var selectedTheme = Data.Settings.SavedThemes.FirstOrDefault(theme => theme.Id == themeId);
        if (selectedTheme is null)
            return;

        var selectedThemeKind = ThemeSelectionPolicy.Resolve(selectedTheme);
        if (Data.Settings.SelectedThemeId == themeId && Data.Settings.Theme == selectedThemeKind)
            return;

        var previousThemeId = Data.Settings.SelectedThemeId;
        var previousThemeKind = Data.Settings.Theme;
        var previousPalette = Data.Settings.CustomTheme.Clone();
        Data.Settings.SelectedThemeId = themeId;
        Data.Settings.Theme = selectedThemeKind;
        Data.Settings.CustomTheme = selectedTheme.Palette.Clone();
        try
        {
            Persist();
        }
        catch
        {
            Data.Settings.SelectedThemeId = previousThemeId;
            Data.Settings.Theme = previousThemeKind;
            Data.Settings.CustomTheme = previousPalette;
            throw;
        }
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveSavedTheme(SavedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var existingTheme = Data.Settings.SavedThemes.FirstOrDefault(existing => existing.Id == theme.Id);
        if (existingTheme?.IsBuiltIn == true)
            throw new InvalidOperationException("Built-in themes cannot be edited.");

        var saved = theme.Clone();
        saved.Name = saved.Name.Trim();
        if (string.IsNullOrWhiteSpace(saved.Name))
            throw new InvalidOperationException("A theme name is required.");

        var previousThemes = Data.Settings.SavedThemes.Select(existing => existing.Clone()).ToList();
        var previousPalette = Data.Settings.CustomTheme.Clone();
        var index = Data.Settings.SavedThemes.FindIndex(existing => existing.Id == saved.Id);
        if (index >= 0)
            Data.Settings.SavedThemes[index] = saved;
        else
            Data.Settings.SavedThemes.Add(saved);

        if (Data.Settings.SelectedThemeId == saved.Id)
            Data.Settings.CustomTheme = saved.Palette.Clone();

        try
        {
            Persist();
        }
        catch
        {
            Data.Settings.SavedThemes = previousThemes;
            Data.Settings.CustomTheme = previousPalette;
            throw;
        }
        if (Data.Settings.SelectedThemeId == saved.Id)
            ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSavedTheme(Guid themeId)
    {
        if (Data.Settings.SavedThemes.Count <= 1)
            throw new InvalidOperationException("At least one theme is required.");

        var theme = Data.Settings.SavedThemes.FirstOrDefault(candidate => candidate.Id == themeId);
        if (theme?.IsBuiltIn == true)
            throw new InvalidOperationException("Built-in themes cannot be deleted.");

        var previousThemes = Data.Settings.SavedThemes.Select(existing => existing.Clone()).ToList();
        var previousThemeId = Data.Settings.SelectedThemeId;
        var previousThemeKind = Data.Settings.Theme;
        var previousPalette = Data.Settings.CustomTheme.Clone();
        var removed = Data.Settings.SavedThemes.RemoveAll(theme => theme.Id == themeId);
        if (removed == 0)
            return;

        if (Data.Settings.SelectedThemeId == themeId)
        {
            var nextTheme = Data.Settings.SavedThemes[0];
            Data.Settings.SelectedThemeId = nextTheme.Id;
            Data.Settings.Theme = ThemeSelectionPolicy.Resolve(nextTheme);
            Data.Settings.CustomTheme = nextTheme.Palette.Clone();
        }

        try
        {
            Persist();
        }
        catch
        {
            Data.Settings.SavedThemes = previousThemes;
            Data.Settings.SelectedThemeId = previousThemeId;
            Data.Settings.Theme = previousThemeKind;
            Data.Settings.CustomTheme = previousPalette;
            throw;
        }
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveCustomTheme(CustomThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        Data.Settings.CustomTheme = palette.Clone();
        Persist();
        if (Data.Settings.Theme == AppTheme.Custom)
            ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SavePosition(double left, double top)
    {
        Data.Settings.CompanionLeft = left;
        Data.Settings.CompanionTop = top;
        Persist();
    }

    public void SaveCalendarEvent(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (string.IsNullOrWhiteSpace(calendarEvent.Title))
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.Title"));

        var savedEvent = calendarEvent.Clone();
        savedEvent.Title = savedEvent.Title.Trim();
        savedEvent.Notes = savedEvent.Notes.Trim();
        savedEvent.Date = savedEvent.Date.Date;
        savedEvent.EndDate = (savedEvent.EndDate ?? savedEvent.Date).Date;
        savedEvent.ReminderAdvanceMinutes = Math.Clamp(savedEvent.ReminderAdvanceMinutes, 0, 10080);
        savedEvent.SnoozeMinutes = Math.Clamp(savedEvent.SnoozeMinutes, 1, 1440);
        savedEvent.RecurringDays = savedEvent.RecurringDays.Distinct().ToList();
        savedEvent.ExcludedOccurrenceDates = savedEvent.ExcludedOccurrenceDates
            .Select(date => date.Date)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        CalendarOccurrenceStatusTracker.Normalize(savedEvent);
        var index = Data.CalendarEvents.FindIndex(existing => existing.Id == savedEvent.Id);
        if (index < 0 && savedEvent.Date < DateTime.Today)
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.PastDate"));
        if (savedEvent.EndDate < savedEvent.Date)
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.DateRange"));
        if (savedEvent.Recurrence == CalendarEventRecurrence.Weekly && savedEvent.RecurringDays.Count == 0)
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.RecurringDays"));
        if (savedEvent.Recurrence == CalendarEventRecurrence.Weekly && savedEvent.EndDate > savedEvent.Date)
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.RecurringSpan"));
        if (!savedEvent.IsAllDay && savedEvent.EndTime <= savedEvent.StartTime)
            throw new InvalidOperationException(LocalizationService.Get("Calendar.Validation.TimeRange"));
        if (savedEvent.Recurrence == CalendarEventRecurrence.Weekly)
        {
            savedEvent.ExcludedOccurrenceDates = savedEvent.ExcludedOccurrenceDates
                .Where(date => date >= savedEvent.Date)
                .ToList();
        }
        else
        {
            savedEvent.ExcludedOccurrenceDates.Clear();
        }

        if (index >= 0)
            Data.CalendarEvents[index] = savedEvent;
        else
            Data.CalendarEvents.Add(savedEvent);
        Persist();
        CalendarEventsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteCalendarEvent(Guid eventId)
    {
        if (Data.CalendarEvents.RemoveAll(calendarEvent => calendarEvent.Id == eventId) == 0)
            return;
        Persist();
        CalendarEventsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteCalendarOccurrence(Guid eventId, DateTime occurrenceDate)
    {
        var index = Data.CalendarEvents.FindIndex(calendarEvent => calendarEvent.Id == eventId);
        if (index < 0)
            return;

        var calendarEvent = Data.CalendarEvents[index].Clone();
        occurrenceDate = occurrenceDate.Date;
        if (calendarEvent.Recurrence != CalendarEventRecurrence.Weekly)
            throw new InvalidOperationException("Only an occurrence in a weekly series can be deleted independently.");
        if (!CalendarOccurrenceCalculator.OccursOnDate(calendarEvent, occurrenceDate))
            return;

        calendarEvent.ExcludedOccurrenceDates.Add(occurrenceDate);
        calendarEvent.ExcludedOccurrenceDates = calendarEvent.ExcludedOccurrenceDates
            .Select(date => date.Date)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        Data.CalendarEvents[index] = calendarEvent;
        Persist();
        CalendarEventsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateCalendarReminderState(CalendarEvent calendarEvent)
    {
        var index = Data.CalendarEvents.FindIndex(existing => existing.Id == calendarEvent.Id);
        if (index < 0)
            return;
        Data.CalendarEvents[index] = calendarEvent.Clone();
        Persist();
        CalendarEventsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool PruneCalendarHistory(DateTime today)
    {
        if (!CalendarRetentionPolicy.Prune(Data.CalendarEvents, today))
            return false;

        Persist();
        CalendarEventsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Persist()
    {
        try
        {
            _storage.Save(Data);
            _lastPersistedData = Data.Clone();
        }
        catch (Exception exception)
        {
            Data.RestoreFrom(_lastPersistedData);
            AppLogger.Error("The application configuration could not be saved; the last saved state was restored.", exception);
            throw new AppPersistenceException(
                LocalizationService.Get("Storage.ConfigurationSaveFailed"),
                exception);
        }
    }
}
