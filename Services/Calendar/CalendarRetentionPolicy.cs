using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Calendar;

/// <summary>
/// Removes terminal one-time reminders and recurring occurrence history once
/// they are at least one year old. Active recurring series themselves remain.
/// </summary>
public static class CalendarRetentionPolicy
{
    public static bool Prune(IList<CalendarEvent> calendarEvents, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(calendarEvents);
        var cutoff = today.Date.AddYears(-1);
        var changed = false;

        for (var index = calendarEvents.Count - 1; index >= 0; index--)
        {
            var calendarEvent = calendarEvents[index];
            if (calendarEvent.Recurrence != CalendarEventRecurrence.Weekly)
            {
                var terminalDate = (calendarEvent.EndDate ?? calendarEvent.Date).Date;
                if ((calendarEvent.IsCompleted || calendarEvent.IsExpired) && terminalDate <= cutoff)
                {
                    calendarEvents.RemoveAt(index);
                    changed = true;
                }
                continue;
            }

            if (!IsRecurringSeriesActive(calendarEvent))
            {
                var lastCompleted = calendarEvent.CompletedOccurrences.DefaultIfEmpty(DateTime.MinValue).Max();
                if (lastCompleted != DateTime.MinValue && lastCompleted.Date <= cutoff)
                {
                    calendarEvents.RemoveAt(index);
                    changed = true;
                }
                continue;
            }

            var completed = calendarEvent.CompletedOccurrences
                .Where(occurrence => occurrence.Date > cutoff)
                .ToList();
            var expired = calendarEvent.ExpiredOccurrences
                .Where(occurrence => occurrence.Date > cutoff)
                .ToList();
            if (completed.Count == calendarEvent.CompletedOccurrences.Count
                && expired.Count == calendarEvent.ExpiredOccurrences.Count)
            {
                continue;
            }

            calendarEvent.CompletedOccurrences = completed;
            calendarEvent.ExpiredOccurrences = expired;
            CalendarOccurrenceStatusTracker.Normalize(calendarEvent);
            changed = true;
        }

        return changed;
    }

    // Weekly series currently have no configured end date, so every valid
    // weekly definition with at least one occurrence day remains active.
    // Keeping this decision here makes a future recurrence-end field a local
    // policy change.
    public static bool IsRecurringSeriesActive(CalendarEvent calendarEvent) =>
        calendarEvent.Recurrence == CalendarEventRecurrence.Weekly
        && calendarEvent.Date != default
        && calendarEvent.RecurringDays is { Count: > 0 };
}
