using System.Net;
using System.Text.Json;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class ChatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "df-chat-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class TestEngine : IChatEngine
    {
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
        public TaskCompletionSource<string>? Delayed { get; set; }
        public Task<string> ReplyAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken token)
        {
            Requests.Add(messages);
            return Delayed?.Task ?? Task.FromResult("A short reply.");
        }
    }

    [Fact]
    public async Task SwitchingAppearanceUsesOnlyItsDialogueInTheProfileAndChatRequest()
    {
        var pet = new CompanionDefinition { Appearance = CompanionAppearance.Model3D,
            Animations = [new() { DialogLines = [new() { Text = "Sprite greeting." }] }] };
        var engine = new TestEngine();
        var session = new ChatSession(new ChatHistoryStore(_root), engine);
        session.Select(pet.Id);

        Assert.Empty(pet.GetDialogueExamples());
        await session.SendAsync(pet.Clone(), "local", "Hello", default);
        Assert.DoesNotContain("Sprite greeting.", engine.Requests[0][0].Content);
        Assert.DoesNotContain("examples of style", engine.Requests[0][0].Content);

        pet.Model3D.Animations.Add(new() { DialogLines = [new() { Text = "Model greeting." }] });
        Assert.Equal(["Model greeting."], pet.GetDialogueExamples());
        await session.SendAsync(pet.Clone(), "local", "Hello", default);
        Assert.Contains("Model greeting.", engine.Requests[1][0].Content);
        Assert.DoesNotContain("Sprite greeting.", engine.Requests[1][0].Content);

        pet.Appearance = CompanionAppearance.Images2D;
        Assert.Equal(["Sprite greeting."], pet.GetDialogueExamples());
        await session.SendAsync(pet.Clone(), "local", "Hello", default);
        Assert.Contains("Sprite greeting.", engine.Requests[2][0].Content);
        Assert.DoesNotContain("Model greeting.", engine.Requests[2][0].Content);
    }

    [Fact]
    public async Task SwitchingPets_IsolatesNotesMessagesAndSpeakingExamplesEvenWhenNamesMatch()
    {
        var a = new CompanionDefinition { Name = "Twin", Personality = "Solemn", UserNickname = "Keeper",
            Animations = [new() { DialogLines = [new() { Text = "At your service, {{user}}." }] }] };
        var b = new CompanionDefinition { Name = "Twin", Description = "A moon owl" };
        var history = new ChatHistoryStore(_root);
        var engine = new TestEngine();
        var session = new ChatSession(history, engine);
        session.Select(a.Id);
        await session.SendAsync(a, "local", "Remember amber-1873", default);
        session.Select(b.Id);
        await session.SendAsync(b, "local", "Remember cobalt-6294", default);
        Assert.Equal(2, engine.Requests[1].Count);
        Assert.Contains("A moon owl", engine.Requests[1][0].Content);
        Assert.DoesNotContain("amber", string.Join(" ", engine.Requests[1].Select(m => m.Content)));
        Assert.DoesNotContain("At your service", engine.Requests[1][0].Content);
        session.Select(a.Id);
        await session.SendAsync(a, "local", "What do you recall?", default);
        var back = engine.Requests[2];
        Assert.Contains("At your service, Keeper.", back[0].Content);
        Assert.Contains("amber-1873", back[0].Content);
        Assert.Equal("Remember amber-1873", back[1].Content);
        Assert.DoesNotContain("cobalt", string.Join(" ", back.Select(m => m.Content)));
        var reopened = new ChatHistoryStore(_root);
        Assert.Equal(2, reopened.ForPet(a.Id).Turns.Count);
        Assert.Equal(["cobalt-6294"], reopened.ForPet(b.Id).Notes);
        history.Clear([a.Id], ChatHistoryClearScope.Messages);
        Assert.Empty(reopened.ForPet(a.Id).Turns);
        Assert.Equal(["amber-1873"], reopened.ForPet(a.Id).Notes);
        history.Clear([a.Id], ChatHistoryClearScope.MessagesAndNotes);
        Assert.False(history.ListPets().ContainsKey(a.Id));
        Assert.Single(history.ForPet(b.Id).Turns);
    }

    [Fact]
    public async Task LateReplyAfterSwitchAwayAndBack_CannotCommitToEitherPet()
    {
        var a = new CompanionDefinition();
        var b = new CompanionDefinition();
        var history = new ChatHistoryStore(_root);
        var late = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new TestEngine { Delayed = late };
        var session = new ChatSession(history, engine);
        session.Select(a.Id);
        var old = session.SendAsync(a, "local", "Remember an old request", default);
        session.Select(b.Id);
        engine.Delayed = null;
        await session.SendAsync(b, "local", "Hello", default);
        session.Select(a.Id);
        late.SetResult("Too late");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        Assert.Empty(history.ForPet(a.Id).Notes);
        Assert.Empty(history.ForPet(a.Id).Turns);
        Assert.Single(history.ForPet(b.Id).Turns);
    }

    [Fact]
    public void FailedSave_PreservesPreviousHistoryAndEmitsNoChange()
    {
        var store = new ChatHistoryStore(_root);
        var id = Guid.NewGuid();
        store.Append(id, "Pip", new("hello", "hi", DateTimeOffset.Now), null);
        Directory.CreateDirectory(Path.Combine(_root, "Chat", "history.json.tmp"));
        var notified = false;
        store.Changed += (_, _) => notified = true;
        Assert.ThrowsAny<Exception>(() => store.Clear([id], ChatHistoryClearScope.MessagesAndNotes));
        Assert.Single(store.ForPet(id).Turns);
        Assert.False(notified);
    }

    [Fact]
    public void Schema9ProfilesGainOptionalChatWithoutLosingCharacterText()
    {
        var data = JsonSerializer.Deserialize<DesktopCompanionData>("""
            {"SchemaVersion":9,"Companions":[{"Personality":"Sleepy","Description":"Small"}]}
            """)!;
        new CompanionDataNormalizer().Normalize(data);
        Assert.Equal(DesktopCompanionData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.False(data.Settings.Chat.Enabled);
        Assert.Null(data.Settings.Chat.ModelId);
        Assert.Equal("Ctrl+Shift+F9", data.Settings.Chat.Shortcut);
        Assert.Equal("", data.Companions[0].ThinkingPhrase);
        Assert.Equal("Sleepy", data.Companions[0].Personality);
        Assert.Equal("Small", data.Companions[0].Description);
    }

    [Fact]
    public void Bubbles_PreserveTextAndAllowMoreReadingTimeForLongerPages()
    {
        var reply = "A small dragon tells a long story. " + string.Concat(Enumerable.Repeat("🐉", 240)) + "\nAll done!";
        var pages = ChatBubbleTiming.Pages(reply).ToArray();
        Assert.True(pages.Length >= 3);
        Assert.Equal(reply.Replace(" ", "").Replace("\n", ""), string.Concat(pages).Replace(" ", "").Replace("\n", ""));
        Assert.All(pages, page => Assert.InRange(new System.Globalization.StringInfo(page).LengthInTextElements, 1, 110));
        Assert.True(ChatBubbleTiming.ReadingTime("Hello there!") > ChatBubbleTiming.ReadingTime("Hi"));
    }

    [Fact]
    public void VersionOneHistoryKeepsTurnsAndNotesWhenArchivedPortraitIsAdded()
    {
        var id = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Chat"));
        var path = Path.Combine(directory.FullName, "history.json");
        var expected = new ChatTurn("Hello", "Hi", DateTimeOffset.UtcNow);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1, Pets = new Dictionary<Guid, object>
        {
            [id] = new { Name = "Old name", Turns = new[] { expected }, Notes = new[] { "remember this" } }
        }}));
        var store = new ChatHistoryStore(_root);
        Assert.Null(store.ListPets()[id].Portrait);
        store.RememberArchivedAppearance(new CompanionDefinition { Id = id, Name = "New name", ProfilePicturePath = "portrait.png" });
        var saved = new ChatHistoryStore(_root).ForPet(id);
        Assert.Equal("New name", saved.Name);
        Assert.Equal("portrait.png", saved.ArchivedPortrait?.PicturePath);
        Assert.Equal(expected, Assert.Single(saved.Turns));
        Assert.Equal("remember this", Assert.Single(saved.Notes));
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(2, json.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void ArchivedModelPortraitKeepsItsAssignedIdleAndOlderImagePortraitsStillLoad()
    {
        var id = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Chat"));
        File.WriteAllText(Path.Combine(directory.FullName, "history.json"), JsonSerializer.Serialize(new
        {
            Version = 2, Pets = new Dictionary<Guid, object>
            {
                [id] = new { Name = "Pip", ArchivedPortrait = new { PicturePath = "pic.png", IdleFramePath = "idle.png" } }
            }
        }));
        var store = new ChatHistoryStore(_root);
        Assert.Equal(new CompanionPortrait("pic.png", "idle.png"), store.ListPets()[id].Portrait);
        var pet = new CompanionDefinition { Id = id, Appearance = CompanionAppearance.Model3D, Model3D = new() { FilePath = "pet.glb" } };
        pet.Model3D.CreateInitialScenes("Breathing", "Greeting");
        store.RememberArchivedAppearance(pet);
        Assert.Equal(new CompanionPortrait(null, null, "pet.glb", "Breathing"), new ChatHistoryStore(_root).ListPets()[id].Portrait);
    }

    [Fact]
    public void ClearAppliesOnlyTheCategoriesShownWhenConfirmationOpened()
    {
        var store = new ChatHistoryStore(_root);
        var pet = Guid.NewGuid();
        var turn = new ChatTurn("Hi", "Hello", DateTimeOffset.Now);
        store.Append(pet, "Pip", turn, null);
        var messagesOnlyDialog = store.ForPet(pet);
        store.Append(pet, "Pip", turn, "new note");
        store.Clear([pet], messagesOnlyDialog.GetClearScope(alsoDeleteNotes: true));
        var notesOnlyDialog = store.ForPet(pet);
        Assert.Empty(notesOnlyDialog.Turns);
        Assert.Equal(["new note"], notesOnlyDialog.Notes);

        store.Append(pet, "Pip", turn, null);
        store.Clear([pet], notesOnlyDialog.GetClearScope(alsoDeleteNotes: false));
        var remaining = store.ForPet(pet);
        Assert.Equal(turn, Assert.Single(remaining.Turns));
        Assert.Empty(remaining.Notes);
    }

    [Fact]
    public void ClearingPetsWithoutHistoryKeepsTheStoreUntouched()
    {
        var store = new ChatHistoryStore(_root);
        int changes = 0;
        store.Changed += (_, _) => changes++;
        var unusedId = Guid.NewGuid();
        store.Clear([unusedId], ChatHistoryClearScope.MessagesAndNotes);
        Assert.Equal(0, changes);
        Assert.Equal(0, store.GetTotalSizeBytes());

        var savedId = Guid.NewGuid();
        store.Append(savedId, "Pip", new("Hi", "Hello", DateTimeOffset.Now), null);
        store.Clear([unusedId], ChatHistoryClearScope.MessagesAndNotes);
        Assert.Equal(1, changes);
        Assert.Single(store.ForPet(savedId).Turns);
    }

    [Fact]
    public void StorageSizesReflectSavedUtf8RecordsAndTheActualFile()
    {
        var store = new ChatHistoryStore(_root);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Chat"));
        var path = Path.Combine(directory.FullName, "history.json");
        const string first = """{ "Name": "Twin", "Turns": [], "Notes": ["茶"] }""";
        var second = """{"Name":"Twin","Turns":[],"Notes":["LONG"]}""".Replace("LONG", new string('x', 50_000));
        File.WriteAllText(path, $$"""
            {"Version":1,"Pets":{
                "{{a}}":{{first}},
                "{{b}}":{{second}}
            }
            }
            """);

        var summaries = store.ListPets();
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(first), summaries[a].SizeBytes);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(second), summaries[b].SizeBytes);
        Assert.Equal(new FileInfo(path).Length, store.GetTotalSizeBytes());

        store.Append(a, "Twin", new("Hello", "Hi", DateTimeOffset.UtcNow), null);
        var withTurn = store.ListPets()[a].SizeBytes;
        store.Clear([a], ChatHistoryClearScope.Messages);
        Assert.InRange(store.ListPets()[a].SizeBytes, 1, withTurn - 1);
        Assert.Equal(new FileInfo(path).Length, store.GetTotalSizeBytes());
        store.ClearAll();
        Assert.Empty(store.ListPets());
        Assert.Equal(0, store.GetTotalSizeBytes());
    }

    [Fact]
    public void ClearAllRemovesArchivedAndNotesOnlyEntriesAndIsRepeatable()
    {
        var store = new ChatHistoryStore(_root);
        var first = Guid.NewGuid();
        var archived = Guid.NewGuid();
        store.Append(first, "Twin", new("hello", "hi", DateTimeOffset.UtcNow), "note");
        store.Clear([first], ChatHistoryClearScope.Messages);
        store.Append(archived, "Twin", new("hello", "hi", DateTimeOffset.UtcNow), "private");
        var staging = Path.Combine(_root, "Chat", "history.json.tmp");
        File.Copy(Path.Combine(_root, "Chat", "history.json"), staging);
        store.ClearAll();
        store.ClearAll();
        Assert.False(File.Exists(staging));
        Assert.Empty(new ChatHistoryStore(_root).ListPets());
        Assert.False(File.Exists(Path.Combine(_root, "Chat", "history.json")));
    }

    [Fact]
    public async Task DamagedDownloadCannotInstallEngineOrModel()
    {
        using var http = new HttpClient(new BrokenRelease());
        var store = new ChatModelStore(_root, http);
        if (!ChatModelStore.Supported) return;
        var id = ChatModelStore.Catalogue[0].Id;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(id, new Progress<long>(), default));
        Assert.False(store.Installed(id));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root, "Chat", "Models")));
    }

    private sealed class BrokenRelease : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1,2,3]) });
    }
}
