using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public void JsonStore_RoundTripsConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonAppDataStore(directory.Path, CreateOptions());
        var data = new DesktopCompanionData();
        data.Settings.ViewportWidth = 321;
        data.Settings.FocusModeEnabled = true;
        data.Settings.UserBirthday = new DateTime(1995, 4, 12);
        var reminderId = Guid.NewGuid();
        var selfBirthdayId = Guid.NewGuid();
        data.Companions.Add(new CompanionDefinition
        {
            Name = "Test companion",
            Animations =
            [
                new AnimationScene { Id = reminderId },
                new AnimationScene { Id = selfBirthdayId }
            ],
            ReminderAnimationId = reminderId,
            SelfBirthdayAnimation1Id = selfBirthdayId
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Title = "Dentist",
            Recurrence = CalendarEventRecurrence.Weekly,
            CompletedOccurrences = [new DateTime(2026, 8, 5, 10, 0, 0)],
            ExpiredOccurrences = [new DateTime(2026, 8, 19, 10, 0, 0)],
            ExcludedOccurrenceDates = [new DateTime(2026, 8, 12)]
        });

        store.Write(data);
        var loaded = store.Read();

        Assert.Equal(321, loaded.Settings.ViewportWidth);
        Assert.True(loaded.Settings.FocusModeEnabled);
        Assert.Equal(new DateTime(1995, 4, 12), loaded.Settings.UserBirthday);
        var loadedCompanion = Assert.Single(loaded.Companions);
        Assert.Equal(reminderId, loadedCompanion.ReminderAnimationId);
        Assert.Equal(selfBirthdayId, loadedCompanion.SelfBirthdayAnimation1Id);
        var loadedEvent = Assert.Single(loaded.CalendarEvents);
        Assert.Equal("Dentist", loadedEvent.Title);
        Assert.Equal([new DateTime(2026, 8, 5, 10, 0, 0)], loadedEvent.CompletedOccurrences);
        Assert.Equal([new DateTime(2026, 8, 19, 10, 0, 0)], loadedEvent.ExpiredOccurrences);
        Assert.Equal([new DateTime(2026, 8, 12)], loadedEvent.ExcludedOccurrenceDates);
    }

    [Fact]
    public void JsonStore_FailedReplacementLeavesExistingDocumentReadable()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonAppDataStore(directory.Path, CreateOptions());
        var original = new DesktopCompanionData();
        original.Settings.ViewportWidth = 300;
        store.Write(original);

        var updated = original.Clone();
        updated.Settings.ViewportWidth = 400;
        store.Write(updated);

        Assert.Equal(400, store.Read().Settings.ViewportWidth);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void RestoreFrom_UsesDeepSnapshot()
    {
        var data = new DesktopCompanionData();
        data.Companions.Add(new CompanionDefinition { Name = "Before" });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Recurrence = CalendarEventRecurrence.Weekly,
            CompletedOccurrences = [new DateTime(2026, 7, 20)]
        });
        var snapshot = data.Clone();

        data.Companions[0].Name = "After";
        data.CalendarEvents[0].CompletedOccurrences.Add(new DateTime(2026, 7, 27));
        data.RestoreFrom(snapshot);
        snapshot.Companions[0].Name = "Snapshot changed";
        snapshot.CalendarEvents[0].CompletedOccurrences.Clear();

        Assert.Equal("Before", data.Companions[0].Name);
        Assert.Equal([new DateTime(2026, 7, 20)], data.CalendarEvents[0].CompletedOccurrences);
    }

    [Fact]
    public void Normalizer_ClampsCurrentValuesAndFrameIndexes()
    {
        var data = new DesktopCompanionData
        {
            SchemaVersion = 99,
            Settings = new AppSettings { ViewportWidth = -10, ViewportHeight = 99999 },
            Companions =
            [
                new CompanionDefinition
                {
                    Animations =
                    [
                        new AnimationScene
                        {
                            PngFrames = [new PngAnimationFrame(), new PngAnimationFrame(), new PngAnimationFrame()],
                            FrameLoopMode = AnimationFrameLoopMode.SelectedRange,
                            LoopStartFrameIndex = 1,
                            LoopEndFrameIndex = 9,
                            DialogStartFrameIndex = 8
                        }
                    ]
                }
            ]
        };

        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal(AppSettings.MinimumViewportSize, data.Settings.ViewportWidth);
        Assert.Equal(AppSettings.MaximumViewportSize, data.Settings.ViewportHeight);
        var scene = data.Companions[0].Animations[0];
        Assert.Equal(1, scene.LoopStartFrameIndex);
        Assert.Equal(2, scene.LoopEndFrameIndex);
        Assert.Equal(2, scene.DialogStartFrameIndex);
    }

    [Fact]
    public void Normalizer_ClearsIndividualFrameSettingsForNonPngSource()
    {
        var scene = new AnimationScene
        {
            AnimationPath = "animation.gif",
            FrameLoopMode = AnimationFrameLoopMode.SelectedRange,
            LoopStartFrameIndex = 2,
            LoopEndFrameIndex = 4,
            DialogStartFrameIndex = 3
        };
        var data = new DesktopCompanionData
        {
            Companions = [new CompanionDefinition { Animations = [scene] }]
        };

        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal(AnimationFrameLoopMode.EntireAnimation, scene.FrameLoopMode);
        Assert.Equal(0, scene.LoopStartFrameIndex);
        Assert.Equal(0, scene.LoopEndFrameIndex);
        Assert.Equal(0, scene.DialogStartFrameIndex);
    }

    [Fact]
    public void Normalizer_RepairsAndOrdersRecurringOccurrenceCollections()
    {
        var firstDate = new DateTime(2026, 7, 6);
        var later = new DateTime(2026, 7, 13, 12, 0, 0);
        var earlierTimeSameDate = new DateTime(2026, 7, 13, 8, 0, 0);
        var calendarEvent = new CalendarEvent
        {
            Date = firstDate,
            Recurrence = CalendarEventRecurrence.Weekly,
            CompletedOccurrences = [later, earlierTimeSameDate, firstDate.AddDays(-7)],
            ExpiredOccurrences = [later, firstDate.AddDays(14)]
        };
        var data = new DesktopCompanionData { CalendarEvents = [calendarEvent] };

        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal([later], calendarEvent.CompletedOccurrences);
        Assert.Equal([firstDate.AddDays(14)], calendarEvent.ExpiredOccurrences);
    }

    [Fact]
    public void Normalizer_AddsBuiltInThemesWithoutReplacingSelectedCustomTheme()
    {
        var customId = Guid.NewGuid();
        var customPalette = ThemeService.CreateEditablePalette(AppTheme.SeaBlue);
        customPalette.PageBackground = "#FF123456";
        var data = new DesktopCompanionData
        {
            Settings = new AppSettings
            {
                Theme = AppTheme.Custom,
                SelectedThemeId = customId,
                SavedThemes = [new SavedTheme { Id = customId, Name = "Mine", Palette = customPalette }]
            }
        };

        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal(customId, data.Settings.SelectedThemeId);
        Assert.Equal("#FF123456", data.Settings.SavedThemes.Single(theme => theme.Id == customId).Palette.PageBackground);
        Assert.Contains(data.Settings.SavedThemes, theme => theme.IsBuiltIn && theme.Name == LocalizationService.Get("Settings.Theme.Crimson"));
        Assert.Contains(data.Settings.SavedThemes, theme => theme.IsBuiltIn && theme.Name == LocalizationService.Get("Settings.Theme.SeaBlue"));
        Assert.Contains(data.Settings.SavedThemes, theme => theme.IsBuiltIn && theme.Name == LocalizationService.Get("Settings.Theme.StellaDark"));
    }

    [Fact]
    public void Normalizer_CreatesCustomThemeWhenCustomSelectionIsMissing()
    {
        var data = new DesktopCompanionData
        {
            Settings = new AppSettings
            {
                Theme = AppTheme.Custom,
                SelectedThemeId = Guid.NewGuid(),
                CustomTheme = ThemeService.CreateEditablePalette(AppTheme.StellaDark)
            }
        };

        new CompanionDataNormalizer().Normalize(data);

        var selected = data.Settings.SavedThemes.Single(theme => theme.Id == data.Settings.SelectedThemeId);
        Assert.False(selected.IsBuiltIn);
        Assert.Equal(data.Settings.CustomTheme.PageBackground, selected.Palette.PageBackground);
    }

    [Fact]
    public void ConfigurationWithoutFocusMode_DefaultsToDisabled()
    {
        var data = JsonSerializer.Deserialize<DesktopCompanionData>(
            """{"SchemaVersion":1,"Settings":{}}""",
            CreateOptions());

        Assert.NotNull(data);
        new CompanionDataNormalizer().Normalize(data);

        Assert.False(data.Settings.FocusModeEnabled);
    }

    [Fact]
    public void Schema1Configuration_MigratesAutoClickSettings()
    {
        var data = JsonSerializer.Deserialize<DesktopCompanionData>(
            """{"SchemaVersion":1,"Settings":{}}""",
            CreateOptions());

        Assert.NotNull(data);
        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal(2, data.SchemaVersion);
        Assert.Equal(AppSettings.DefaultAutoClickIntervalMilliseconds, data.Settings.AutoClickIntervalMilliseconds);
        Assert.Equal(AppSettings.DefaultAutoClickHotkey, data.Settings.AutoClickHotkey);
    }

    [Fact]
    public void FirstLoad_CreatesNormalizedEmptyConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);

        var loaded = storage.Load();

        Assert.Empty(loaded.Companions);
        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.NotEmpty(loaded.Settings.SavedThemes);
        Assert.True(File.Exists(storage.DataFilePath));
    }

    private static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DesktopFamiliar.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
