using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class CompanionBundleServiceTests
{
    [Fact]
    public void ExportThenImport_PreservesConfigurationAndCopiesEveryReferencedAsset()
    {
        using var directory = new TemporaryDirectory();
        var sourceRoot = Path.Combine(directory.Path, "Source");
        var dataRoot = Path.Combine(directory.Path, "Data");
        Directory.CreateDirectory(sourceRoot);
        var frame = WriteAsset(sourceRoot, "idle.png", [1, 2, 3]);
        var animation = WriteAsset(sourceRoot, "start.gif", [4, 5]);
        var voice = WriteAsset(sourceRoot, "voice.wav", [6, 7]);
        var font = WriteAsset(sourceRoot, "font.ttf", [8]);
        var bubble = WriteAsset(sourceRoot, "bubble.png", [9]);
        var idleId = Guid.NewGuid();
        var busyId = Guid.NewGuid();
        var companion = new CompanionDefinition
        {
            Name = "Bundle test",
            Description = "A dragon small enough to sleep on a key.",
            Personality = "Quiet, with a dry sense of humour.",
            ThinkingPhrase = "One moment, Keeper.",
            ImportedFontPath = font,
            BubbleImagePath = bubble,
            Animations =
            [
                new AnimationScene
                {
                    Id = idleId,
                    Name = "Idle",
                    PngFrames = [new PngAnimationFrame { FilePath = frame, DurationSeconds = 0.2 }]
                },
                new AnimationScene
                {
                    Id = busyId,
                    Name = "Busy",
                    AnimationPath = animation,
                    DialogLines = [new DialogLine { Text = "Working", VoicePath = voice }]
                }
            ],
            IdleDefaultAnimationId = idleId,
            ReminderAnimationId = busyId
        };
        var service = new CompanionBundleService(dataRoot, CreateOptions());
        var bundlePath = Path.Combine(directory.Path, "test.companionpack");

        service.Export(companion, bundlePath);

        using (var archive = ZipFile.OpenRead(bundlePath))
        using (var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open()))
        {
            var manifest = reader.ReadToEnd();
            Assert.DoesNotContain(sourceRoot, manifest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("assets/", manifest, StringComparison.OrdinalIgnoreCase);
        }

        var preview = service.Inspect(bundlePath);
        Assert.Equal(companion.Name, preview.Name);
        Assert.Equal(2, preview.AnimationCount);

        var importedId = Guid.NewGuid();
        var importResult = service.Import(bundlePath, importedId, "Imported copy");
        var imported = importResult.Companion;

        Assert.Equal(importedId, imported.Id);
        Assert.Equal("Imported copy", imported.Name);
        Assert.Equal(companion.Description, imported.Description);
        Assert.Equal(companion.Personality, imported.Personality);
        Assert.Equal(companion.ThinkingPhrase, imported.ThinkingPhrase);
        Assert.Equal(["Working"], imported.GetDialogueExamples());
        Assert.All(EnumerateAssets(imported), path =>
        {
            Assert.True(File.Exists(path));
            Assert.StartsWith(Path.GetFullPath(dataRoot), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal([1, 2, 3], File.ReadAllBytes(imported.Animations[0].PngFrames[0].FilePath));
        Assert.Equal([6, 7], File.ReadAllBytes(imported.Animations[1].DialogLines[0].VoicePath!));

        service.DiscardImport(importResult);
        Assert.False(Directory.Exists(importResult.AssetDirectory));
    }

    [Fact]
    public void ReimportAfterDeletionDoesNotInheritPrivateHistory()
    {
        using var directory = new TemporaryDirectory();
        var storage = new CompanionStorage(directory.Path);
        var manager = new CompanionManager(storage);
        var scene = new AnimationScene { Name = "Idle", AnimationPath = WriteAsset(directory.Path, "idle.png", [1]) };
        var pet = new CompanionDefinition { Name = "Pip", Animations = [scene], IdleDefaultAnimationId = scene.Id };
        manager.SaveCompanion(pet);
        var history = new ChatHistoryStore(directory.Path);
        history.Append(pet.Id, pet.Name, new("Remember amber", "Okay", DateTimeOffset.Now), "amber");
        var pack = Path.Combine(directory.Path, "pip.companionpack");
        storage.ExportCompanion(pet, pack);
        manager.DeleteCompanion(pet.Id);

        var fresh = storage.ImportCompanionBundle(pack).Companion;
        manager.SaveCompanion(fresh);
        Assert.NotEqual(pet.Id, fresh.Id);
        Assert.Empty(history.ForPet(fresh.Id).Notes);
        Assert.Empty(history.ForPet(fresh.Id).Turns);
        Assert.Equal(["amber"], history.ForPet(pet.Id).Notes);

        history.Append(fresh.Id, fresh.Name, new("Remember teal", "Okay", DateTimeOffset.Now), "teal");
        var overwritten = storage.ImportCompanionBundle(pack, overwriteCompanionId: fresh.Id).Companion;
        manager.SaveCompanion(overwritten);
        Assert.Equal(fresh.Id, overwritten.Id);
        Assert.Equal(["teal"], history.ForPet(overwritten.Id).Notes);
    }

    [Fact]
    public void Import_RejectsTraversalBeforeWritingAssets()
    {
        using var directory = new TemporaryDirectory();
        var dataRoot = Path.Combine(directory.Path, "Data");
        var bundlePath = Path.Combine(directory.Path, "unsafe.companionpack");
        var idleId = Guid.NewGuid();
        var busyId = Guid.NewGuid();
        var companion = new CompanionDefinition
        {
            Name = "Unsafe",
            Animations =
            [
                new AnimationScene
                {
                    Id = idleId,
                    PngFrames = [new PngAnimationFrame { FilePath = "assets/../escape.png" }]
                },
                new AnimationScene
                {
                    Id = busyId,
                    PngFrames = [new PngAnimationFrame { FilePath = "assets/busy.png" }]
                }
            ],
            IdleDefaultAnimationId = idleId,
            ReminderAnimationId = busyId
        };
        using (var file = File.Create(bundlePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var stream = manifestEntry.Open())
            {
                JsonSerializer.Serialize(
                    stream,
                    new
                    {
                        Format = CompanionBundleService.FormatIdentifier,
                        FormatVersion = CompanionBundleService.CurrentFormatVersion,
                        companion
                    },
                    CreateOptions());
            }
            archive.CreateEntry("assets/busy.png");
        }

        var service = new CompanionBundleService(dataRoot, CreateOptions());

        Assert.Throws<InvalidDataException>(() => service.Import(bundlePath, Guid.NewGuid()));
        Assert.False(File.Exists(Path.Combine(directory.Path, "escape.png")));
        Assert.False(Directory.Exists(Path.Combine(dataRoot, "Assets")));
    }

    [Fact]
    public void StorageWithoutExplicitPackage_StartsWithoutACharacter()
    {
        using var directory = new TemporaryDirectory();

        var loaded = new CompanionStorage(directory.Path).Load();

        Assert.Empty(loaded.Companions);
        Assert.True(File.Exists(Path.Combine(directory.Path, "desktop-familiar.json")));
    }

    private static IEnumerable<string> EnumerateAssets(CompanionDefinition companion)
    {
        if (!string.IsNullOrWhiteSpace(companion.ImportedFontPath))
            yield return companion.ImportedFontPath;
        if (!string.IsNullOrWhiteSpace(companion.BubbleImagePath))
            yield return companion.BubbleImagePath;
        foreach (var scene in companion.Animations)
        {
            if (!string.IsNullOrWhiteSpace(scene.AnimationPath))
                yield return scene.AnimationPath;
            foreach (var frame in scene.PngFrames)
                yield return frame.FilePath;
            foreach (var line in scene.DialogLines.Where(line => !string.IsNullOrWhiteSpace(line.VoicePath)))
                yield return line.VoicePath!;
        }
    }

    private static string WriteAsset(string directory, string fileName, byte[] contents)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, contents);
        return path;
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
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"DesktopFamiliar.CompanionBundleServiceTests-{Guid.NewGuid():N}");
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
