using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DesktopFamiliar.Services.Chat;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class PublisherChatModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("df-publisher-").FullName;
    private const string Repo = "LiquidAI/LFM2.5-350M-GGUF";
    private const string FileName = "LFM2.5-350M-Q4_K_M.gguf";
    private static readonly string Revision = new('b', 40);
    private static readonly byte[] Weights = [.. "GGUF"u8.ToArray(), .. new byte[128]];
    private static readonly string Digest = Convert.ToHexString(SHA256.HashData(Weights)).ToLowerInvariant();
    private static readonly PublisherChatRelease Release = new(Repo, Revision, FileName, Digest, Weights.Length);
    private static readonly ChatModelPc Roomy = new(true, 4, 8_000_000_000, 5_000_000_000);

    [Fact]
    public void DownloadPickerRequiresConfirmedResourcesAndReusesLinkedWeightsWithNoSetupSpace()
    {
        var gemma = ChatModelStore.Catalogue[0];
        var path = Path.Combine(_root, "external.gguf");
        File.WriteAllBytes(path, Weights);
        var localId = "local:" + gemma.Sha256;
        new LocalChatModelIndex(_root).Link(localId, path);
        AddEngine();
        using var http = new HttpClient();
        var store = new ChatModelStore(_root, http);
        Assert.Equal(ChatModelStore.Catalogue.Count, store.DownloadChoices(Roomy).Count);
        Assert.Equal(gemma.Id, Assert.Single(store.DownloadChoices(Roomy with { FreeDisk = 0 })).Id);
        Assert.Equal(gemma.Id, Assert.Single(store.DownloadChoices(Roomy with { FreeDisk = null })).Id);
        Assert.Empty(store.DownloadChoices(Roomy with { FreeRam = null }));
        Assert.Empty(store.DownloadChoices(Roomy with { FreeRam = 1_000_000 }));
        Assert.Empty(store.DownloadChoices(Roomy with { Threads = 2 }));
        Assert.Empty(store.DownloadChoices(Roomy with { Supported = false }));
        Assert.Equal(localId, Assert.Single(store.AvailableModels()).Id);
        Assert.True(ChatModelPc.Read(Path.Combine(_root, "not-yet-created", "models"), true).FreeDisk > 0);
        Assert.False(Directory.Exists(Path.Combine(_root, "not-yet-created")));
    }

    [Theory]
    [InlineData("LiquidAI/LFM2.5-1.2B-JP-202606-GGUF", "Chat.Hub.Japanese")]
    [InlineData("Qwen/Qwen2.5-Coder-1.5B-Instruct-GGUF", "Chat.Hub.Coding")]
    [InlineData("LiquidAI/LFM2.5-230M-GGUF", "Chat.Hub.Liquid")]
    public void NewFamilySizesAndEditionsUsePublisherIdentityAndLocalizedSpecialty(string repo, string strength)
    {
        var name = repo.Split('/')[1][..^5];
        var model = (Release with { Repository = repo, FileName = name.ToLowerInvariant() + "-q4_k_m.gguf" }).ToModel();
        Assert.NotNull(model);
        Assert.Equal((name, strength), (model.Name, model.StrengthsKey));
        Assert.Null(model.SampleReplyTokens);
        Assert.Contains(Revision, model.FileUrl);
        Assert.True(model.Requirements.SetupSpaceMb * 1_000_000L > model.FileBytes);
    }

    [Fact]
    public void DiscoveryRejectsUnexpectedPublishersNonChatModelsAndUnpinnedOrSplitFiles()
    {
        foreach (var release in new[] {
            Release with { Repository = "Community/LFM2.5-350M-GGUF" }, Release with { Revision = "main" },
            Release with { FileName = "../" + FileName }, Release with { Digest = "not-a-digest" },
            Release with { FileName = "LFM2.5-350M-Q4_K_M-00001-of-00002.gguf" }, Release with { Bytes = 0 },
            Release with { Repository = "LiquidAI/LFM2.5-Embedding-350M-GGUF" },
            Release with { Repository = "LiquidAI/LFM2.5-1.2B-Base-GGUF" } })
            Assert.Null(release.ToModel());
    }

    [Fact]
    public async Task PartialPublisherOutageStillFindsPinnedWeightsAndDeduplicatesKnownFiles()
    {
        var bundled = ChatModelStore.Catalogue[0];
        using var http = new HttpClient(new Handler(req =>
        {
            var uri = req.RequestUri!;
            if (uri.Query.Contains("author=Qwen")) return new(HttpStatusCode.TooManyRequests);
            if (uri.Query.Contains("author=LiquidAI")) return Json(new[] { new { id = Repo, sha = Revision },
                new { id = "LiquidAI/LFM2.5-VL-3B-GGUF", sha = Revision } });
            Assert.Equal($"/api/models/{Repo}/tree/{Revision}", uri.AbsolutePath);
            return Json(new[] { Entry(Digest), Entry(bundled.Sha256), Entry(Digest, "../bad.gguf") });
        }));
        var store = new ChatModelStore(_root, http);
        var result = await store.DiscoverAsync(default);
        Assert.True(result.Incomplete);
        Assert.Equal(ChatModelStore.Catalogue.Count + 1, store.DownloadChoices(Roomy).Count);
        Assert.Equal("LFM2.5-350M", store.FindEntry("local:" + Digest)!.Name);
        Assert.Empty(store.DownloadChoices(Roomy with { FreeRam = 1 }));
        Assert.False(Directory.Exists(Path.Combine(_root, "Chat")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DiscoverAsync(new(true)));
        Assert.Equal(ChatModelStore.Catalogue.Count + 1, store.DownloadChoices(Roomy).Count);
    }

    [Fact]
    public async Task PublisherInstallSurvivesOfflineRestartAndReusesAnExistingInstallation()
    {
        AddEngine();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Weights) }));
        var probed = 0;
        var store = new ChatModelStore(_root, http, (_, file, _) =>
        {
            Assert.Equal(Weights, File.ReadAllBytes(file));
            probed++;
            return Task.CompletedTask;
        });
        var model = Release.ToModel()!;
        await store.InstallAsync(model, new Progress<long>(), default);
        var restarted = new ChatModelStore(_root, http);
        Assert.Equal(model, restarted.FindEntry(model.Id));
        Assert.Equal(model.Name, Assert.Single(restarted.AvailableModels()).Name);
        Assert.Equal(model.Id, restarted.AvailableIdFor(model));
        Assert.Equal(model.Id, await restarted.LinkFileAsync(restarted.ModelPath(model.Id), new Progress<long>(), default));
        Assert.Single(restarted.DownloadChoices(Roomy with { FreeDisk = 0 }));
        await restarted.InstallAsync(model, new Progress<long>(), default);
        Assert.Equal(1, probed);
        Assert.False(File.Exists(Path.Combine(_root, "Chat", "local-models.json")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("{}")]
    public async Task DamagedPublisherReceiptCanBeReinstalledOrLinked(string? damagedReceipt)
    {
        AddEngine();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Weights) }));
        var store = new ChatModelStore(_root, http, (_, _, _) => Task.CompletedTask);
        var model = Release.ToModel()!;
        var progress = new Progress<long>();
        await store.InstallAsync(model, progress, default);
        var weights = store.ModelPath(model.Id);
        var receipt = Path.Combine(Path.GetDirectoryName(weights)!, "publisher.json");
        void DamageReceipt()
        {
            if (damagedReceipt == null) File.Delete(receipt);
            else File.WriteAllText(receipt, damagedReceipt);
        }

        DamageReceipt();
        Assert.False(store.Installed(model.Id));
        Assert.Null(store.AvailableIdFor(model));
        Assert.Empty(store.AvailableModels());
        await store.InstallAsync(model, progress, default);
        Assert.Equal(model.Id, Assert.Single(new ChatModelStore(_root, http).AvailableModels()).Id);

        DamageReceipt();
        var linked = await store.LinkFileAsync(weights, progress, default);
        Assert.Equal("local:" + Digest, linked);
        Assert.Equal(linked, Assert.Single(new ChatModelStore(_root, http).AvailableModels()).Id);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("size")]
    [InlineData("probe")]
    [InlineData("cancel")]
    public async Task FailedPublisherInstallKeepsPreviousLibraryAndNoPartialWeights(string failure)
    {
        AddEngine();
        using var cancel = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Weights) }));
        var store = new ChatModelStore(_root, http, (_, _, ct) =>
        {
            if (failure == "cancel") cancel.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidDataException("No visible chat reply.");
        });
        var release = failure switch { "digest" => Release with { Digest = new('e', 64) },
            "size" => Release with { Bytes = 2 }, _ => Release };
        var operation = () => store.InstallAsync(release.ToModel()!, new Progress<long>(), cancel.Token);
        if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(operation);
        else await Assert.ThrowsAsync<InvalidDataException>(operation);
        Assert.True(store.HasEngine);
        Assert.Empty(store.AvailableModels());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_root, "Chat", "Models")));
    }

    private static object Entry(string digest, string file = FileName) => new {
        path = file, size = Weights.Length, lfs = new { oid = digest, size = Weights.Length }
    };
    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
    private void AddEngine()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "Chat", "Engines", "b12496"));
        File.WriteAllBytes(Path.Combine(dir.FullName, "llama-server.exe"), []);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
