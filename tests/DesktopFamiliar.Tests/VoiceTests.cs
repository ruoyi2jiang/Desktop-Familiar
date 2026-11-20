using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;
using DesktopFamiliar.Services.Voice;

namespace DesktopFamiliar.Tests;

public sealed class VoiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void MigrationKeepsExistingChatShortcutWhenItUsesTheNewVoiceDefault()
    {
        var store = new CompanionStorage(_root);
        var old = Path.Combine(Path.GetDirectoryName(store.DataFilePath)!, "desktop-familiar.v15.json");
        const string original = """{"SchemaVersion":15,"Settings":{"Chat":{"Enabled":true,"Shortcut":"Ctrl+Shift+F10"}}}""";
        File.WriteAllText(old, original);
        var data = store.Load();
        Assert.Equal("Ctrl+Shift+F10", data.Settings.Chat.Shortcut);
        Assert.Equal("Ctrl+Shift+F9", data.Settings.Chat.TalkShortcut);
        Assert.Equal(original, File.ReadAllText(old));
        store.Save(data);
        Assert.Equal(data.Settings.Chat, store.Load().Settings.Chat);
    }

    [Fact]
    public void DictationPreservesTheExistingDraftAndEntireTranscriptAcrossTheInputLimit()
    {
        string draft = new('a', 1198);
        const string transcript = "🌻 remember the last sentence";
        Assert.Equal(draft + " " + transcript, PushToTalkSession.AppendTranscript(draft + "  ", transcript));
    }

    [Fact]
    public void OpeningSchema15DefaultsVoiceInMemoryAndPreservesObservedReminderAndWipeReceipt()
    {
        var storage = new CompanionStorage(_root);
        var dir = Path.GetDirectoryName(storage.DataFilePath)!;
        var pet = Guid.NewGuid();
        var reminder = Guid.NewGuid();
        var wiped = Guid.NewGuid();
        var source = Path.Combine(dir, "desktop-familiar.v15.json");
        var old = JsonSerializer.SerializeToUtf8Bytes(new { SchemaVersion = 15,
            Settings = new { Chat = new { Enabled = true } }, Companions = new[] { new { Id = pet, Name = "Test", Personality = "Kind" } },
            CalendarEvents = new[] { new { Id = reminder, Title = "Reminder" } }, PendingCompanionCleanup = new[] { wiped } });
        File.WriteAllBytes(source, old);
        new CalendarExpirationStore(dir, 15).Record(Convert.ToHexString(SHA256.HashData(old)), reminder, DateTime.Today);
        File.WriteAllBytes(Path.Combine(dir, $"history-wipe.v15.{wiped:N}"), []);
        var data = storage.Load();
        Assert.True(data.Settings.Chat.VoiceEnabled);
        Assert.False(data.Settings.Chat.SendOnRelease);
        Assert.Equal(new CompanionVoice(), data.Companions.Single().Voice);
        Assert.Equal("Kind", data.Companions.Single().Personality);
        Assert.True(data.CalendarEvents.Single().IsExpired);
        Assert.False(File.Exists(storage.DataFilePath));
        Assert.False(File.Exists(storage.HistoryWipeReceipt(wiped)));
        storage.Save(data);
        Assert.True(File.Exists(storage.HistoryWipeReceipt(wiped)));
        Assert.Equal(old, File.ReadAllBytes(source));
    }

    [Fact]
    public void VoicePreferencesStayWithEachPetAcrossAppearanceChangesAndBundleImport()
    {
        var storage = new CompanionStorage(_root);
        var data = storage.Load();
        var path = Path.Combine(_root, "idle.png");
        File.WriteAllBytes(path, [1]);
        var scene = new AnimationScene { Name = "Idle", AnimationPath = path };
        var a = new CompanionDefinition { Name = "One", Animations = [scene], IdleDefaultAnimationId = scene.Id,
            Voice = new() { Id = "local-example", Speaker = 2, Rate = 3, ReplyLanguage = "ja-JP" } };
        var b = new CompanionDefinition { Name = "Two", Appearance = CompanionAppearance.Model3D,
            Voice = new() { Id = "off", ReplyLanguage = "en-GB" } };
        data.Settings.Chat = new(true) { VoiceEnabled = false, MicrophoneId = "endpoint", SendOnRelease = true };
        data.Companions = [a, b];
        storage.Save(data);
        var loaded = storage.Load();
        Assert.Equal(data.Settings.Chat, loaded.Settings.Chat);
        Assert.Equal(a.Voice, loaded.Companions[0].Voice);
        Assert.Equal(b.Voice, loaded.Companions[1].Voice);
        var clone = a.Clone();
        clone.Appearance = CompanionAppearance.Model3D;
        Assert.Equal(a.Voice, clone.Voice);
        clone.Voice = clone.Voice with { Speaker = 0 };
        Assert.Equal(2, a.Voice.Speaker);

        var pack = Path.Combine(_root, "sample.companionpack");
        var service = new CompanionBundleService(_root, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        service.Export(a, pack);
        using (var archive = ZipFile.OpenRead(pack))
        using (var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open()))
            Assert.Equal(6, manifest.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Equal(a.Voice, service.Import(pack, Guid.NewGuid()).Companion.Voice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MicTestReleasesTheInputBeforePlayingTheSameRecording(bool cancelPlayback)
    {
        using var stream = new MemoryStream();
        using var pcm = new NAudio.Wave.RawSourceWaveStream(new MemoryStream([0, 64, 0, 0]), new NAudio.Wave.WaveFormat(16000, 16, 1));
        NAudio.Wave.WaveFileWriter.WriteWavFileToStream(stream, pcm);
        var wave = stream.ToArray();
        var capture = new Recording(wave);
        var played = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        var test = MicrophoneTest.RunAsync(() => capture, TimeSpan.Zero, (audio, token) =>
        {
            Assert.True(capture.Disposed);
            Assert.Same(wave, audio);
            return played.Task.WaitAsync(token);
        }, stop.Token);
        Assert.False(test.IsCompleted);
        if (cancelPlayback)
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
            return;
        }
        played.SetResult();
        Assert.Equal(0.5, await test);
    }

    [Fact]
    public async Task CancellingMicTestDuringCaptureDiscardsTheSample()
    {
        using var stop = new CancellationTokenSource();
        var capture = new Recording();
        var test = MicrophoneTest.RunAsync(() => capture, Timeout.InfiniteTimeSpan,
            (_, _) => throw new InvalidOperationException("Unexpected playback"), stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
        Assert.True(capture.Disposed);
    }

    [Fact]
    public async Task ClosingPromptDiscardsLateTranscriptBeforeAnotherPetCanReceiveIt()
    {
        var first = new Recording();
        var recognition = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var prior = new PushToTalkSession(() => first, (_, _) => recognition.Task);
        Assert.True(prior.Begin());
        Assert.False(prior.Begin());
        var oldText = prior.ReleaseAsync();
        Assert.True(first.Disposed);
        Assert.False(prior.Begin());
        prior.Dispose();
        recognition.SetResult("private to the previous pet");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldText);
        using var next = new PushToTalkSession(() => new Recording(), (_, _) => Task.FromResult("new message"));
        Assert.Null(await next.ReleaseAsync());
        Assert.True(next.Begin());
        Assert.Equal("new message", await next.ReleaseAsync());
        Assert.False(prior.Begin());
    }

    [Fact]
    public async Task ClosingWhileTheKeyIsDownReleasesTheMicWithoutTranscribing()
    {
        var capture = new Recording();
        using var session = new PushToTalkSession(() => capture, (_, _) => throw new InvalidOperationException("Unexpected recognition"));
        Assert.True(session.Begin());
        session.Dispose();
        Assert.True(capture.Disposed);
        Assert.Null(await session.ReleaseAsync());
        Assert.False(session.Begin());
    }

    [Fact]
    public void IncompleteManagedBankDoesNotPreventVoiceDiscoveryOrARepairDownload()
    {
        using var http = new HttpClient(new BadDownload());
        var store = new VoiceBankStore(_root, http);
        var asset = VoiceBankStore.Catalogue.Banks[0];
        var path = store.PathFor(asset);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        Assert.Empty(store.Installed());
        Assert.Contains(asset, VoiceBankStore.Catalogue.Banks);
    }

    [Fact]
    public async Task RecognitionFailureReleasesResourcesAndAllowsAnotherHold()
    {
        var first = new Recording();
        using var session = new PushToTalkSession(() => first, (_, _) => throw new IOException());
        Assert.True(session.Begin());
        await Assert.ThrowsAsync<IOException>(() => session.ReleaseAsync());
        Assert.True(first.Disposed);
        Assert.False(session.Transcribing);
        Assert.True(session.Begin());
    }

    [Fact]
    public void DownloadCataloguePinsAllPayloadsAndDiscoversSystemVoices()
    {
        var cat = VoiceBankStore.Catalogue;
        Assert.NotEmpty(cat.Windows.Categories);
        Assert.Contains("ja-JP", cat.Languages);
        var all = cat.Banks.Append(cat.Recognition).Append(cat.RecognitionEngine).Append(cat.VoiceEngine).ToArray();
        Assert.Equal(all.Length, all.DistinctBy(a => a.Id).Count());
        Assert.All(all, a =>
        {
            Assert.Equal("https", new Uri(a.Url).Scheme);
            Assert.True(a.Size > 0);
            Assert.Matches("^[a-f0-9]{64}$", a.Sha256);
            Assert.False(Path.IsPathRooted(a.Entry));
            Assert.DoesNotContain("..", a.Entry);
        });
        Assert.Equal(RecognitionSource.Windows, VoiceService.Plan("en-GB", false, ["en-GB"], true).Source);
        Assert.Equal(RecognitionSource.Missing, VoiceService.Plan("ja-JP", false, ["en-GB"], false).Source);
        Assert.Equal(RecognitionSource.Missing, VoiceService.Plan("en-GB", true, ["en-GB"], false).Source);
        Assert.Equal(RecognitionSource.Whisper, VoiceService.Plan("en-GB", true, ["en-GB"], true).Source);
        Assert.Equal("ja-JP", VoiceService.ResolveLanguage("ja-JP", ["en-GB"]));
    }

    [Fact]
    public async Task FailedVerifiedDownloadLeavesNoInstalledVoiceOrTemporaryPayload()
    {
        using var http = new HttpClient(new BadDownload());
        var store = new VoiceBankStore(_root, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallRecognitionAsync(null, default));
        Assert.False(store.HasRecognition);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root, "Voice")));
    }

    [Fact]
    public async Task ImportCopiesOnlyVoiceDataAndReusesAnIdenticalBank()
    {
        var source = Path.Combine(_root, "example");
        var data = Directory.CreateDirectory(Path.Combine(source, "espeak-ng-data")).FullName;
        File.WriteAllBytes(Path.Combine(source, "model.onnx"), [1]);
        File.WriteAllText(Path.Combine(source, "tokens.txt"), "a 0");
        File.WriteAllBytes(Path.Combine(source, "voices.bin"), new byte[511 * 256 * 4 * 2]);
        File.WriteAllBytes(Path.Combine(data, "phondata"), [3]);
        File.WriteAllBytes(Path.Combine(source, "extra.exe"), [7]);
        using var http = new HttpClient(new BadDownload());
        var store = new VoiceBankStore(_root, http);
        var bank = await store.ImportAsync(source, default);
        Assert.Equal(bank.Id, (await store.ImportAsync(source, default)).Id);
        Assert.Equal(2, bank.Speakers);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(bank.Model)!, "extra.exe")));
        File.Delete(Path.Combine(Path.GetDirectoryName(bank.Model)!, "tokens.txt"));
        Assert.Empty(store.Installed());
        Assert.Equal(bank, await store.ImportAsync(source, default));
        Assert.Equal(bank, Assert.Single(store.Installed()));
        File.WriteAllText(Path.Combine(source, "tokens.txt"), "b 0");
        Assert.NotEqual(bank.Id, (await store.ImportAsync(source, default)).Id);
        Directory.Delete(source, true);
        Assert.Equal(2, store.Installed().Count);
    }

    [Fact]
    public async Task ReplyLanguageTravelsWithThePetAndNeverBecomesASpokenInstruction()
    {
        var model = new RecordingChat();
        var history = new ChatHistoryStore(_root);
        var session = new ChatSession(history, model);
        foreach (var tag in new[] { "ja-JP", "fr-FR" })
        {
            var pet = new CompanionDefinition { Voice = new() { ReplyLanguage = tag } };
            session.Select(pet.Id);
            await session.SendAsync(pet, "model", "Hi", default);
            Assert.Contains(System.Globalization.CultureInfo.GetCultureInfo(tag).EnglishName, model.Instructions);
            Assert.Equal("hello", Assert.Single(history.ForPet(pet.Id).Turns).Reply);
        }
    }

    private sealed class Recording(byte[]? wave = null) : IVoiceRecording
    {
        internal bool Disposed { get; private set; }
        public Task<byte[]> FinishAsync(CancellationToken token) => Task.FromResult<byte[]>(wave ?? [1]);
        public void Dispose() => Disposed = true;
    }

    private sealed class BadDownload : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
    }

    private sealed class RecordingChat : IChatEngine
    {
        public string Instructions = "";
        public Task<string> ReplyAsync(string modelId, IReadOnlyList<ChatMessage> messages, CancellationToken token)
        {
            Instructions = messages[0].Content;
            return Task.FromResult("hello");
        }
    }
}
