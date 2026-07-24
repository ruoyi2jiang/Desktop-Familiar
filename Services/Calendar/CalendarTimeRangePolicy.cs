namespace DesktopFamiliar.Services.Calendar;

/// <summary>Owns valid end-time adjustment for timed calendar events.</summary>
public static class CalendarTimeRangePolicy
{
    public static TimeSpan EnsureEndAfterStart(TimeSpan startTime, TimeSpan endTime) =>
        endTime > startTime
            ? endTime
            : startTime.Add(TimeSpan.FromMinutes(30));
}
