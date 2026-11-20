using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Services.Time;

namespace DesktopFamiliar.Tests;

public sealed class PersistenceTests
{
    private sealed class TestClock(DateTime now) : IClock
    {
        public DateTime Now { get; set; } = now;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstUseRespectsLegacyAndPreviouslyOpenedUnsavedLibraries(bool hasLegacyLibrary)
    {
        using var directory = new TemporaryDirectory();
        if (hasLegacyLibrary)
            File.WriteAllText(Path.Combine(directory.Path, "desktop-familiar.json"), """{"SchemaVersion":8}""");
        var first = new CompanionStorage(directory.Path);
        Assert.Equal(!hasLegacyLibrary, first.IsFirstUse);
        var reopened = new CompanionStorage(directory.Path);
        Assert.False(reopened.IsFirstUse);
        Assert.False(File.Exists(reopened.DataFilePath));
    }

    [Fact]
    public void ObservedWeeklyExpirationSurvivesRestartBeyondReminderLookbackWithoutRewritingTheLibrary()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        var occurrence = new DateTime(2026, 7, 20, 12, 0, 0);
        var calendarEvent = new CalendarEvent
        {
            Date = occurrence.Date, Recurrence = CalendarEventRecurrence.Weekly,
            RecurringDays = [occurrence.DayOfWeek], StartTime = occurrence.TimeOfDay,
            EndTime = TimeSpan.FromHours(13)
        };
        storage.Save(new DesktopCompanionData { CalendarEvents = [calendarEvent] });
        var before = File.ReadAllBytes(storage.DataFilePath);
        var clock = new TestClock(occurrence);
        var manager = new CompanionManager(storage);
        using (var reminders = new CalendarReminderService(manager, clock))
        {
            reminders.CheckNow();
            clock.Now = occurrence.AddHours(1);
            reminders.CheckNow();
        }

        clock.Now = occurrence.AddDays(30);
        var reopened = new CompanionManager(new CompanionStorage(directory.Path));
        using var restarted = new CalendarReminderService(reopened, clock);
        restarted.CheckNow();
        var expired = Assert.Single(reopened.Data.CalendarEvents).ExpiredOccurrences;
        Assert.Contains(occurrence, expired);
        Assert.DoesNotContain(occurrence.AddDays(7), expired);
        Assert.Equal(before, File.ReadAllBytes(storage.DataFilePath));

        // An intentional schedule replacement must not inherit the previous file's observations.
        calendarEvent.ReminderEnabled = false;
        storage.Save(new DesktopCompanionData { CalendarEvents = [calendarEvent] });
        Assert.Empty(Assert.Single(storage.Load().CalendarEvents).ExpiredOccurrences);
    }

    [Fact]
    public void OpeningAndReminderMaintenanceLeaveLegacyLibraryUntouchedUntilAUserSave()
    {
        using var directory = new TemporaryDirectory();
        var legacy = Path.Combine(directory.Path, "desktop-familiar.json");
        const string original = """{"SchemaVersion":8, "Companions":[{"Name":"Pip"}],"CalendarEvents":[{"Title":"Past","Date":"2000-01-01","IsCompleted":true}]}""";
        File.WriteAllText(legacy, original);
        var storage = new CompanionStorage(directory.Path);
        var manager = new CompanionManager(storage);
        manager.Data.Settings.DialogBubbleHidden = true;
        manager.SetDialogBubbleHidden(false, automatic: true);
        using var reminders = new CalendarReminderService(manager);
        reminders.CheckNow();
        Assert.Empty(manager.Data.CalendarEvents);
        Assert.False(File.Exists(storage.DataFilePath));
        Assert.Equal(original, File.ReadAllText(legacy));

        manager.Data.Companions[0].Description = "Saved in this version";
        manager.Persist();
        Assert.Equal(original, File.ReadAllText(legacy));
        var protectedBytes = File.ReadAllBytes(storage.DataFilePath);
        File.WriteAllText(legacy, """{"SchemaVersion":8,"Companions":[]}""");
        var reopened = Assert.Single(new CompanionStorage(directory.Path).Load().Companions);
        Assert.Equal("Saved in this version", reopened.Description);
        Assert.Equal(protectedBytes, File.ReadAllBytes(storage.DataFilePath));
    }

    [Fact]
    public void FutureLibraryDisplaysKnownFieldsAndAnExplicitSaveCreatesAnIndependentCopy()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        var futurePath = Path.Combine(Path.GetDirectoryName(storage.DataFilePath)!, "desktop-familiar.v99.json");
        const string original = """
            {"SchemaVersion":99,"FutureSettings":{"a":12},"Companions":[
                {"Name":"Pip","Appearance":"Hologram","Personality":"Lively",
                 "ChatExpressions":{"happy":"6f414e5c-fafd-4f68-9291-a569695bfb83","sad":null,"angry":{"future":true}},
                 "Animations":[{"Name":"Wave","Id":"6f414e5c-fafd-4f68-9291-a569695bfb83","DialogLines":[{"Text":"Hello","StartSeconds":{"tick":8}}]}]},
                {"Name":"Other","Model3D":{"UnknownRig":"B"}}],"PendingCompanionCleanup":["3a6156c8-a581-4891-bd68-0000faeafb1a"]}
            """;
        File.WriteAllText(futurePath, original);
        var data = storage.Load();
        Assert.Null(storage.LastRecoveryNotice);
        Assert.Equal(2, data.Companions.Count);
        Assert.Equal("Lively", data.Companions[0].Personality);
        Assert.Equal(CompanionAppearance.Images2D, data.Companions[0].Appearance);
        Assert.Equal("Hello", Assert.Single(Assert.Single(data.Companions[0].Animations).DialogLines).Text);
        Assert.Empty(data.PendingCompanionCleanup);
        Assert.False(File.Exists(storage.DataFilePath));
        data.Companions[0].Name = "Pip edited";
        storage.Save(data);
        Assert.Equal(original, File.ReadAllText(futurePath));
        Assert.Equal("Pip edited", storage.Load().Companions[0].Name);
        var pet = storage.Load().Companions[0];
        Assert.Equal(Assert.Single(pet.Animations).Id, pet.ChatExpressions["happy"]);
        Assert.Null(pet.ChatExpressions["sad"]);
        Assert.False(pet.ChatExpressions.ContainsKey("angry"));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void RetiredDialogueTruncationIsIgnoredOnReadAndOmittedOnSave(int version)
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        var source = Path.Combine(Path.GetDirectoryName(storage.DataFilePath)!, $"desktop-familiar.v{version}.json");
        var original = JsonSerializer.Serialize(new
        {
            SchemaVersion = version,
            Settings = new { TruncateLongDialogLines = true, DialogBubbleHidden = true, ViewportWidth = 360 }
        });
        File.WriteAllText(source, original);
        var manager = new CompanionManager(storage);
        Assert.Equal(original, File.ReadAllText(source));
        Assert.Null(storage.LastRecoveryNotice);
        Assert.True(manager.Data.Settings.DialogBubbleHidden);
        manager.SetViewportSize(361, 240);

        using var saved = JsonDocument.Parse(File.ReadAllBytes(storage.DataFilePath));
        var settings = saved.RootElement.GetProperty("Settings");
        Assert.False(settings.TryGetProperty("TruncateLongDialogLines", out _));
        Assert.True(settings.GetProperty("DialogBubbleHidden").GetBoolean());
        Assert.Equal(361, settings.GetProperty("ViewportWidth").GetInt32());
        if (source != storage.DataFilePath) Assert.Equal(original, File.ReadAllText(source));
    }

    [Fact]
    public void MigrationKeepsRecognizedFieldsAlreadyPresentInAnOlderDocument()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        var legacy = Path.Combine(directory.Path, "desktop-familiar.json");
        const string input = """
            {"SchemaVersion":8,"Settings":{"Chat":{"Enabled":true,"ModelId":"my-model"}},
             "Companions":[{"Name":"Pip","Appearance":"Model3D","ProfilePicturePath":"pip.png","ThinkingPhrase":"Hmm",
             "Model3D":{"FilePath":"pip.glb","Animations":[{"Name":"Bow","ModelClip":"Bow"}]}}]}
            """;
        File.WriteAllText(legacy, input);
        var loaded = storage.Load();
        var pet = Assert.Single(loaded.Companions);
        Assert.Equal(CompanionAppearance.Model3D, pet.Appearance);
        Assert.Equal("pip.png", pet.ProfilePicturePath);
        Assert.Equal("Hmm", pet.ThinkingPhrase);
        Assert.Equal("Bow", Assert.Single(pet.Model3D.Animations).ModelClip);
        Assert.Equal("my-model", loaded.Settings.Chat.ModelId);
        Assert.Equal(input, File.ReadAllText(legacy));
    }

    [Fact]
    public void SavingCannotDowngradeANewerDocumentPlacedAtTheCurrentWritePath()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        const string original = """{"sChemAVersion":99,"Companions":[{"Name":"Pip","Future":"keep"}]}""";
        File.WriteAllText(storage.DataFilePath, original);
        var data = storage.Load();
        Assert.Equal("Pip", Assert.Single(data.Companions).Name);
        Assert.Null(storage.LastRecoveryNotice);
        Assert.Throws<NewerLibraryVersionException>(() => storage.Save(data));
        Assert.Equal(original, File.ReadAllText(storage.DataFilePath));
    }

    [Fact]
    public void UnreadableLibraryIsUntouchedOnOpenAndArchivedOnExplicitReplacement()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        const string original = """{"SchemaVersion":14,broken""";
        File.WriteAllText(storage.DataFilePath, original);
        var data = storage.Load();
        Assert.Equal(storage.DataFilePath, storage.LastRecoveryNotice!.FilePath);
        Assert.Equal(original, File.ReadAllText(storage.DataFilePath));
        Assert.Single(Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        storage.Save(data);
        var backup = Assert.Single(Directory.GetFiles(directory.Path, "*.invalid-*.json", SearchOption.AllDirectories));
        Assert.Equal(original, File.ReadAllText(backup));
        storage.Load();
        Assert.Null(storage.LastRecoveryNotice);
    }

    [Fact]
    public void JsonStore_RoundTripsConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonAppDataStore(directory.Path, CreateOptions());
        var data = new DesktopCompanionData();
        data.Settings.ViewportWidth = 321;
        data.Settings.Chat = new ChatPreferences(true, "gemma-3-1b-it-q4", "Alt+Shift+M");
        data.Settings.FocusModeEnabled = true;
        data.Settings.UserBirthday = new DateTime(1995, 4, 12);
        var reminderId = Guid.NewGuid();
        var selfBirthdayId = Guid.NewGuid();
        var dragLeftId = Guid.NewGuid();
        data.Companions.Add(new CompanionDefinition
        {
            Name = "Test companion",
            Description = "A pocket dragon.",
            Personality = "Sleepy.\nProtective of keyboards.",
            Animations =
            [
                new AnimationScene { Id = reminderId },
                new AnimationScene { Id = selfBirthdayId },
                new AnimationScene { Id = dragLeftId }
            ],
            ReminderAnimationId = reminderId,
            SelfBirthdayAnimation1Id = selfBirthdayId,
            InteractionStates = new CompanionInteractionStateAssignments
            {
                DragLeftAnimationId = dragLeftId
            }
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
        Assert.Equal(data.Settings.Chat, loaded.Settings.Chat);
        Assert.True(loaded.Settings.FocusModeEnabled);
        Assert.Equal(new DateTime(1995, 4, 12), loaded.Settings.UserBirthday);
        var loadedCompanion = Assert.Single(loaded.Companions);
        Assert.Equal("A pocket dragon.", loadedCompanion.Description);
        Assert.Equal("Sleepy.\nProtective of keyboards.", loadedCompanion.Personality);
        Assert.Equal(reminderId, loadedCompanion.ReminderAnimationId);
        Assert.Equal(selfBirthdayId, loadedCompanion.SelfBirthdayAnimation1Id);
        Assert.Equal(dragLeftId, loadedCompanion.InteractionStates.DragLeftAnimationId);
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
            SchemaVersion = DesktopCompanionData.CurrentSchemaVersion,
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
    public void Normalizer_RestoresBuiltInThemeWhenCustomEnumReferencesBuiltInSelection()
    {
        var seaBlueId = Guid.NewGuid();
        var data = new DesktopCompanionData
        {
            Settings = new AppSettings
            {
                Theme = AppTheme.Custom,
                SelectedThemeId = seaBlueId,
                SavedThemes =
                [
                    new SavedTheme
                    {
                        Id = seaBlueId,
                        IsBuiltIn = true,
                        Name = LocalizationService.Get("Settings.Theme.SeaBlue"),
                        Palette = ThemeService.CreateEditablePalette(AppTheme.SeaBlue)
                    }
                ]
            }
        };

        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal(AppTheme.SeaBlue, data.Settings.Theme);
        Assert.Equal(seaBlueId, data.Settings.SelectedThemeId);
        Assert.DoesNotContain(data.Settings.SavedThemes, theme => !theme.IsBuiltIn);
    }

    [Fact]
    public void Storage_RepairsBuiltInThemeInMemoryOnEachRead()
    {
        using var directory = new TemporaryDirectory();
        var seaBlueId = Guid.NewGuid();
        var storage = new CompanionStorage(directory.Path);
        storage.Save(new DesktopCompanionData
        {
            SchemaVersion = DesktopCompanionData.CurrentSchemaVersion,
            Settings = new AppSettings
            {
                Theme = AppTheme.Custom,
                SelectedThemeId = seaBlueId,
                SavedThemes =
                [
                    new SavedTheme
                    {
                        Id = seaBlueId,
                        IsBuiltIn = true,
                        Name = LocalizationService.Get("Settings.Theme.SeaBlue"),
                        Palette = ThemeService.CreateEditablePalette(AppTheme.SeaBlue)
                    }
                ]
            }
        });

        var original = File.ReadAllBytes(storage.DataFilePath);
        var firstLoad = storage.Load();
        var secondLoad = storage.Load();

        Assert.Equal(AppTheme.SeaBlue, firstLoad.Settings.Theme);
        Assert.Equal(seaBlueId, firstLoad.Settings.SelectedThemeId);
        Assert.Equal(AppTheme.SeaBlue, secondLoad.Settings.Theme);
        Assert.Equal(seaBlueId, secondLoad.Settings.SelectedThemeId);
        Assert.DoesNotContain(secondLoad.Settings.SavedThemes, theme => !theme.IsBuiltIn);
        Assert.Equal(original, File.ReadAllBytes(storage.DataFilePath));
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
    public void Schema1Configuration_MigratesToTheCurrentCoreSchema()
    {
        var data = JsonSerializer.Deserialize<DesktopCompanionData>(
            """{"SchemaVersion":1,"Settings":{}}""",
            CreateOptions());

        Assert.NotNull(data);
        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, data.SchemaVersion);
    }

    [Fact]
    public void Schema2Configuration_MigratesEmptyInteractionStates()
    {
        var data = JsonSerializer.Deserialize<DesktopCompanionData>(
            """{"SchemaVersion":2,"Settings":{},"Companions":[{}]}""",
            CreateOptions());

        Assert.NotNull(data);
        new CompanionDataNormalizer().Normalize(data);

        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, data.SchemaVersion);
        var interactionStates = Assert.Single(data.Companions).InteractionStates;
        Assert.NotNull(interactionStates);
        Assert.Null(interactionStates.DragLeftAnimationId);
        Assert.Empty(interactionStates.ClickAreas);
    }

    [Fact]
    public void Schema8ProfileMigration_PreservesDialogueAndWritesEmptyProfileFields()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        File.WriteAllText(storage.DataFilePath, """
            {"SchemaVersion":8,"Companions":[{"Name":"Pip","Animations":[
                {"DialogLines":[{"Text":"  Hello, {{user}}!  "},{"Text":" "}]},
                {"DialogLines":[{"Text":"Hello, {{user}}!"},{"Text":"Desk secure.","VoicePath":"clip.wav"}]}]}]}
            """);

        var data = storage.Load();
        var companion = Assert.Single(data.Companions);
        Assert.Equal("Pip", companion.Name);
        Assert.Equal(["Hello, {{user}}!", "Desk secure."], companion.GetDialogueExamples());
        Assert.Equal("  Hello, {{user}}!  ", companion.Animations[0].DialogLines[0].Text);
        using var original = JsonDocument.Parse(File.ReadAllText(storage.DataFilePath));
        Assert.Equal(8, original.RootElement.GetProperty("SchemaVersion").GetInt32());
        storage.Save(data);
        using var saved = JsonDocument.Parse(File.ReadAllText(storage.DataFilePath));
        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, saved.RootElement.GetProperty("SchemaVersion").GetInt32());
        var profile = saved.RootElement.GetProperty("Companions")[0];
        Assert.Equal("", profile.GetProperty("Description").GetString());
        Assert.Equal("", profile.GetProperty("Personality").GetString());
    }

    [Fact]
    public void UpgradingAnOlderLibraryRetainsAnyExistingProfileText()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        File.WriteAllText(storage.DataFilePath, """
            {"SchemaVersion":8,"Companions":[{"Description":"A librarian","Personality":"Inquisitive"}]}
            """);
        storage.Load();
        var pet = Assert.Single(storage.Load().Companions);
        Assert.Equal("A librarian", pet.Description);
        Assert.Equal("Inquisitive", pet.Personality);
    }

    [Fact]
    public void Normalizer_RemovesMissingInteractionStateAssignments()
    {
        var companion = new CompanionDefinition
        {
            InteractionStates = new CompanionInteractionStateAssignments
            {
                DragLeftAnimationId = Guid.NewGuid(),
                ClickAreas = [new CompanionClickArea { AnimationId = Guid.NewGuid() }]
            }
        };

        new CompanionDataNormalizer().Normalize(new DesktopCompanionData
        {
            Companions = [companion]
        });

        Assert.Null(companion.InteractionStates.DragLeftAnimationId);
        Assert.Null(Assert.Single(companion.InteractionStates.ClickAreas).AnimationId);
    }

    [Fact]
    public void CompanionAssetStore_DeletesOnlyTheSelectedCompanionResources()
    {
        using var directory = new TemporaryDirectory();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var firstAsset = Path.Combine(directory.Path, "Assets", first.ToString("N"), "Animation", "first.png");
        var secondAsset = Path.Combine(directory.Path, "Assets", second.ToString("N"), "Animation", "second.png");
        Directory.CreateDirectory(Path.GetDirectoryName(firstAsset)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondAsset)!);
        File.WriteAllText(firstAsset, "first");
        File.WriteAllText(secondAsset, "second");

        new CompanionAssetStore(directory.Path).DeleteCompanionResources(first);

        Assert.False(Directory.Exists(Path.GetDirectoryName(firstAsset)!));
        Assert.True(File.Exists(secondAsset));
    }

    [Fact]
    public void FirstLoad_LeavesNormalizedEmptyConfigurationInMemory()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);

        var loaded = storage.Load();

        Assert.Empty(loaded.Companions);
        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.NotEmpty(loaded.Settings.SavedThemes);
        Assert.False(File.Exists(storage.DataFilePath));
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
