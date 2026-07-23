using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Calendar;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Services.Time;

namespace DesktopFamiliar.Tests;

public sealed class CalendarReminderServiceTests
{
    [Fact]
    public void FocusModeReminder_EntersReminderStateAndAppliesVisibilityBeforePresentation()
    {
        var calendarEvent = CreateTimedEvent();
        var manager = new CompanionManager(new MemoryCompanionRepository(new DesktopCompanionData
        {
            Settings = new AppSettings { FocusModeEnabled = true },
            Companions = [new CompanionDefinition { Name = "Test companion" }],
            CalendarEvents = [calendarEvent]
        }));
        var calls = new List<string>();
        var coordinator = new CalendarReminderPresentationCoordinator(
            manager,
            () =>
            {
                calls.Add(manager.ShouldDisplayCompanion ? "visibility:shown" : "visibility:hidden");
            },
            (presentedEvent, occurrenceStart) =>
            {
                Assert.Equal(CompanionActivityState.Reminder, manager.ActivityState);
                Assert.Same(calendarEvent, presentedEvent);
                Assert.Equal(new DateTime(2026, 7, 20, 12, 0, 0), occurrenceStart);
                calls.Add("reminder");
            },
            () => calls.Add("clear"));

        coordinator.Present(new CalendarReminderEventArgs(
            calendarEvent,
            new DateTime(2026, 7, 20, 12, 0, 0)));

        Assert.Equal(CompanionActivityState.Reminder, manager.ActivityState);
        Assert.True(manager.ShouldDisplayCompanion);
        Assert.Equal(["visibility:shown", "reminder"], calls);

        coordinator.End(new CalendarReminderEndedEventArgs(
            CalendarReminderEndReason.Acknowledged,
            wasPresenting: true));

        Assert.Equal(CompanionActivityState.Idle, manager.ActivityState);
        Assert.False(manager.ShouldDisplayCompanion);
        Assert.Equal(["visibility:shown", "reminder", "clear", "visibility:hidden"], calls);
    }

    [Fact]
    public void FocusModeReminder_PresentationFailureReturnsToHiddenIdleState()
    {
        var manager = new CompanionManager(new MemoryCompanionRepository(new DesktopCompanionData
        {
            Settings = new AppSettings { FocusModeEnabled = true },
            Companions = [new CompanionDefinition { Name = "Test companion" }]
        }));
        var visibilityStates = new List<bool>();
        var coordinator = new CalendarReminderPresentationCoordinator(
            manager,
            () => visibilityStates.Add(manager.ShouldDisplayCompanion),
            (_, _) => throw new InvalidOperationException("Simulated overlay failure."),
            () => { });

        Assert.Throws<InvalidOperationException>(() => coordinator.Present(
            new CalendarReminderEventArgs(CreateTimedEvent(), new DateTime(2026, 7, 20, 12, 0, 0))));

        Assert.Equal(CompanionActivityState.Idle, manager.ActivityState);
        Assert.False(manager.ShouldDisplayCompanion);
        Assert.Equal([true, false], visibilityStates);
    }

    [Fact]
    public void SnoozedReminder_RaisesAgainThenExpiresAtEventEnd()
    {
        var calendarEvent = CreateTimedEvent();
        var clock = new ManualClock(new DateTime(2026, 7, 20, 11, 45, 0));
        var manager = CreateManager(calendarEvent);
        using var service = new CalendarReminderService(manager, clock);
        var raised = new List<CalendarReminderEventArgs>();
        var ended = new List<CalendarReminderEndedEventArgs>();
        service.ReminderRaised += (_, args) => raised.Add(args);
        service.ReminderEnded += (_, args) => ended.Add(args);

        service.CheckNow();
        service.SnoozeActiveReminder();
        clock.Now = new DateTime(2026, 7, 20, 11, 54, 59);
        service.CheckNow();
        clock.Now = new DateTime(2026, 7, 20, 11, 55, 0);
        service.CheckNow();
        clock.Now = new DateTime(2026, 7, 20, 13, 0, 0);
        service.CheckNow();

        Assert.Equal(2, raised.Count);
        Assert.Equal(new DateTime(2026, 7, 20, 12, 0, 0), raised[0].OccurrenceStart);
        Assert.Equal(
            [CalendarReminderEndReason.Snoozed, CalendarReminderEndReason.Expired],
            ended.Select(args => args.Reason));
        Assert.True(manager.Data.CalendarEvents[0].IsExpired);
        Assert.False(manager.Data.CalendarEvents[0].IsCompleted);
        Assert.Null(service.ActiveReminder);
    }

    [Fact]
    public void AcknowledgedReminder_IsCompletedAndCleared()
    {
        var calendarEvent = CreateTimedEvent();
        var clock = new ManualClock(new DateTime(2026, 7, 20, 11, 45, 0));
        var manager = CreateManager(calendarEvent);
        using var service = new CalendarReminderService(manager, clock);
        CalendarReminderEndedEventArgs? ended = null;
        service.ReminderEnded += (_, args) => ended = args;

        service.CheckNow();
        service.AcknowledgeActiveReminder();

        Assert.Equal(CalendarReminderEndReason.Acknowledged, ended?.Reason);
        Assert.True(ended?.WasPresenting);
        Assert.True(manager.Data.CalendarEvents[0].IsCompleted);
        Assert.False(manager.Data.CalendarEvents[0].IsExpired);
        Assert.Null(service.ActiveReminder);
    }

    [Fact]
    public void WeeklyReminder_PersistsCompletedAndExpiredOccurrenceHistory()
    {
        var firstOccurrence = new DateTime(2026, 7, 20, 12, 0, 0);
        var calendarEvent = CreateTimedEvent();
        calendarEvent.Recurrence = CalendarEventRecurrence.Weekly;
        calendarEvent.RecurringDays = [DayOfWeek.Monday];
        var clock = new ManualClock(firstOccurrence.AddMinutes(-15));
        var manager = CreateManager(calendarEvent);
        using var service = new CalendarReminderService(manager, clock);

        service.CheckNow();
        service.AcknowledgeActiveReminder();

        clock.Now = firstOccurrence.AddDays(7).AddMinutes(-15);
        service.CheckNow();
        clock.Now = firstOccurrence.AddDays(7).AddHours(1);
        service.CheckNow();

        var savedEvent = Assert.Single(manager.Data.CalendarEvents);
        Assert.Equal([firstOccurrence], savedEvent.CompletedOccurrences);
        Assert.Equal([firstOccurrence.AddDays(7)], savedEvent.ExpiredOccurrences);
        Assert.Equal(CalendarOccurrenceStatus.Completed,
            CalendarOccurrenceStatusTracker.Resolve(savedEvent, firstOccurrence));
        Assert.Equal(CalendarOccurrenceStatus.Expired,
            CalendarOccurrenceStatusTracker.Resolve(savedEvent, firstOccurrence.AddDays(7)));
    }

    [Fact]
    public void PersistedSnooze_IsRespectedAfterServiceRestart()
    {
        var calendarEvent = CreateTimedEvent();
        calendarEvent.SnoozedUntil = new DateTime(2026, 7, 20, 11, 55, 0);
        var clock = new ManualClock(new DateTime(2026, 7, 20, 11, 50, 0));
        var manager = CreateManager(calendarEvent);
        using var service = new CalendarReminderService(manager, clock);
        var raised = 0;
        service.ReminderRaised += (_, _) => raised++;

        service.CheckNow();
        Assert.Equal(0, raised);

        clock.Now = new DateTime(2026, 7, 20, 11, 55, 0);
        service.CheckNow();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void DailyCheckPrunesTerminalRemindersAtLeastOneYearOld()
    {
        var oldCompleted = CreateTimedEvent();
        oldCompleted.Date = new DateTime(2025, 7, 20);
        oldCompleted.EndDate = oldCompleted.Date;
        oldCompleted.IsCompleted = true;
        var current = CreateTimedEvent();
        var manager = new CompanionManager(new MemoryCompanionRepository(new DesktopCompanionData
        {
            CalendarEvents = [oldCompleted, current]
        }));
        using var service = new CalendarReminderService(
            manager,
            new ManualClock(new DateTime(2026, 7, 21, 9, 0, 0)));

        service.CheckNow();

        Assert.DoesNotContain(manager.Data.CalendarEvents, calendarEvent => calendarEvent.Id == oldCompleted.Id);
        Assert.Contains(manager.Data.CalendarEvents, calendarEvent => calendarEvent.Id == current.Id);
    }

    [Fact]
    public void PresentationFailure_IsRetriedOnNextCheck()
    {
        var calendarEvent = CreateTimedEvent();
        var clock = new ManualClock(new DateTime(2026, 7, 20, 11, 45, 0));
        var manager = CreateManager(calendarEvent);
        using var service = new CalendarReminderService(manager, clock);
        var attempts = 0;
        service.ReminderRaised += (_, _) =>
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("Simulated presentation failure.");
        };

        service.CheckNow();
        service.CheckNow();

        Assert.Equal(2, attempts);
        Assert.NotNull(service.ActiveReminder);
    }

    [Fact]
    public void DeletingActiveWeeklyOccurrenceEndsItAndKeepsFutureSeriesReminder()
    {
        var calendarEvent = new CalendarEvent
        {
            Title = "Stand-up",
            Date = new DateTime(2026, 7, 20),
            EndDate = new DateTime(2026, 7, 20),
            IsAllDay = false,
            StartTime = TimeSpan.FromHours(12),
            EndTime = TimeSpan.FromHours(13),
            ReminderAdvanceMinutes = 15,
            Recurrence = CalendarEventRecurrence.Weekly,
            RecurringDays = [DayOfWeek.Monday, DayOfWeek.Friday]
        };
        var clock = new ManualClock(new DateTime(2026, 7, 20, 11, 45, 0));
        var manager = CreateManager(calendarEvent);
        using var service = new CalendarReminderService(manager, clock);
        var raised = new List<DateTime>();
        CalendarReminderEndedEventArgs? ended = null;
        service.ReminderRaised += (_, args) => raised.Add(args.OccurrenceStart);
        service.ReminderEnded += (_, args) => ended = args;

        service.CheckNow();
        manager.DeleteCalendarOccurrence(calendarEvent.Id, new DateTime(2026, 7, 20));

        Assert.Equal(CalendarReminderEndReason.OccurrenceDeleted, ended?.Reason);
        Assert.True(ended?.WasPresenting);
        Assert.Null(service.ActiveReminder);
        Assert.Single(manager.Data.CalendarEvents);

        clock.Now = new DateTime(2026, 7, 24, 11, 45, 0);
        service.CheckNow();

        Assert.Equal(
            [new DateTime(2026, 7, 20, 12, 0, 0), new DateTime(2026, 7, 24, 12, 0, 0)],
            raised);
    }

    private static CalendarEvent CreateTimedEvent() => new()
    {
        Title = "Appointment",
        Date = new DateTime(2026, 7, 20),
        EndDate = new DateTime(2026, 7, 20),
        IsAllDay = false,
        StartTime = TimeSpan.FromHours(12),
        EndTime = TimeSpan.FromHours(13),
        ReminderAdvanceMinutes = 15,
        SnoozeMinutes = 10
    };

    private static CompanionManager CreateManager(CalendarEvent calendarEvent) =>
        new(new MemoryCompanionRepository(new DesktopCompanionData
        {
            CalendarEvents = [calendarEvent]
        }));

    private sealed class ManualClock(DateTime now) : IClock
    {
        public DateTime Now { get; set; } = now;
    }

    private sealed class MemoryCompanionRepository(DesktopCompanionData data) : ICompanionRepository
    {
        private DesktopCompanionData _data = data.Clone();

        public DesktopCompanionData Load() => _data.Clone();

        public void Save(DesktopCompanionData data) => _data = data.Clone();
    }
}
