using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services.Chat;

namespace DesktopFamiliar.Services.Storage;

public sealed record ChatModel(string Id, string NameKey, string FileUrl, string Sha256, int DownloadMb, string TermsUrl)
{
    public string Name => LocalizationService.Get(NameKey);
    public ChatModelRequirements? Requirements { get; init; }
}

public sealed class ChatModelStore(string root, HttpClient http,
    Func<string, string, CancellationToken, Task>? probeLocalFile = null)
{
    private const string EngineUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b12496/llama-b12496-bin-win-cpu-x64.zip";
    private const string EngineHash = "f02e029c0f4f0f9c8ea341ef47d0fd28191df0bcfa5ab4f40f4aa43942e943ea";
    public static IReadOnlyList<ChatModel> Catalogue { get; } = LoadCatalogue();
    private readonly string _directory = Path.Combine(root, "Chat", "Models");
    private readonly SemaphoreSlim _install = new(1);
    private readonly LocalChatModelIndex _local = new(root);
    private readonly string _sharedEngine = Path.Combine(root, "Chat", "Engines", "b12496");
    private readonly Func<string, string, CancellationToken, Task> _probe = probeLocalFile ?? LlamaChatEngine.ProbeAsync;

    private static IReadOnlyList<ChatModel> LoadCatalogue()
    {
        using var stream = typeof(ChatModelStore).Assembly.GetManifestResourceStream("DesktopFamiliar.Resources.chat-models.json")
            ?? throw new FileNotFoundException("Missing bundled chat model catalogue.");
        var models = JsonSerializer.Deserialize<ChatModel[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        static bool Https(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        if (models == null || models.DistinctBy(m => m.Id).Count() != models.Length || models.Any(m =>
            string.IsNullOrEmpty(m.Id) || m.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
            string.IsNullOrWhiteSpace(m.NameKey) || !Https(m.FileUrl) || !Https(m.TermsUrl) ||
            m.Sha256?.Length != 64 || !m.Sha256.All(Uri.IsHexDigit) || m.DownloadMb <= 0 ||
            m.Requirements is not { SuggestedFreeRamMb: > 0, SuggestedThreads: > 0, SetupSpaceMb: > 0 }))
            throw new InvalidDataException("Invalid bundled chat model catalogue.");
        return Array.AsReadOnly(models);
    }

    public static bool Supported => RuntimeInformation.OSArchitecture == Architecture.X64;
    internal static ChatModel? CatalogueEntry(string id) => Catalogue.FirstOrDefault(model =>
        model.Id == id || string.Equals("local:" + model.Sha256, id, StringComparison.OrdinalIgnoreCase));

    internal string DescribeRequirements(ChatModel? model, bool forDownload) =>
        ChatModelPc.Read(root, forDownload).Describe(model?.Requirements, forDownload);
    private static bool IsManaged(string id) => Catalogue.Any(model => model.Id == id);
    private string ManagedFile(string id) => Path.Combine(_directory, id, "model.gguf");
    private string LegacyEngine(string id) => Path.Combine(_directory, id, "engine", "llama-server.exe");

    internal string EnginePath(string id)
    {
        if (IsManaged(id) && File.Exists(LegacyEngine(id))) return LegacyEngine(id);
        var shared = Path.Combine(_sharedEngine, "llama-server.exe");
        if (File.Exists(shared)) return shared;
        return Catalogue.Select(m => LegacyEngine(m.Id)).FirstOrDefault(File.Exists) ?? shared;
    }

    public bool HasEngine => File.Exists(EnginePath(""));
    private string? FindModelPath(string id) => IsManaged(id) ? ManagedFile(id) : _local.Find(id);
    internal string ModelPath(string id) => FindModelPath(id) ?? throw new FileNotFoundException("Unknown local chat model.");

    public bool Installed(string? id) => id != null && File.Exists(FindModelPath(id)) && File.Exists(EnginePath(id));

    public string? AvailableIdFor(ChatModel model)
    {
        if (Installed(model.Id)) return model.Id;
        var linkedId = "local:" + model.Sha256.ToLowerInvariant();
        return Installed(linkedId) ? linkedId : null;
    }

    public IReadOnlyList<AvailableChatModel> AvailableModels()
    {
        var models = Catalogue.Where(m => Installed(m.Id)).Select(m => new AvailableChatModel(m.Id, m.Name, ManagedFile(m.Id))).ToList();
        foreach (var (id, path) in _local.Entries().Where(entry => Installed(entry.Key)))
        {
            var match = CatalogueEntry(id);
            models.Add(new(id, match?.Name ?? Path.GetFileName(path), path));
        }
        return models;
    }

    public static Task<string[]> ScanFolderAsync(string folder, CancellationToken token) =>
        Task.Run(() => LocalChatModelIndex.Scan(folder, token), token);

    public async Task<string> LinkFileAsync(string file, IProgress<long> progress, CancellationToken token)
    {
        if (!Supported) throw new PlatformNotSupportedException();
        var path = Path.GetFullPath(file);
        if (!Path.GetExtension(path).Equals(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected a GGUF file.");
        await _install.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            var header = new byte[4];
            if (input.Length < 24) throw new InvalidDataException("Incomplete GGUF header.");
            await input.ReadExactlyAsync(header, token).ConfigureAwait(false);
            if (!header.AsSpan().SequenceEqual("GGUF"u8)) throw new InvalidDataException("Invalid GGUF header.");
            input.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).ToLowerInvariant();
            var known = Catalogue.FirstOrDefault(m => m.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase));
            long downloaded = 0;
            var engine = await EnsureEngineAsync(bytes => progress.Report(downloaded += bytes), token).ConfigureAwait(false);
            if (known != null && string.Equals(path, ManagedFile(known.Id), StringComparison.OrdinalIgnoreCase))
                return known.Id;
            if (known == null) await _probe(engine, path, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var id = "local:" + hash;
            _local.Link(id, path);
            return id;
        }
        finally { _install.Release(); }
    }

    public Task InstallAsync(string id, IProgress<long> progress, CancellationToken token) =>
        InstallAsync(Catalogue.Single(m => m.Id == id), progress, token);

    internal async Task InstallAsync(ChatModel model, IProgress<long> progress, CancellationToken token)
    {
        if (!Supported) throw new PlatformNotSupportedException();
        await _install.WaitAsync(token);
        var stage = Path.Combine(_directory, ".install-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (Installed(model.Id)) return;
            Directory.CreateDirectory(stage);
            long total = 0;
            void Report(int bytes) { total += bytes; progress.Report(total); }
            await EnsureEngineAsync(Report, token).ConfigureAwait(false);
            await DownloadAsync(model.FileUrl, model.Sha256, Path.Combine(stage, "model.gguf"), Report, token);
            token.ThrowIfCancellationRequested();
            var destination = ManagedFile(model.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(Path.Combine(stage, "model.gguf"), destination, overwrite: true);
        }
        finally
        {
            try { RemoveStagingDirectory(stage); }
            finally { _install.Release(); }
        }
    }

    // Called under the installation lock. Existing per-model engines are also usable by linked files.
    private async Task<string> EnsureEngineAsync(Action<int> onBytes, CancellationToken token)
    {
        var existing = EnginePath("");
        if (File.Exists(existing)) return existing;
        var stage = _sharedEngine + ".install-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        try
        {
            var archive = Path.Combine(stage, "engine.zip");
            await DownloadAsync(EngineUrl, EngineHash, archive, onBytes, token).ConfigureAwait(false);
            var engine = Path.Combine(stage, "engine");
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, engine), token).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(engine, "llama-server.exe")))
                throw new InvalidDataException("Engine executable missing from release.");
            token.ThrowIfCancellationRequested();
            var previous = Path.Combine(stage, "previous");
            if (Directory.Exists(_sharedEngine)) Directory.Move(_sharedEngine, previous);
            try { Directory.Move(engine, _sharedEngine); }
            catch
            {
                if (Directory.Exists(previous)) Directory.Move(previous, _sharedEngine);
                throw;
            }
            return Path.Combine(_sharedEngine, "llama-server.exe");
        }
        finally { RemoveStagingDirectory(stage); }
    }

    private static void RemoveStagingDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning("Could not remove a temporary chat download.", ex);
        }
    }

    private async Task DownloadAsync(string url, string hash, string path, Action<int> progress, CancellationToken token)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var dest = File.Create(path);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[256 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            digest.AppendData(buffer, 0, count);
            progress(count);
        }
        if (!Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Download checksum mismatch.");
    }
}
