using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Tests;

public sealed class CalendarOccurrenceCalculatorTests
{
    [Fact]
    public void WeeklyEvent_ReturnsOldestUncompletedDueOccurrence()
    {
        var calendarEvent = CreateWeeklyEvent();
        var fridayMorning = new DateTime(2026, 6, 5, 9, 0, 0);

        var occurrence = CalendarOccurrenceCalculator.GetDueOccurrence(calendarEvent, fridayMorning);

        Assert.Equal(new DateTime(2026, 6, 1, 10, 0, 0), occurrence);
    }

    [Fact]
    public void WeeklyEvent_AfterPriorCompletionReturnsNextDueOccurrence()
    {
        var calendarEvent = CreateWeeklyEvent();
        calendarEvent.CompletedOccurrences.Add(new DateTime(2026, 6, 1, 10, 0, 0));
        var fridayMorning = new DateTime(2026, 6, 5, 9, 0, 0);

        var occurrence = CalendarOccurrenceCalculator.GetDueOccurrence(calendarEvent, fridayMorning);

        Assert.Equal(new DateTime(2026, 6, 5, 10, 0, 0), occurrence);
    }

    [Fact]
    public void OneTimeEvent_BeforeReminderWindowIsNotDue()
    {
        var calendarEvent = new CalendarEvent
        {
            Date = new DateTime(2026, 7, 20),
            IsAllDay = false,
            StartTime = TimeSpan.FromHours(12),
            ReminderAdvanceMinutes = 15
        };

        var occurrence = CalendarOccurrenceCalculator.GetDueOccurrence(
            calendarEvent,
            new DateTime(2026, 7, 20, 11, 44, 59));

        Assert.Null(occurrence);
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(12, false)]
    public void ContinuousEvent_OccursAcrossInclusiveDateSpan(int day, bool expected)
    {
        var calendarEvent = new CalendarEvent
        {
            Date = new DateTime(2026, 8, 9),
            EndDate = new DateTime(2026, 8, 11)
        };

        Assert.Equal(
            expected,
            CalendarOccurrenceCalculator.OccursOnDate(calendarEvent, new DateTime(2026, 8, day)));
    }

    [Fact]
    public void TimedOccurrence_EndsAtConfiguredEndTime()
    {
        var calendarEvent = new CalendarEvent
        {
            Date = new DateTime(2026, 8, 9),
            EndDate = new DateTime(2026, 8, 9),
            IsAllDay = false,
            StartTime = TimeSpan.FromHours(10),
            EndTime = TimeSpan.FromHours(11)
        };
        var occurrence = new DateTime(2026, 8, 9, 10, 0, 0);

        Assert.False(CalendarOccurrenceCalculator.HasOccurrenceEnded(
            calendarEvent,
            occurrence,
            new DateTime(2026, 8, 9, 10, 59, 59)));
        Assert.True(CalendarOccurrenceCalculator.HasOccurrenceEnded(
            calendarEvent,
            occurrence,
            new DateTime(2026, 8, 9, 11, 0, 0)));
    }

    [Fact]
    public void MultiDayAllDayOccurrence_EndsAfterFinalDay()
    {
        var calendarEvent = new CalendarEvent
        {
            Date = new DateTime(2026, 8, 9),
            EndDate = new DateTime(2026, 8, 11),
            IsAllDay = true
        };

        Assert.Equal(
            new DateTime(2026, 8, 12),
            CalendarOccurrenceCalculator.GetOccurrenceEnd(calendarEvent, calendarEvent.Date));
    }

    [Fact]
    public void WeeklyEvent_SkipsExpiredOccurrence()
    {
        var calendarEvent = CreateWeeklyEvent();
        calendarEvent.ExpiredOccurrences.Add(new DateTime(2026, 6, 1, 10, 0, 0));

        var occurrence = CalendarOccurrenceCalculator.GetDueOccurrence(
            calendarEvent,
            new DateTime(2026, 6, 5, 9, 0, 0));

        Assert.Equal(new DateTime(2026, 6, 5, 10, 0, 0), occurrence);
    }

    [Fact]
    public void WeeklyEvent_ExcludedOccurrenceIsHiddenAndSkippedByReminder()
    {
        var calendarEvent = CreateWeeklyEvent();
        calendarEvent.ExcludedOccurrenceDates.Add(new DateTime(2026, 6, 1));

        var occurrence = CalendarOccurrenceCalculator.GetDueOccurrence(
            calendarEvent,
            new DateTime(2026, 6, 5, 9, 0, 0));

        Assert.False(CalendarOccurrenceCalculator.OccursOnDate(
            calendarEvent,
            new DateTime(2026, 6, 1)));
        Assert.True(CalendarOccurrenceCalculator.OccursOnDate(
            calendarEvent,
            new DateTime(2026, 6, 5)));
        Assert.Equal(new DateTime(2026, 6, 5, 10, 0, 0), occurrence);
    }

    private static CalendarEvent CreateWeeklyEvent() => new()
    {
        Date = new DateTime(2026, 6, 1),
        IsAllDay = false,
        StartTime = TimeSpan.FromHours(10),
        ReminderAdvanceMinutes = 120,
        Recurrence = CalendarEventRecurrence.Weekly,
        RecurringDays = [DayOfWeek.Monday, DayOfWeek.Friday]
    };
}
