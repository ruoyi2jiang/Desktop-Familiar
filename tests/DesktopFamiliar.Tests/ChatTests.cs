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
        history.Clear([a.Id], includeNotes: false);
        Assert.Empty(reopened.ForPet(a.Id).Turns);
        Assert.Equal(["amber-1873"], reopened.ForPet(a.Id).Notes);
        history.Clear([a.Id], includeNotes: true);
        Assert.False(history.Names().ContainsKey(a.Id));
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
        Assert.ThrowsAny<Exception>(() => store.Clear([id], true));
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
        Assert.Equal(10, data.SchemaVersion);
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
