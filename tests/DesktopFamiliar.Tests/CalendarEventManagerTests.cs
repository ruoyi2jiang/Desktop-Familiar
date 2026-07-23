using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class CalendarEventManagerTests
{
    [Fact]
    public void DeleteOccurrence_PersistsExceptionWithoutDeletingSeries()
    {
        var repository = new MemoryCompanionRepository(CreateData());
        var manager = new CompanionManager(repository);
        var changed = 0;
        manager.CalendarEventsChanged += (_, _) => changed++;
        var eventId = manager.Data.CalendarEvents[0].Id;

        manager.DeleteCalendarOccurrence(eventId, new DateTime(2026, 8, 3, 14, 30, 0));

        var calendarEvent = Assert.Single(manager.Data.CalendarEvents);
        Assert.Equal(eventId, calendarEvent.Id);
        Assert.Equal([new DateTime(2026, 8, 3)], calendarEvent.ExcludedOccurrenceDates);
        Assert.Equal(
            [new DateTime(2026, 8, 3)],
            Assert.Single(repository.Load().CalendarEvents).ExcludedOccurrenceDates);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void DeleteOccurrence_SaveFailureRestoresSeriesAndDoesNotPublishChange()
    {
        var manager = new CompanionManager(new FailingCompanionRepository(CreateData()));
        var changed = 0;
        manager.CalendarEventsChanged += (_, _) => changed++;
        var eventId = manager.Data.CalendarEvents[0].Id;

        Assert.Throws<AppPersistenceException>(() =>
            manager.DeleteCalendarOccurrence(eventId, new DateTime(2026, 8, 3)));

        Assert.Empty(Assert.Single(manager.Data.CalendarEvents).ExcludedOccurrenceDates);
        Assert.Equal(0, changed);
    }

    private static DesktopCompanionData CreateData() => new()
    {
        CalendarEvents =
        [
            new CalendarEvent
            {
                Title = "Weekly review",
                Date = new DateTime(2026, 8, 3),
                EndDate = new DateTime(2026, 8, 3),
                Recurrence = CalendarEventRecurrence.Weekly,
                RecurringDays = [DayOfWeek.Monday]
            }
        ]
    };

    private sealed class MemoryCompanionRepository(DesktopCompanionData data) : ICompanionRepository
    {
        private DesktopCompanionData _data = data.Clone();

        public DesktopCompanionData Load() => _data.Clone();

        public void Save(DesktopCompanionData data) => _data = data.Clone();
    }

    private sealed class FailingCompanionRepository(DesktopCompanionData data) : ICompanionRepository
    {
        public DesktopCompanionData Load() => data.Clone();

        public void Save(DesktopCompanionData data) => throw new IOException("Simulated save failure.");
    }
}
