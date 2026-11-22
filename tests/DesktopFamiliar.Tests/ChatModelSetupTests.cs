using System.Net;
using System.Security.Cryptography;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class ChatModelSetupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("df-models-").FullName;
    private static readonly IProgress<long> Progress = new Progress<long>();
    private static ChatModel Gemma => ChatModelStore.Catalogue.Single(m => m.Id == "gemma-3-1b-it-q4");

    [Fact]
    public void LocalMeasurementsAreSeparatedByModelAndGpuPreference()
    {
        var results = new ChatModelTestResults();
        var cpu = new ChatModelTestResult("Sample answer", TimeSpan.FromSeconds(2), 24, 1024, ChatComputeBackend.Cpu, 14);
        var fallback = cpu with { Backend = ChatComputeBackend.CpuFallback, TokensPerSecond = 21 };
        results.Record("first", false, cpu);
        Assert.Null(results.Get("first", true));
        results.Record("first", true, fallback);
        Assert.Same(cpu, results.Get("first", false));
        Assert.Same(fallback, results.Get("first", true));
        Assert.Null(results.Get("other", false));
        Assert.Contains("14 tokens per reply", ChatModelTestResults.Summary(Gemma, cpu));
        Assert.Contains("24.0 tokens/s", ChatModelTestResults.Summary(Gemma, cpu));
        Assert.Contains("Sample answer", ChatModelTestResults.Details(cpu));
        Assert.DoesNotContain("tokens/s", ChatModelTestResults.Summary(Gemma, null));
        Assert.DoesNotContain("tokens per", ChatModelTestResults.Summary(null, null));
    }

    [Fact]
    public void LinkedCatalogueWeightsGetTheSameRequirementsAsTheDownload()
    {
        Assert.Same(Gemma, ChatModelStore.CatalogueEntry(Gemma.Id));
        Assert.Same(Gemma, ChatModelStore.CatalogueEntry("local:" + Gemma.Sha256.ToUpperInvariant()));
        Assert.NotNull(Gemma.Requirements);
        Assert.Null(ChatModelStore.CatalogueEntry("local:" + new string('a', 64)));
    }

    [Theory]
    [InlineData("gemma-3-1b-it-q4")]
    [InlineData("lfm2-5-1-2b-instruct-q4")]
    [InlineData("qwen3-5-2b-q4")]
    [InlineData("qwen3-4b-instruct-2507-q4")]
    public void ExistingLinkedFilesGainCatalogueDetailsWithoutBeingCopiedOrRelinked(string id)
    {
        LegacyEngine();
        var model = Assert.Single(ChatModelStore.Catalogue, m => m.Id == id);
        var path = FileAt("external/model.gguf", []);
        var link = "local:" + model.Sha256;
        new LocalChatModelIndex(_root).Link(link, path);
        using var network = new Requests();
        using var http = new HttpClient(network);
        var store = new ChatModelStore(_root, http);
        Assert.Equal(link, store.AvailableIdFor(model));
        Assert.Equal(new AvailableChatModel(link, model.Name, path), Assert.Single(store.AvailableModels()));
        Assert.Same(model, ChatModelStore.CatalogueEntry(link));
        Assert.True(model.FileBytes < model.Requirements!.SetupSpaceMb * 1_000_000L);
        Assert.InRange(model.SampleReplyTokens!.Value, 1, 64);
        Assert.Equal(model.Sha256, store.TestKey(model.Id));
        Assert.Equal(store.TestKey(model.Id), store.TestKey(link));
        Assert.DoesNotContain("[Chat.", DesktopFamiliar.Localization.LocalizationService.Get(model.StrengthsKey));
        Assert.Equal(id == "qwen3-5-2b-q4", model.DisableThinking);
        Assert.Empty(network.Urls);
    }

    [Fact]
    public async Task FailedGpuDownloadKeepsExistingCpuAndDoesNotOptInOrLeaveStaging()
    {
        if (!ChatModelStore.Supported) return;
        var cpu = LegacyEngine();
        using var http = new HttpClient(new Requests());
        var store = new ChatModelStore(_root, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SetGpuEnabledAsync(true, default));
        Assert.False(store.GpuEnabled);
        Assert.False(store.GpuInstalled);
        Assert.Equal(cpu, store.EnginePath(Gemma.Id));
        Assert.Empty(Directory.GetDirectories(_root, "*.install-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TurningOffGpuKeepsTheCachedEngineAndWorksOffline()
    {
        using var requests = new Requests();
        using var http = new HttpClient(requests);
        var store = new ChatModelStore(_root, http);
        FileAt("Chat/Engines/b12496-vulkan/llama-server.exe", []);
        FileAt("Chat/Engines/b12496-vulkan/enabled", []);
        Assert.True(store.GpuEnabled);
        await store.SetGpuEnabledAsync(false, default);
        Assert.False(new ChatModelStore(_root, http).GpuEnabled);
        Assert.True(store.GpuInstalled);
        Assert.Empty(requests.Urls);
    }

    [Theory]
    [InlineData(true, 4, 2000, 1000, true, "Recommended")]
    [InlineData(true, 4, 1999, 1000, true, "Limited")]
    [InlineData(true, 2, 8000, 1000, true, "Limited")]
    [InlineData(true, 4, 8000, 999, true, "LowDisk")]
    [InlineData(true, 4, 2000, 0, false, "Recommended")]
    [InlineData(true, 4, null, 1000, true, "Unknown")]
    [InlineData(true, 4, 2000, null, true, "Unknown")]
    [InlineData(false, 16, 8000, 8000, false, "Unsupported")]
    public void SuitabilityChecksAvailableResourcesAndOnlyChecksDiskForDownloads(
        bool supported, int threads, int? ramMb, int? diskMb, bool downloading, string fit)
    {
        var pc = new ChatModelPc(supported, threads, ramMb * 1_000_000L, diskMb * 1_000_000L);
        Assert.Equal(fit, pc.Assess(new ChatModelRequirements(2000, 4, 1000, 2000), downloading).ToString());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FileAt(string relative, byte[] bytes)
    {
        var path = Path.GetFullPath(relative, _root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class Requests(byte[]? response = null) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response ?? [42]) });
        }
    }

    private string LegacyEngine() => FileAt($"Chat/Models/{Gemma.Id}/engine/llama-server.exe", []);
    private static byte[] Fixture(byte value) => [.. "GGUF"u8.ToArray(), .. new byte[24], value];

    [Fact]
    public void ExistingCatalogueSelectionWorksWithoutAnIndex()
    {
        LegacyEngine();
        var path = FileAt($"Chat/Models/{Gemma.Id}/model.gguf", []);
        using var http = new HttpClient(new Requests());
        var store = new ChatModelStore(_root, http);
        Assert.True(store.Installed(Gemma.Id));
        Assert.False(store.Installed("../unknown"));
        var item = Assert.Single(store.AvailableModels());
        Assert.Equal((Gemma.Id, path), (item.Id, item.Path));
        Assert.False(File.Exists(Path.Combine(_root, "Chat", "local-models.json")));
    }

    [Fact]
    public async Task RepairingMissingWeightsPreservesTheLegacyEngine()
    {
        if (!ChatModelStore.Supported) return;
        var engine = LegacyEngine();
        var bytes = Fixture(7);
        var release = Gemma with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), FileBytes = bytes.Length };
        var requests = new Requests(bytes);
        using var http = new HttpClient(requests);
        var store = new ChatModelStore(_root, http);
        await store.InstallAsync(release, Progress, default);

        Assert.Equal(release.FileUrl, Assert.Single(requests.Urls));
        Assert.True(File.Exists(engine));
        Assert.True(store.Installed(release.Id));
        Assert.Equal(bytes, File.ReadAllBytes(store.ModelPath(release.Id)));
    }

    [Fact]
    public async Task LinkingUsesOriginalPathAndEngineAndCanReconnectMovedContent()
    {
        if (!ChatModelStore.Supported) return;
        var engine = LegacyEngine();
        var bytes = Fixture(1);
        var path = FileAt("my models/first.gguf", bytes);
        var network = new Requests();
        using var http = new HttpClient(network);
        var probed = new List<string>();
        var store = new ChatModelStore(_root, http, (exe, file, _) =>
        {
            Assert.Equal(engine, exe);
            probed.Add(file);
            return Task.CompletedTask;
        });
        var id = await store.LinkFileAsync(path, Progress, default);
        Assert.Equal("local:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), id);
        var restarted = new ChatModelStore(_root, http);
        Assert.True(restarted.Installed(id));
        Assert.Equal(path, Assert.Single(restarted.AvailableModels()).Path);
        var moved = Path.Combine(Path.GetDirectoryName(path)!, "renamed.GGUF");
        File.Move(path, moved);
        Assert.False(restarted.Installed(id));
        Assert.Empty(restarted.AvailableModels());
        Assert.Equal(id, await store.LinkFileAsync(moved, Progress, default));
        Assert.Equal(new[] { path, moved }, probed);
        Assert.Equal(moved, Assert.Single(store.AvailableModels()).Path);
        Assert.Equal(new[] { moved }, Directory.GetFiles(_root, "*.gguf", SearchOption.AllDirectories));
        Assert.Empty(network.Urls);
    }

    [Fact]
    public async Task FolderScanOnlyListsFilesUnderTheChosenFolderAndRegistersNothing()
    {
        var second = FileAt("chosen/sub/B.GGUF", []);
        var first = FileAt("chosen/A.gguf", []);
        FileAt("chosen/sub/skip.bin", []);
        FileAt("other/hidden.gguf", []);
        var folder = Path.Combine(_root, "chosen");
        Assert.Equal(new[] { first, second }, await ChatModelStore.ScanFolderAsync(folder, default));
        Assert.Empty(await ChatModelStore.ScanFolderAsync(Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ChatModelStore.ScanFolderAsync(folder, new(true)));
        Assert.False(Directory.Exists(Path.Combine(_root, "Chat")));
    }

    [Fact]
    public async Task InvalidFileFailedProbeAndFailedSaveLeaveOnlyPreviouslyLinkedFile()
    {
        if (!ChatModelStore.Supported) return;
        LegacyEngine();
        using var http = new HttpClient(new Requests());
        var good = FileAt("one.gguf", Fixture(1));
        var bad = FileAt("bad.gguf", new byte[28]);
        var incompatible = FileAt("two.gguf", Fixture(2));
        var store = new ChatModelStore(_root, http, (_, path, _) => path == incompatible
            ? Task.FromException(new IOException("Unsupported model")) : Task.CompletedTask);
        var initial = await store.LinkFileAsync(good, Progress, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LinkFileAsync(bad, Progress, default));
        await Assert.ThrowsAsync<IOException>(() => store.LinkFileAsync(incompatible, Progress, default));
        var third = FileAt("three.gguf", Fixture(3));
        Directory.CreateDirectory(Path.Combine(_root, "Chat", "local-models.json.tmp"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.LinkFileAsync(third, Progress, default));
        Assert.Equal(initial, Assert.Single(store.AvailableModels()).Id);
        Assert.Equal(initial, Assert.Single(new ChatModelStore(_root, http).AvailableModels()).Id);
    }

    [Fact]
    public async Task LinkWithMissingRuntimeRequestsOnlyEngineAndRejectsDamagedArchive()
    {
        if (!ChatModelStore.Supported) return;
        var path = FileAt("local.gguf", Fixture(1));
        var network = new Requests();
        using var http = new HttpClient(network);
        var store = new ChatModelStore(_root, http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LinkFileAsync(path, Progress, default));
        Assert.EndsWith(".zip", Assert.Single(network.Urls));
        Assert.Empty(store.AvailableModels());
        Assert.True(File.Exists(path));
    }
}
