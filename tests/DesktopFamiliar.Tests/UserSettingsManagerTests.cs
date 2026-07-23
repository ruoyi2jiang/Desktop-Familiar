using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class UserSettingsManagerTests
{
    [Fact]
    public void UserBirthdayIsNormalizedAndPersisted()
    {
        var repository = new MemoryCompanionRepository(new DesktopCompanionData());
        var manager = new CompanionManager(repository);
        var changed = 0;
        manager.UserBirthdayChanged += (_, _) => changed++;

        manager.SetUserBirthday(new DateTime(1995, 4, 12, 18, 30, 0));

        Assert.Equal(new DateTime(1995, 4, 12), manager.Data.Settings.UserBirthday);
        Assert.Equal(new DateTime(1995, 4, 12), repository.Load().Settings.UserBirthday);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void UserBirthdaySaveFailureRestoresPreviousValueAndDoesNotPublishChange()
    {
        var original = new DateTime(1990, 1, 2);
        var repository = new MemoryCompanionRepository(new DesktopCompanionData
        {
            Settings = new AppSettings { UserBirthday = original }
        });
        var manager = new CompanionManager(repository);
        var changed = false;
        manager.UserBirthdayChanged += (_, _) => changed = true;
        repository.FailSaves = true;

        Assert.Throws<AppPersistenceException>(() =>
            manager.SetUserBirthday(new DateTime(2000, 3, 4)));

        Assert.Equal(original, manager.Data.Settings.UserBirthday);
        Assert.False(changed);
    }

    private sealed class MemoryCompanionRepository(DesktopCompanionData data) : ICompanionRepository
    {
        private DesktopCompanionData _data = data.Clone();
        public bool FailSaves { get; set; }

        public DesktopCompanionData Load() => _data.Clone();

        public void Save(DesktopCompanionData data)
        {
            if (FailSaves)
                throw new IOException("Simulated save failure.");
            _data = data.Clone();
        }
    }
}
