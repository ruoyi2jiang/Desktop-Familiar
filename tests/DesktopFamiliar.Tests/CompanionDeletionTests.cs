using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class CompanionDeletionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("familiar-deletion-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteRemovesConfigurationAndOptionallyOnlyTheSelectedPetsPrivateData(bool deleteLocalData)
    {
        var storage = new CompanionStorage(_root);
        var manager = new CompanionManager(storage);
        var history = new ChatHistoryStore(_root);
        var pet = SavePet(manager, storage, history);
        var other = SavePet(manager, storage, history);
        other.BubbleImagePath = pet.BubbleImagePath;
        manager.SaveCompanion(other);

        Assert.True(new CompanionDeletionService(manager, storage, history).Delete(pet.Id, deleteLocalData));

        var reloaded = storage.Load();
        Assert.Equal(other.Id, Assert.Single(reloaded.Companions).Id);
        if (deleteLocalData) Assert.Equal([pet.Id], reloaded.PendingCompanionCleanup);
        else Assert.Empty(reloaded.PendingCompanionCleanup);
        Assert.Equal(!deleteLocalData, File.Exists(pet.ProfilePicturePath));
        Assert.Equal(!deleteLocalData, history.ListPets().ContainsKey(pet.Id));
        Assert.Equal(!deleteLocalData, history.ForPet(pet.Id).Notes.Any());
        if (!deleteLocalData)
            Assert.Equal(new CompanionPortrait(pet.ProfilePicturePath, pet.IdleDefaultAnimation?.GetPreviewPath()),
                history.ListPets()[pet.Id].Portrait);
        Assert.True(File.Exists(other.ProfilePicturePath));
        Assert.True(File.Exists(other.BubbleImagePath));
        Assert.Single(history.ForPet(other.Id).Notes);
        Assert.Single(history.ForPet(other.Id).Turns);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPortraitIsRemovedAfterItsLastLiveOrArchivedReferenceIsReleased(bool keepSecondHistory)
    {
        var storage = new CompanionStorage(_root);
        var manager = new CompanionManager(storage);
        var history = new ChatHistoryStore(_root);
        var owner = SavePet(manager, storage, history);
        var second = SavePet(manager, storage, history);
        second.ProfilePicturePath = owner.ProfilePicturePath;
        manager.SaveCompanion(second);
        var deletion = new CompanionDeletionService(manager, storage, history);
        Assert.True(deletion.Delete(owner.Id, deleteLocalData: true));
        Assert.True(File.Exists(second.ProfilePicturePath));

        manager = new CompanionManager(storage);
        deletion = new CompanionDeletionService(manager, storage, history);
        Assert.True(deletion.Delete(second.Id, deleteLocalData: !keepSecondHistory));
        if (keepSecondHistory)
        {
            Assert.Equal([owner.Id], storage.Load().PendingCompanionCleanup);
            Assert.True(File.Exists(history.ForPet(second.Id).ArchivedPortrait!.PicturePath));
            history.ClearAll();
            deletion.RetryPending();
        }
        Assert.Empty(storage.Load().PendingCompanionCleanup);
        Assert.False(File.Exists(owner.ProfilePicturePath));
    }

    [Fact]
    public void WipeKeepsSharedFilesReferencedOnlyByAnotherPetInAnIndependentLibrary()
    {
        var storage = new CompanionStorage(_root);
        var manager = new CompanionManager(storage);
        var history = new ChatHistoryStore(_root);
        var owner = SavePet(manager, storage, history);
        var otherId = Guid.NewGuid();
        var sibling = Path.Combine(Path.GetDirectoryName(storage.DataFilePath)!, "desktop-familiar.v99.json");
        var original = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 99,
            Companions = new object[] {
                new { owner.Id, owner.ProfilePicturePath },
                new { Id = otherId, FutureLayers = new[] { owner.BubbleImagePath } }
            }
        });
        File.WriteAllText(sibling, original);

        var deletion = new CompanionDeletionService(manager, storage, history);
        Assert.True(deletion.Delete(owner.Id, deleteLocalData: true));
        Assert.False(File.Exists(owner.ProfilePicturePath));
        Assert.True(File.Exists(owner.BubbleImagePath));
        Assert.Equal(original, File.ReadAllText(sibling));
        Assert.Contains(owner.Id, manager.Data.PendingCompanionCleanup);

        File.Delete(sibling);
        deletion.RetryPending();
        Assert.False(File.Exists(owner.BubbleImagePath));
        Assert.Empty(manager.Data.PendingCompanionCleanup);
    }

    [Fact]
    public void FailedCleanupSurvivesRestartAndRetriesOnlyTheConfirmedPet()
    {
        var storage = new CompanionStorage(_root);
        var manager = new CompanionManager(storage);
        var history = new ChatHistoryStore(_root);
        var pet = SavePet(manager, storage, history);
        var other = SavePet(manager, storage, history);
        var deletion = new CompanionDeletionService(manager, storage, history);
        using (File.Open(pet.ProfilePicturePath!, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(deletion.Delete(pet.Id, deleteLocalData: true));
        Assert.Equal([pet.Id], storage.Load().PendingCompanionCleanup);
        var beforeLaunch = File.ReadAllBytes(storage.DataFilePath);
        var restarted = new CompanionManager(storage);
        new CompanionDeletionService(restarted, storage, history).RetryPending(deferSave: true);
        Assert.Empty(restarted.Data.PendingCompanionCleanup);
        Assert.Equal(beforeLaunch, File.ReadAllBytes(storage.DataFilePath));
        Assert.False(File.Exists(pet.ProfilePicturePath));
        Assert.False(history.ListPets().ContainsKey(pet.Id));
        Assert.True(File.Exists(other.ProfilePicturePath));
        Assert.Single(history.ForPet(other.Id).Turns);
        // A different schema can still have the same pet registered and start a fresh conversation.
        history.Append(pet.Id, pet.Name, new("New conversation", "Hi", DateTimeOffset.UtcNow), "new note");
        var anotherStartup = new CompanionManager(storage);
        new CompanionDeletionService(anotherStartup, storage, history).RetryPending(deferSave: true);
        Assert.Equal("New conversation", Assert.Single(history.ForPet(pet.Id).Turns).Prompt);
        Assert.Equal(["new note"], history.ForPet(pet.Id).Notes);
        Assert.Equal(beforeLaunch, File.ReadAllBytes(storage.DataFilePath));
        anotherStartup.SavePosition(20, 20);
        Assert.Empty(storage.Load().PendingCompanionCleanup);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(storage.DataFilePath)!, "history-wipe.*"));
    }

    [Fact]
    public void SchemaTenMigratesToIdlePortraitWithoutQueuingCleanupOrChangingHistory()
    {
        var storage = new CompanionStorage(_root);
        var history = new ChatHistoryStore(_root);
        var pet = SavePet(new CompanionManager(storage), storage, history);
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(storage.DataFilePath))!;
        json["SchemaVersion"] = 10;
        json.AsObject().Remove("PendingCompanionCleanup");
        json["Companions"]![0]!.AsObject().Remove("ProfilePicturePath");
        File.WriteAllText(storage.DataFilePath, json.ToJsonString());
        var migrated = storage.Load();
        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Empty(migrated.PendingCompanionCleanup);
        Assert.Null(migrated.Companions.Single().ProfilePicturePath);
        Assert.Equal(pet.IdleDefaultAnimationId, migrated.Companions.Single().IdleDefaultAnimationId);
        Assert.Single(history.ForPet(pet.Id).Turns);
    }

    private CompanionDefinition SavePet(CompanionManager manager, CompanionStorage storage, ChatHistoryStore history)
    {
        var source = Path.Combine(_root, "image.png");
        File.WriteAllBytes(source, [1]);
        var scene = new AnimationScene { Name = "Idle", AnimationPath = source };
        var pet = new CompanionDefinition { Name = "Twin", Animations = [scene], IdleDefaultAnimationId = scene.Id };
        pet.ProfilePicturePath = storage.ImportAsset(pet.Id, source, "Portraits");
        pet.BubbleImagePath = storage.ImportAsset(pet.Id, source, "Bubbles");
        manager.SaveCompanion(pet);
        history.Append(pet.Id, pet.Name, new("Hello", "Hi", DateTimeOffset.UtcNow), "a private note");
        return pet;
    }
}
