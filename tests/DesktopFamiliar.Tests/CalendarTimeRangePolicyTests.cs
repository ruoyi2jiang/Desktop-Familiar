using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Tests;

public sealed class CalendarTimeRangePolicyTests
{
    [Fact]
    public void NewEvent_DefaultsToTimed()
    {
        Assert.False(new CalendarEvent().IsAllDay);
    }

    [Fact]
    public void EndBeforeStart_ShiftsToThirtyMinutesAfterStart()
    {
        var endTime = CalendarTimeRangePolicy.EnsureEndAfterStart(
            TimeSpan.FromHours(14),
            TimeSpan.FromHours(13));

        Assert.Equal(TimeSpan.FromHours(14.5), endTime);
    }

    [Fact]
    public void LastHalfHourStart_UsesMidnightAsTheNextDayEnd()
    {
        var endTime = CalendarTimeRangePolicy.EnsureEndAfterStart(
            TimeSpan.FromHours(23.5),
            TimeSpan.FromHours(22));

        Assert.Equal(TimeSpan.FromDays(1), endTime);
    }

    [Fact]
    public void ExistingLaterEndTime_IsRetained()
    {
        var endTime = CalendarTimeRangePolicy.EnsureEndAfterStart(
            TimeSpan.FromHours(9),
            TimeSpan.FromHours(10));

        Assert.Equal(TimeSpan.FromHours(10), endTime);
    }
}
