namespace DesktopFamiliar.Services.Calendar;

/// <summary>Pure month-navigation limits for the calendar window.</summary>
public static class CalendarNavigationPolicy
{
    public const int FutureYearOptions = 20;

    public static DateTime GetEarliestMonth(DateTime today) =>
        FirstOfMonth(today).AddYears(-1);

    public static DateTime ClampMonth(DateTime requestedMonth, DateTime today)
    {
        var requested = FirstOfMonth(requestedMonth);
        var earliest = GetEarliestMonth(today);
        return requested < earliest ? earliest : requested;
    }

    public static IReadOnlyList<int> GetSelectableYears(
        DateTime today,
        DateTime displayedMonth,
        IEnumerable<DateTime> knownEventDates)
    {
        var earliestYear = GetEarliestMonth(today).Year;
        var latestKnownYear = knownEventDates
            .Select(date => date.Year)
            .DefaultIfEmpty(today.Year)
            .Max();
        var latestYear = Math.Max(
            Math.Max(today.Year + FutureYearOptions, displayedMonth.Year),
            latestKnownYear);
        return Enumerable.Range(earliestYear, latestYear - earliestYear + 1).ToList();
    }

    public static bool CanMoveToPreviousMonth(DateTime displayedMonth, DateTime today) =>
        FirstOfMonth(displayedMonth) > GetEarliestMonth(today);

    private static DateTime FirstOfMonth(DateTime value) => new(value.Year, value.Month, 1);
}
