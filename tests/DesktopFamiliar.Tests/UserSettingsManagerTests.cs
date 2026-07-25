using DesktopFamiliar.Localization;
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

    [Fact]
    public void SelectingBuiltInSavedTheme_PersistsItsBuiltInThemeKind()
    {
        var seaBlue = new SavedTheme
        {
            Id = Guid.NewGuid(),
            IsBuiltIn = true,
            Name = LocalizationService.Get("Settings.Theme.SeaBlue"),
            Palette = ThemeService.CreateEditablePalette(AppTheme.SeaBlue)
        };
        var repository = new MemoryCompanionRepository(new DesktopCompanionData
        {
            Settings = new AppSettings
            {
                Theme = AppTheme.Crimson,
                SavedThemes = [seaBlue]
            }
        });
        var manager = new CompanionManager(repository);

        manager.SelectSavedTheme(seaBlue.Id);

        Assert.Equal(AppTheme.SeaBlue, manager.Data.Settings.Theme);
        Assert.Equal(seaBlue.Id, repository.Load().Settings.SelectedThemeId);
        Assert.Equal(AppTheme.SeaBlue, repository.Load().Settings.Theme);
    }

    [Fact]
    public void SavingBuiltInTheme_IsRejectedWithoutChangingTheStoredPalette()
    {
        var builtIn = new SavedTheme
        {
            Id = Guid.NewGuid(),
            IsBuiltIn = true,
            Name = LocalizationService.Get("Settings.Theme.Crimson"),
            Palette = ThemeService.CreateEditablePalette(AppTheme.Crimson)
        };
        var repository = new MemoryCompanionRepository(new DesktopCompanionData
        {
            Settings = new AppSettings { SavedThemes = [builtIn] }
        });
        var manager = new CompanionManager(repository);

        var replacement = builtIn.Clone();
        replacement.Name = "Edited";
        replacement.Palette.PageBackground = "#FF123456";

        Assert.Throws<InvalidOperationException>(() => manager.SaveSavedTheme(replacement));
        var persisted = repository.Load().Settings.SavedThemes.Single(theme => theme.Id == builtIn.Id);
        Assert.Equal(builtIn.Name, persisted.Name);
        Assert.Equal(builtIn.Palette.PageBackground, persisted.Palette.PageBackground);
    }

    [Fact]
    public void DeletingSelectedCustomTheme_SwitchesToTheReplacementThemeKind()
    {
        var builtIn = new SavedTheme
        {
            Id = Guid.NewGuid(),
            IsBuiltIn = true,
            Name = LocalizationService.Get("Settings.Theme.SeaBlue"),
            Palette = ThemeService.CreateEditablePalette(AppTheme.SeaBlue)
        };
        var custom = new SavedTheme
        {
            Id = Guid.NewGuid(),
            Name = "Mine",
            Palette = ThemeService.CreateEditablePalette(AppTheme.Crimson)
        };
        var repository = new MemoryCompanionRepository(new DesktopCompanionData
        {
            Settings = new AppSettings
            {
                Theme = AppTheme.Custom,
                SelectedThemeId = custom.Id,
                CustomTheme = custom.Palette.Clone(),
                SavedThemes = [builtIn, custom]
            }
        });
        var manager = new CompanionManager(repository);

        manager.DeleteSavedTheme(custom.Id);

        Assert.Equal(builtIn.Id, manager.Data.Settings.SelectedThemeId);
        Assert.Equal(AppTheme.SeaBlue, manager.Data.Settings.Theme);
        Assert.Equal(AppTheme.SeaBlue, repository.Load().Settings.Theme);
    }

    [Fact]
    public void SelectingSavedTheme_SaveFailureRestoresTheInMemorySelection()
    {
        var first = new SavedTheme { Id = Guid.NewGuid(), Name = "First" };
        var second = new SavedTheme { Id = Guid.NewGuid(), Name = "Second" };
        var repository = new MemoryCompanionRepository(new DesktopCompanionData
        {
            Settings = new AppSettings
            {
                Theme = AppTheme.Custom,
                SelectedThemeId = first.Id,
                CustomTheme = first.Palette.Clone(),
                SavedThemes = [first, second]
            }
        });
        var manager = new CompanionManager(repository);
        repository.FailSaves = true;

        Assert.Throws<AppPersistenceException>(() => manager.SelectSavedTheme(second.Id));

        Assert.Equal(first.Id, manager.Data.Settings.SelectedThemeId);
        Assert.Equal(first.Palette.PageBackground, manager.Data.Settings.CustomTheme.PageBackground);
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
