using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Calendar;

public enum CalendarOccurrenceStatus
{
    Scheduled,
    ReminderDisabled,
    Completed,
    Expired
}

/// <summary>
/// Owns the persisted completed/expired state of individual calendar
/// occurrences. It is independent of reminder timing and UI presentation.
/// </summary>
public static class CalendarOccurrenceStatusTracker
{
    public static CalendarOccurrenceStatus Resolve(CalendarEvent calendarEvent, DateTime occurrenceDate)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        if (calendarEvent.Recurrence == CalendarEventRecurrence.OneTime)
        {
            if (calendarEvent.IsCompleted)
                return CalendarOccurrenceStatus.Completed;
            if (calendarEvent.IsExpired)
                return CalendarOccurrenceStatus.Expired;
        }
        else
        {
            var date = occurrenceDate.Date;
            if (ContainsDate(calendarEvent.CompletedOccurrences, date))
            {
                return CalendarOccurrenceStatus.Completed;
            }
            if (ContainsDate(calendarEvent.ExpiredOccurrences, date))
            {
                return CalendarOccurrenceStatus.Expired;
            }
        }

        return calendarEvent.ReminderEnabled
            ? CalendarOccurrenceStatus.Scheduled
            : CalendarOccurrenceStatus.ReminderDisabled;
    }

    public static void MarkCompleted(CalendarEvent calendarEvent, DateTime occurrence)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (calendarEvent.Recurrence == CalendarEventRecurrence.OneTime)
        {
            calendarEvent.IsCompleted = true;
            calendarEvent.IsExpired = false;
            return;
        }

        Normalize(calendarEvent);
        UpsertByDate(calendarEvent.CompletedOccurrences, occurrence);
        RemoveDate(calendarEvent.ExpiredOccurrences, occurrence.Date);
    }

    public static void MarkExpired(CalendarEvent calendarEvent, DateTime occurrence)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (calendarEvent.Recurrence == CalendarEventRecurrence.OneTime)
        {
            calendarEvent.IsCompleted = false;
            calendarEvent.IsExpired = true;
            return;
        }

        Normalize(calendarEvent);
        UpsertByDate(calendarEvent.ExpiredOccurrences, occurrence);
        RemoveDate(calendarEvent.CompletedOccurrences, occurrence.Date);
    }

    public static void Normalize(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        calendarEvent.CompletedOccurrences ??= [];
        calendarEvent.ExpiredOccurrences ??= [];

        if (calendarEvent.Recurrence != CalendarEventRecurrence.Weekly)
        {
            calendarEvent.CompletedOccurrences.Clear();
            calendarEvent.ExpiredOccurrences.Clear();
            if (calendarEvent.IsCompleted)
                calendarEvent.IsExpired = false;
            return;
        }

        calendarEvent.IsCompleted = false;
        calendarEvent.IsExpired = false;
        calendarEvent.CompletedOccurrences = NormalizeDates(
            calendarEvent.CompletedOccurrences,
            calendarEvent.Date);
        calendarEvent.ExpiredOccurrences = NormalizeDates(
            calendarEvent.ExpiredOccurrences,
            calendarEvent.Date)
            .Where(occurrence => !ContainsDate(calendarEvent.CompletedOccurrences, occurrence.Date))
            .ToList();
    }

    private static List<DateTime> NormalizeDates(IEnumerable<DateTime> occurrences, DateTime firstDate) =>
        occurrences
            .Where(occurrence => occurrence.Date >= firstDate.Date)
            .GroupBy(occurrence => occurrence.Date)
            .Select(group => group.Max())
            .OrderBy(occurrence => occurrence)
            .ToList();

    private static void UpsertByDate(List<DateTime> occurrences, DateTime occurrence)
    {
        RemoveDate(occurrences, occurrence.Date);
        occurrences.Add(occurrence);
        occurrences.Sort();
    }

    private static void RemoveDate(List<DateTime> occurrences, DateTime date) =>
        occurrences.RemoveAll(occurrence => occurrence.Date == date.Date);

    private static bool ContainsDate(IEnumerable<DateTime> occurrences, DateTime date) =>
        occurrences.Any(occurrence => occurrence.Date == date.Date);
}
