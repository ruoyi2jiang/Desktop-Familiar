using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Calendar;

/// <summary>
/// Pure calendar recurrence calculations. Reminder presentation and persistence
/// are intentionally handled elsewhere.
/// </summary>
public static class CalendarOccurrenceCalculator
{
    public static DateTime? GetDueOccurrence(CalendarEvent calendarEvent, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        var eventTime = calendarEvent.IsAllDay ? TimeSpan.Zero : calendarEvent.StartTime;

        if (calendarEvent.Recurrence == CalendarEventRecurrence.OneTime)
        {
            if (calendarEvent.IsCompleted || calendarEvent.IsExpired)
                return null;
            var occurrence = calendarEvent.Date.Date.Add(eventTime);
            return now >= occurrence.AddMinutes(-calendarEvent.ReminderAdvanceMinutes)
                ? occurrence
                : null;
        }

        var days = calendarEvent.RecurringDays.Count == 0
            ? new HashSet<DayOfWeek> { calendarEvent.Date.DayOfWeek }
            : calendarEvent.RecurringDays.ToHashSet();
        var reminderHorizon = now.AddMinutes(calendarEvent.ReminderAdvanceMinutes);
        var firstDate = Max(calendarEvent.Date.Date, now.Date.AddDays(-7));
        var lastDate = reminderHorizon.Date;

        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (!days.Contains(date.DayOfWeek))
                continue;

            var occurrence = date.Add(eventTime);
            if (occurrence > reminderHorizon)
                continue;
            if (IsOccurrenceExcluded(calendarEvent, occurrence))
                continue;
            if (calendarEvent.CompletedOccurrences.Any(item => item.Date == occurrence.Date)
                || calendarEvent.ExpiredOccurrences.Any(item => item.Date == occurrence.Date))
            {
                continue;
            }
            return occurrence;
        }

        return null;
    }

    public static DateTime GetOccurrenceEnd(CalendarEvent calendarEvent, DateTime occurrenceStart)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        var endDate = calendarEvent.Recurrence == CalendarEventRecurrence.Weekly
            ? occurrenceStart.Date
            : (calendarEvent.EndDate ?? calendarEvent.Date).Date;
        return calendarEvent.IsAllDay
            ? endDate.AddDays(1)
            : endDate.Add(calendarEvent.EndTime);
    }

    public static bool HasOccurrenceEnded(
        CalendarEvent calendarEvent,
        DateTime occurrenceStart,
        DateTime now) =>
        now >= GetOccurrenceEnd(calendarEvent, occurrenceStart);

    public static bool OccursOnDate(CalendarEvent calendarEvent, DateTime date)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        date = date.Date;
        if (calendarEvent.Recurrence == CalendarEventRecurrence.Weekly)
        {
            var repeatingDays = calendarEvent.RecurringDays.Count == 0
                ? new HashSet<DayOfWeek> { calendarEvent.Date.DayOfWeek }
                : calendarEvent.RecurringDays.ToHashSet();
            return date >= calendarEvent.Date.Date
                && repeatingDays.Contains(date.DayOfWeek)
                && !IsOccurrenceExcluded(calendarEvent, date);
        }

        var endDate = (calendarEvent.EndDate ?? calendarEvent.Date).Date;
        return date >= calendarEvent.Date.Date && date <= endDate;
    }

    public static bool IsOccurrenceExcluded(CalendarEvent calendarEvent, DateTime occurrence)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        return calendarEvent.Recurrence == CalendarEventRecurrence.Weekly
            && calendarEvent.ExcludedOccurrenceDates.Any(date => date.Date == occurrence.Date);
    }

    private static DateTime Max(DateTime first, DateTime second) => first >= second ? first : second;

}
