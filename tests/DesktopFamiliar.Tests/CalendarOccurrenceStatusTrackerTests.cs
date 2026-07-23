using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Tests;

public sealed class CalendarOccurrenceStatusTrackerTests
{
    [Theory]
    [InlineData(false, false, true, CalendarOccurrenceStatus.Scheduled)]
    [InlineData(false, false, false, CalendarOccurrenceStatus.ReminderDisabled)]
    [InlineData(true, false, true, CalendarOccurrenceStatus.Completed)]
    [InlineData(false, true, true, CalendarOccurrenceStatus.Expired)]
    public void Resolve_OneTimeEventReturnsPersistedStatus(
        bool isCompleted,
        bool isExpired,
        bool reminderEnabled,
        CalendarOccurrenceStatus expected)
    {
        var calendarEvent = new CalendarEvent
        {
            IsCompleted = isCompleted,
            IsExpired = isExpired,
            ReminderEnabled = reminderEnabled
        };

        var status = CalendarOccurrenceStatusTracker.Resolve(calendarEvent, calendarEvent.Date);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void WeeklyOccurrencesRetainIndependentCompletedAndExpiredStatuses()
    {
        var first = new DateTime(2026, 7, 20, 12, 0, 0);
        var second = first.AddDays(7);
        var calendarEvent = CreateWeeklyEvent(first.Date);

        CalendarOccurrenceStatusTracker.MarkCompleted(calendarEvent, first);
        CalendarOccurrenceStatusTracker.MarkExpired(calendarEvent, second);

        Assert.Equal(CalendarOccurrenceStatus.Completed,
            CalendarOccurrenceStatusTracker.Resolve(calendarEvent, first));
        Assert.Equal(CalendarOccurrenceStatus.Expired,
            CalendarOccurrenceStatusTracker.Resolve(calendarEvent, second));
        Assert.Equal(CalendarOccurrenceStatus.Scheduled,
            CalendarOccurrenceStatusTracker.Resolve(calendarEvent, second.AddDays(7)));
        Assert.Equal([first], calendarEvent.CompletedOccurrences);
        Assert.Equal([second], calendarEvent.ExpiredOccurrences);
    }

    [Fact]
    public void MarkingAnOccurrenceReplacesItsPreviousTerminalStatus()
    {
        var occurrence = new DateTime(2026, 7, 20, 12, 0, 0);
        var calendarEvent = CreateWeeklyEvent(occurrence.Date);

        CalendarOccurrenceStatusTracker.MarkExpired(calendarEvent, occurrence);
        CalendarOccurrenceStatusTracker.MarkCompleted(calendarEvent, occurrence.AddMinutes(5));

        Assert.Equal(CalendarOccurrenceStatus.Completed,
            CalendarOccurrenceStatusTracker.Resolve(calendarEvent, occurrence));
        Assert.Empty(calendarEvent.ExpiredOccurrences);
        Assert.Equal(occurrence.AddMinutes(5), Assert.Single(calendarEvent.CompletedOccurrences));
    }

    [Fact]
    public void NormalizeDeduplicatesAndOrdersOccurrenceHistory()
    {
        var completed = new DateTime(2026, 7, 20, 12, 0, 0);
        var expired = completed.AddDays(7);
        var calendarEvent = CreateWeeklyEvent(completed.Date);
        calendarEvent.CompletedOccurrences = [completed.AddMinutes(-5), completed];
        calendarEvent.ExpiredOccurrences = [expired, expired.AddMinutes(-5)];

        CalendarOccurrenceStatusTracker.Normalize(calendarEvent);

        Assert.Equal([completed], calendarEvent.CompletedOccurrences);
        Assert.Equal([expired], calendarEvent.ExpiredOccurrences);
    }

    private static CalendarEvent CreateWeeklyEvent(DateTime date) => new()
    {
        Date = date,
        EndDate = date,
        Recurrence = CalendarEventRecurrence.Weekly,
        RecurringDays = [date.DayOfWeek]
    };
}
