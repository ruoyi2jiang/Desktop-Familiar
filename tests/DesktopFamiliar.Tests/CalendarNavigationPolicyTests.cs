using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Tests;

public sealed class CalendarNavigationPolicyTests
{
    private static readonly DateTime Today = new(2026, 7, 21);

    [Fact]
    public void CalendarCannotNavigateEarlierThanSameMonthOneYearAgo()
    {
        Assert.Equal(
            new DateTime(2025, 7, 1),
            CalendarNavigationPolicy.ClampMonth(new DateTime(2024, 1, 1), Today));
        Assert.False(CalendarNavigationPolicy.CanMoveToPreviousMonth(
            new DateTime(2025, 7, 1),
            Today));
        Assert.True(CalendarNavigationPolicy.CanMoveToPreviousMonth(
            new DateTime(2025, 8, 1),
            Today));
    }

    [Fact]
    public void SelectableYearsIncludeKnownFutureEventsAndEarliestAllowedYear()
    {
        var years = CalendarNavigationPolicy.GetSelectableYears(
            Today,
            new DateTime(2026, 7, 1),
            [new DateTime(2050, 1, 1)]);

        Assert.Equal(2025, years[0]);
        Assert.Contains(2050, years);
    }
}
