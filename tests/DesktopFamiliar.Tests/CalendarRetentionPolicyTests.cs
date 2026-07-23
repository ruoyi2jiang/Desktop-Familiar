using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Tests;

public sealed class CalendarRetentionPolicyTests
{
    private static readonly DateTime Today = new(2026, 7, 21);

    [Fact]
    public void OldTerminalOneTimeRemindersAreRemovedButUnhandledEventsRemain()
    {
        var oldCompleted = new CalendarEvent
        {
            Date = Today.AddYears(-1),
            EndDate = Today.AddYears(-1),
            IsCompleted = true
        };
        var oldUnhandled = new CalendarEvent
        {
            Date = Today.AddYears(-2),
            EndDate = Today.AddYears(-2)
        };
        var recentExpired = new CalendarEvent
        {
            Date = Today.AddMonths(-6),
            EndDate = Today.AddMonths(-6),
            IsExpired = true
        };
        var events = new List<CalendarEvent> { oldCompleted, oldUnhandled, recentExpired };

        var changed = CalendarRetentionPolicy.Prune(events, Today);

        Assert.True(changed);
        Assert.DoesNotContain(oldCompleted, events);
        Assert.Contains(oldUnhandled, events);
        Assert.Contains(recentExpired, events);
    }

    [Fact]
    public void ActiveWeeklySeriesIsKeptWhileOldOccurrenceStatusesArePruned()
    {
        var oldCompleted = Today.AddYears(-1);
        var recentCompleted = Today.AddMonths(-2);
        var oldExpired = Today.AddYears(-2);
        var recentExpired = Today.AddMonths(-1);
        var weekly = new CalendarEvent
        {
            Date = Today.AddYears(-3),
            Recurrence = CalendarEventRecurrence.Weekly,
            RecurringDays = [DayOfWeek.Tuesday],
            CompletedOccurrences = [oldCompleted, recentCompleted],
            ExpiredOccurrences = [oldExpired, recentExpired]
        };
        var events = new List<CalendarEvent> { weekly };

        var changed = CalendarRetentionPolicy.Prune(events, Today);

        Assert.True(changed);
        Assert.Same(weekly, Assert.Single(events));
        Assert.Equal([recentCompleted], weekly.CompletedOccurrences);
        Assert.Equal([recentExpired], weekly.ExpiredOccurrences);
    }

    [Fact]
    public void InactiveRecurringSeriesUsesCompletedOccurrenceHistoryForRetention()
    {
        var completed = Today.AddYears(-2);
        var inactive = new CalendarEvent
        {
            Date = Today.AddYears(-3),
            Recurrence = CalendarEventRecurrence.Weekly,
            RecurringDays = [],
            CompletedOccurrences = [completed]
        };
        var events = new List<CalendarEvent> { inactive };

        Assert.True(CalendarRetentionPolicy.Prune(events, Today));
        Assert.Empty(events);
    }
}
