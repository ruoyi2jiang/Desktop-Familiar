using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class FocusModeManagerTests
{
    [Fact]
    public void FocusMode_PersistsAndTracksReminderVisibilityTransitions()
    {
        var repository = new MemoryCompanionRepository(CreateData());
        var manager = new CompanionManager(repository);
        var focusChangeCount = 0;
        manager.FocusModeChanged += (_, _) => focusChangeCount++;

        manager.SetFocusMode(true);

        Assert.True(manager.Data.Settings.FocusModeEnabled);
        Assert.True(repository.Load().Settings.FocusModeEnabled);
        Assert.False(manager.ShouldDisplayCompanion);
        Assert.Equal(1, focusChangeCount);

        manager.SetActivityState(CompanionActivityState.Reminder);
        Assert.True(manager.ShouldDisplayCompanion);

        manager.SetActivityState(CompanionActivityState.Idle);
        Assert.False(manager.ShouldDisplayCompanion);

        manager.SetFocusMode(false);
        Assert.True(manager.ShouldDisplayCompanion);
        Assert.Equal(2, focusChangeCount);
    }

    [Fact]
    public void FocusMode_SaveFailureRestoresStateAndDoesNotPublishChange()
    {
        var manager = new CompanionManager(new FailingCompanionRepository(CreateData()));
        var focusChangeCount = 0;
        manager.FocusModeChanged += (_, _) => focusChangeCount++;

        Assert.Throws<AppPersistenceException>(() => manager.SetFocusMode(true));

        Assert.False(manager.Data.Settings.FocusModeEnabled);
        Assert.Equal(0, focusChangeCount);
    }

    private static DesktopCompanionData CreateData() => new()
    {
        Settings = new AppSettings(),
        Companions = [new CompanionDefinition { Name = "Test companion" }]
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
