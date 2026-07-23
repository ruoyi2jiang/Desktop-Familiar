using System.Windows.Threading;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Calendar;
using DesktopFamiliar.Services.Time;

namespace DesktopFamiliar.Services;

public sealed class CalendarReminderEventArgs(
    CalendarEvent calendarEvent,
    DateTime occurrenceStart) : EventArgs
{
    public CalendarEvent CalendarEvent { get; } = calendarEvent;
    public DateTime OccurrenceStart { get; } = occurrenceStart;
}

public enum CalendarReminderEndReason
{
    Snoozed,
    Acknowledged,
    Expired,
    Removed,
    OccurrenceDeleted
}

public sealed class CalendarReminderEndedEventArgs(
    CalendarReminderEndReason reason,
    bool wasPresenting) : EventArgs
{
    public CalendarReminderEndReason Reason { get; } = reason;
    public bool WasPresenting { get; } = wasPresenting;
}

/// <summary>
/// Schedules and persists one active reminder occurrence at a time. Presentation
/// is raised again after a snooze, while acknowledgement and expiration mark the
/// occurrence as handled so it cannot be raised again.
/// </summary>
public sealed class CalendarReminderService : IDisposable
{
    private readonly CompanionManager _manager;
    private readonly DispatcherTimer _timer;
    private readonly IClock _clock;
    private Guid? _activeEventId;
    private DateTime? _activeOccurrence;
    private bool _isPresenting;
    private bool _isChecking;
    private DateTime? _lastRetentionDate;

    public event EventHandler<CalendarReminderEventArgs>? ReminderRaised;
    public event EventHandler<CalendarReminderEndedEventArgs>? ReminderEnded;

    public CalendarEvent? ActiveReminder => _activeEventId is Guid id
        ? _manager.Data.CalendarEvents.FirstOrDefault(calendarEvent => calendarEvent.Id == id)
        : null;

    public DateTime? ActiveOccurrence => _activeOccurrence;

    public CalendarReminderService(CompanionManager manager, IClock? clock = null)
    {
        _manager = manager;
        _clock = clock ?? SystemClock.Instance;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _timer.Tick += (_, _) => CheckForDueReminderSafely();
        _manager.CalendarEventsChanged += ManagerCalendarEventsChanged;
    }

    public void Start()
    {
        _timer.Start();
        CheckForDueReminderSafely();
    }

    public void CheckNow() => CheckForDueReminderSafely();

    public void SnoozeActiveReminder()
    {
        if (ActiveReminder is not { } calendarEvent)
            return;
        if (TryExpireActiveReminder(calendarEvent))
            return;

        calendarEvent.SnoozedUntil = _clock.Now.AddMinutes(Math.Clamp(calendarEvent.SnoozeMinutes, 1, 1440));
        _manager.UpdateCalendarReminderState(calendarEvent);
        var wasPresenting = _isPresenting;
        _isPresenting = false;
        ReminderEnded?.Invoke(
            this,
            new CalendarReminderEndedEventArgs(CalendarReminderEndReason.Snoozed, wasPresenting));
    }

    public void AcknowledgeActiveReminder()
    {
        if (ActiveReminder is not { } calendarEvent)
            return;
        if (TryExpireActiveReminder(calendarEvent))
            return;

        var occurrence = _activeOccurrence
            ?? CalendarOccurrenceCalculator.GetDueOccurrence(calendarEvent, _clock.Now)
            ?? _clock.Now;
        CalendarOccurrenceStatusTracker.MarkCompleted(calendarEvent, occurrence);
        calendarEvent.SnoozedUntil = null;
        _manager.UpdateCalendarReminderState(calendarEvent);
        EndActiveReminder(CalendarReminderEndReason.Acknowledged);
    }

    private void ManagerCalendarEventsChanged(object? sender, EventArgs e)
    {
        if (_activeEventId is Guid id)
        {
            var active = _manager.Data.CalendarEvents.FirstOrDefault(calendarEvent => calendarEvent.Id == id);
            if (active is null)
            {
                EndActiveReminder(CalendarReminderEndReason.Removed);
                return;
            }
            if (_activeOccurrence is DateTime occurrence
                && CalendarOccurrenceCalculator.IsOccurrenceExcluded(active, occurrence))
            {
                EndActiveReminder(CalendarReminderEndReason.OccurrenceDeleted);
                return;
            }
        }
        CheckForDueReminderSafely();
    }

    private void CheckForDueReminderSafely()
    {
        try
        {
            CheckForDueReminder();
        }
        catch (Exception exception)
        {
            AppLogger.Warning("Calendar reminders could not be refreshed and will be retried.", exception);
        }
    }

    private void CheckForDueReminder()
    {
        if (_isChecking)
            return;

        _isChecking = true;
        try
        {
            CheckForDueReminderCore();
        }
        finally
        {
            _isChecking = false;
        }
    }

    private void CheckForDueReminderCore()
    {
        var now = _clock.Now;
        if (_lastRetentionDate != now.Date)
        {
            _manager.PruneCalendarHistory(now.Date);
            _lastRetentionDate = now.Date;
        }
        if (ActiveReminder is { } active)
        {
            if (TryExpireActiveReminder(active))
                return;
            if (active.SnoozedUntil is DateTime snoozedUntil && snoozedUntil > now)
                return;
            if (active.SnoozedUntil is not null)
            {
                active.SnoozedUntil = null;
                _manager.UpdateCalendarReminderState(active);
            }
            RaiseActiveReminder(active);
            return;
        }

        while (true)
        {
            var expiredOccurrenceHandled = false;
            var candidates = _manager.Data.CalendarEvents
                .Where(calendarEvent =>
                    calendarEvent.ReminderEnabled
                    && !calendarEvent.IsCompleted
                    && !calendarEvent.IsExpired)
                .Select(calendarEvent => new
                {
                    CalendarEvent = calendarEvent,
                    Occurrence = CalendarOccurrenceCalculator.GetDueOccurrence(calendarEvent, now)
                })
                .Where(candidate => candidate.Occurrence is not null)
                .OrderByDescending(candidate => candidate.CalendarEvent.IsUrgent)
                .ThenBy(candidate => candidate.Occurrence)
                .ToList();

            foreach (var candidate in candidates)
            {
                var occurrence = candidate.Occurrence!.Value;
                if (CalendarOccurrenceCalculator.HasOccurrenceEnded(candidate.CalendarEvent, occurrence, now))
                {
                    MarkOccurrenceExpired(candidate.CalendarEvent, occurrence);
                    expiredOccurrenceHandled = true;
                    break;
                }
                if (candidate.CalendarEvent.SnoozedUntil is DateTime snoozedUntil
                    && snoozedUntil > now)
                {
                    continue;
                }

                if (candidate.CalendarEvent.SnoozedUntil is not null)
                {
                    candidate.CalendarEvent.SnoozedUntil = null;
                    _manager.UpdateCalendarReminderState(candidate.CalendarEvent);
                }
                _activeEventId = candidate.CalendarEvent.Id;
                _activeOccurrence = occurrence;
                RaiseActiveReminder(candidate.CalendarEvent);
                return;
            }

            if (!expiredOccurrenceHandled)
                return;
        }
    }

    private void RaiseActiveReminder(CalendarEvent calendarEvent)
    {
        if (_isPresenting)
            return;
        var occurrence = _activeOccurrence
            ?? CalendarOccurrenceCalculator.GetDueOccurrence(calendarEvent, _clock.Now);
        if (occurrence is null)
            return;

        _isPresenting = true;
        try
        {
            ReminderRaised?.Invoke(this, new CalendarReminderEventArgs(calendarEvent, occurrence.Value));
        }
        catch
        {
            _isPresenting = false;
            throw;
        }
    }

    private bool TryExpireActiveReminder(CalendarEvent calendarEvent)
    {
        var occurrence = _activeOccurrence
            ?? CalendarOccurrenceCalculator.GetDueOccurrence(calendarEvent, _clock.Now);
        if (occurrence is null
            || !CalendarOccurrenceCalculator.HasOccurrenceEnded(calendarEvent, occurrence.Value, _clock.Now))
        {
            return false;
        }

        var enteredCheckGuard = !_isChecking;
        if (enteredCheckGuard)
            _isChecking = true;
        try
        {
            MarkOccurrenceExpired(calendarEvent, occurrence.Value);
        }
        finally
        {
            if (enteredCheckGuard)
                _isChecking = false;
        }
        EndActiveReminder(CalendarReminderEndReason.Expired);
        return true;
    }

    private void MarkOccurrenceExpired(CalendarEvent calendarEvent, DateTime occurrence)
    {
        CalendarOccurrenceStatusTracker.MarkExpired(calendarEvent, occurrence);
        calendarEvent.SnoozedUntil = null;
        _manager.UpdateCalendarReminderState(calendarEvent);
    }

    private void EndActiveReminder(CalendarReminderEndReason reason)
    {
        var wasPresenting = _isPresenting;
        _activeEventId = null;
        _activeOccurrence = null;
        _isPresenting = false;
        ReminderEnded?.Invoke(this, new CalendarReminderEndedEventArgs(reason, wasPresenting));
    }

    public void Dispose()
    {
        _timer.Stop();
        _manager.CalendarEventsChanged -= ManagerCalendarEventsChanged;
    }
}
