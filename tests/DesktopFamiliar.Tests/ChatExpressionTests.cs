using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class ChatExpressionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("df-expressions-").FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Schema14LoadsWithAutomaticMatchesAndWritesOnlyOnSave()
    {
        var storage = new CompanionStorage(_root);
        var legacy = Path.Combine(_root, "Library", "desktop-familiar.v14.json");
        var old = """{"SchemaVersion":14,"Companions":[{"Animations":[{"Name":"Happy"}],"Model3D":{"Animations":[{"Name":"Ponder"}]}}]}""";
        File.WriteAllText(legacy, old);
        var data = storage.Load();
        var pet = Assert.Single(data.Companions);
        Assert.Equal("happy", Assert.Single(ChatExpressionPolicy.Available(pet)).Key);
        Assert.Equal("thoughtful", Assert.Single(ChatExpressionPolicy.Available(pet.Model3D)).Key);
        Assert.False(File.Exists(storage.DataFilePath));
        pet.ChatExpressions["happy"] = null;
        var copy = pet.Clone();
        copy.ChatExpressions.Clear();
        Assert.Empty(ChatExpressionPolicy.Available(pet));
        storage.Save(data);
        Assert.Null(Assert.Single(storage.Load().Companions).ChatExpressions["happy"]);
        Assert.Equal(old, File.ReadAllText(legacy));
    }

    [Fact]
    public void MatchingPrefersUniqueWholeScenesAndAllowsExplicitOverrides()
    {
        var smile = new AnimationScene { Name = "Smile" };
        var happy = new AnimationScene { Name = " HAPPY " };
        var scenes = new CompanionDefinition { Animations = [smile, happy,
            new() { Name = "Sad", PartOfSceneId = smile.Id }, new() { Name = "Angry attack" }] };
        Assert.Equal(happy.Id, Assert.Single(ChatExpressionPolicy.Available(scenes)).Value);
        scenes.Animations.Add(new() { Name = "Happy" });
        Assert.Empty(ChatExpressionPolicy.Available(scenes));
        scenes.ChatExpressions["happy"] = smile.Id;
        Assert.Equal(smile.Id, ChatExpressionPolicy.Available(scenes)["happy"]);
        scenes.Animations.Remove(smile);
        new CompanionDataNormalizer().Normalize(new() { Companions = [scenes] });
        Assert.Null(scenes.ChatExpressions["happy"]);
        Assert.Empty(ChatExpressionPolicy.Available(scenes));
    }

    [Fact]
    public async Task SessionOffersOnlyActivePetAndAppearanceAndStoresSpokenText()
    {
        var happy = new AnimationScene { Name = "Happy" };
        var thoughtful = new AnimationScene { Name = "Ponder" };
        var pet = new CompanionDefinition { Animations = [happy], Model3D = new() { Animations = [thoughtful] } };
        var other = new CompanionDefinition { Animations = [new() { Name = "Angry" }] };
        var engine = new ExpressionEngine();
        var history = new ChatHistoryStore(_root);
        var session = new ChatSession(history, engine);
        session.Select(pet.Id);
        var reply = await session.SendReplyAsync(pet.Clone(), "model", "Great news!", default);
        Assert.Equal(["happy"], engine.Offered);
        Assert.Equal(happy.Id, reply.ExpressionSceneId);
        Assert.Equal("Hello!", Assert.Single(history.ForPet(pet.Id).Turns).Reply);
        pet.Appearance = CompanionAppearance.Model3D;
        Assert.Null((await session.SendReplyAsync(pet.Clone(), "model", "Again?", default)).ExpressionSceneId);
        Assert.Equal(["thoughtful"], engine.Offered);
        session.Select(other.Id);
        Assert.Null((await session.SendReplyAsync(other, "model", "Hi", default)).ExpressionSceneId);
        Assert.Equal(["angry"], engine.Offered);
        other.ChatExpressions["angry"] = null;
        Assert.Null((await session.SendReplyAsync(other, "model", "Hi", default)).ExpressionSceneId);
        Assert.True(engine.UsedPlain);
    }

    [Fact]
    public void PlaybackUsesTalkingFallbackAndChangesOnlyWhenAVisibleReplyNeedsADifferentScene()
    {
        var reaction = new AnimationScene();
        var talk = new AnimationScene();
        var scenes = new CompanionDefinition { Animations = [reaction, talk], ChatReplyAnimationId = talk.Id };
        var playback = new ChatReplyPlayback(reaction.Id);
        Assert.Same(reaction, playback.Scene(scenes));
        Assert.False(ScenePlaybackPolicy.CanStartChatReply(AnimationSceneRole.ChatReply, CompanionActivityState.Idle, false, reaction.Id, reaction.Id));
        Assert.True(ScenePlaybackPolicy.CanStartChatReply(AnimationSceneRole.ChatReply, CompanionActivityState.Idle, false, reaction.Id, talk.Id));
        Assert.False(ScenePlaybackPolicy.CanStartChatReply(AnimationSceneRole.Reminder, CompanionActivityState.Reminder, false, talk.Id, reaction.Id));
        playback.MarkFailed(reaction.Id);
        Assert.Same(talk, playback.Scene(scenes));
        playback.MarkFailed(talk.Id);
        Assert.Null(playback.Scene(scenes));
        Assert.Same(talk, new ChatReplyPlayback(null).Scene(scenes));
    }

    [Theory]
    [InlineData("happy", "happy")]
    [InlineData("neutral", null)]
    [InlineData("unavailable", null)]
    public async Task StructuredRequestUsesAllowedChoicesAndExtractsOnlySpeech(string key, string? expected)
    {
        var handler = new CompletionHandler((body, call) =>
        {
            Assert.Equal(1, call);
            var choices = body.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema")
                .GetProperty("properties").GetProperty("expression").GetProperty("enum");
            Assert.Equal(["neutral", "happy"], choices.EnumerateArray().Select(item => item.GetString()));
            return Completion(JsonSerializer.Serialize(new { expression = key, reply = "I love that!" }));
        });
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost") };
        var reply = await LlamaChatEngine.RequestReplyAsync(http, "local", [new("user", "Hello")], ["happy"], 220, default);
        Assert.Equal(new ModelChatReply("I love that!", expected), reply);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectedOrMalformedStructuredGenerationRetriesPlainSpeech(bool rejected)
    {
        var handler = new CompletionHandler((body, call) =>
        {
            if (call == 1) return rejected ? new(HttpStatusCode.BadRequest) : Completion("""{"expression":"happy","reply":""");
            Assert.Equal(2, call);
            Assert.False(body.TryGetProperty("response_format", out _));
            Assert.Single(body.GetProperty("messages").EnumerateArray());
            return Completion("Hello again.");
        });
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost") };
        var reply = await LlamaChatEngine.RequestReplyAsync(http, "local", [new("user", "Hello")], ["happy"], 220, default);
        Assert.Equal(new ModelChatReply("Hello again."), reply);
    }

    private static HttpResponseMessage Completion(string content) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }) };

    private sealed class CompletionHandler(Func<JsonElement, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            return respond(json.RootElement, ++_calls);
        }
    }

    private sealed class ExpressionEngine : IChatEngine
    {
        public IReadOnlyList<string> Offered { get; private set; } = [];
        public bool UsedPlain { get; private set; }
        public Task<string> ReplyAsync(string modelId, IReadOnlyList<ChatMessage> messages, CancellationToken token)
        {
            UsedPlain = true;
            return Task.FromResult("Ordinary reply.");
        }
        public Task<ModelChatReply> ReplyWithExpressionAsync(string modelId, IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<string> expressions, CancellationToken token)
        {
            Offered = expressions;
            return Task.FromResult(new ModelChatReply("Hello!", "happy"));
        }
    }
}
