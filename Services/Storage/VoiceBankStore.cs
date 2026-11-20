using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services.Voice;

namespace DesktopFamiliar.Services.Storage;

internal sealed record SpeechAsset(string Id, string Url, long Size, string Sha256, string Entry,
    string? NameKey = null, string? Language = null, string? Terms = null);
internal sealed record WindowsVoiceSource(string NameKey, string[] Categories);
internal sealed record VoiceCatalogue(WindowsVoiceSource Windows, string[] Languages, SpeechAsset Recognition,
    SpeechAsset RecognitionEngine, SpeechAsset VoiceEngine, SpeechAsset[] Banks);
internal sealed record LocalVoiceBank(string Id, string Name, string Model, int Speakers);

internal sealed class VoiceBankStore(string root, HttpClient http)
{
    private readonly string _root = Path.Combine(root, "Voice");
    private readonly SemaphoreSlim _install = new(1);
    internal static VoiceCatalogue Catalogue { get; } = ReadCatalogue();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static VoiceCatalogue ReadCatalogue()
    {
        using var stream = typeof(VoiceBankStore).Assembly.GetManifestResourceStream("DesktopFamiliar.Resources.voice-banks.json")
            ?? throw new FileNotFoundException("Missing embedded speech catalogue.");
        var catalogue = JsonSerializer.Deserialize<VoiceCatalogue>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Empty speech catalogue.");
        var assets = catalogue.Banks.Append(catalogue.Recognition).Append(catalogue.RecognitionEngine).Append(catalogue.VoiceEngine).ToArray();
        if (assets.DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Count() != assets.Length || assets.Any(a =>
            string.IsNullOrEmpty(a.Id) || a.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.') ||
            a.Id is "." or ".." || !Uri.TryCreate(a.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            a.Size <= 0 || a.Sha256.Length != 64 || !a.Sha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(a.Entry) ||
            Path.IsPathRooted(a.Entry) || a.Entry.Split('/', '\\').Any(p => p is "." or "..")))
            throw new InvalidDataException("Invalid speech catalogue entry.");
        return catalogue;
    }

    internal string PathFor(SpeechAsset asset) => Path.Combine(_root, asset.Id, asset.Entry);
    internal bool HasRecognition => File.Exists(PathFor(Catalogue.Recognition)) && File.Exists(PathFor(Catalogue.RecognitionEngine));
    internal bool HasVoiceEngine => File.Exists(PathFor(Catalogue.VoiceEngine));

    public IReadOnlyList<LocalVoiceBank> Installed()
    {
        var banks = new List<LocalVoiceBank>();
        foreach (var asset in Catalogue.Banks)
            if (ReadInstalledBank(asset) is { } bank) banks.Add(bank);
        if (Directory.Exists(_root))
            foreach (var path in Directory.GetDirectories(_root, "local-*").Order())
            {
                var info = Path.Combine(path, "bank.json");
                if (!File.Exists(info)) continue;
                var model = FindModel(path);
                if (model == null) continue;
                try { banks.Add(Describe(Path.GetFileName(path), JsonSerializer.Deserialize<string>(File.ReadAllText(info)) ?? "", model)); }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
                { AppLogger.Warning("An imported voice bank is unavailable.", ex); }
            }
        return banks;
    }

    private LocalVoiceBank? ReadInstalledBank(SpeechAsset asset)
    {
        var model = PathFor(asset);
        if (!File.Exists(model)) return null;
        try { return Describe(asset.Id, LocalizationService.Get(asset.NameKey!), model); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            AppLogger.Warning("An installed voice bank is incomplete.", ex);
            return null;
        }
    }

    private static string? FindModel(string dir) => new[] { "model.int8.onnx", "model.onnx" }
        .Select(name => Path.Combine(dir, name)).FirstOrDefault(File.Exists);
    private static LocalVoiceBank Describe(string id, string name, string model)
    {
        var dir = Path.GetDirectoryName(model)!;
        if (!File.Exists(Path.Combine(dir, "tokens.txt")) || !Directory.Exists(Path.Combine(dir, "espeak-ng-data")))
            throw new InvalidDataException("Incomplete Kokoro bank.");
        var size = new FileInfo(Path.Combine(dir, "voices.bin")).Length;
        // sherpa Kokoro banks contain 511 style vectors of 256 float32 values per speaker.
        const int speakerBytes = 511 * 256 * sizeof(float);
        if (size <= 0 || size % speakerBytes != 0 || size / speakerBytes > 1024)
            throw new InvalidDataException("Invalid Kokoro speaker table.");
        return new(id, name, model, (int)(size / speakerBytes));
    }

    public Task InstallRecognitionAsync(IProgress<int>? progress, CancellationToken token) =>
        InstallAsync([Catalogue.RecognitionEngine, Catalogue.Recognition], progress, token);
    public Task InstallBankAsync(SpeechAsset bank, IProgress<int>? progress, CancellationToken token) =>
        InstallAsync([Catalogue.VoiceEngine, bank], progress, token);
    public Task InstallEngineAsync(IProgress<int>? progress, CancellationToken token) =>
        InstallAsync([Catalogue.VoiceEngine], progress, token);

    private async Task InstallAsync(SpeechAsset[] assets, IProgress<int>? progress, CancellationToken token)
    {
        if (!Environment.Is64BitProcess || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new PlatformNotSupportedException();
        await _install.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_root);
            var missing = assets.Where(a => Catalogue.Banks.Contains(a) ? ReadInstalledBank(a) == null : !File.Exists(PathFor(a))).ToArray();
            long completed = 0, total = missing.Sum(a => a.Size);
            foreach (var asset in missing)
            {
                var stage = Path.Combine(_root, ".install-" + Guid.NewGuid().ToString("N"));
                var payload = stage + ".download";
                Directory.CreateDirectory(stage);
                try
                {
                    using var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await using (var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false))
                    await using (var file = File.Create(payload))
                    using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        var buffer = new byte[81920];
                        long bytes = 0;
                        int count, lastPercent = -1;
                        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                        {
                            bytes += count;
                            if (bytes > asset.Size) throw new InvalidDataException("Unexpected speech download size.");
                            hash.AppendData(buffer, 0, count);
                            await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                            var percent = (int)(100 * (completed + bytes) / total);
                            if (percent != lastPercent) progress?.Report(lastPercent = percent);
                        }
                        if (bytes != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Speech download did not match its catalogue digest.");
                    }
                    if (asset.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        ZipFile.ExtractToDirectory(payload, stage);
                    else if (asset.Url.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase))
                        await SpeechProcess.RunAsync(Path.Combine(Environment.SystemDirectory, "tar.exe"),
                            ["-xf", payload, "-C", stage], token).ConfigureAwait(false);
                    else File.Move(payload, Path.Combine(stage, asset.Entry));
                    if (!File.Exists(Path.Combine(stage, asset.Entry))) throw new InvalidDataException("Missing package entry.");
                    token.ThrowIfCancellationRequested();
                    if (Catalogue.Banks.Contains(asset))
                        Describe(asset.Id, asset.Id, Path.Combine(stage, asset.Entry));
                    ActivateStagedPackage(stage, Path.Combine(_root, asset.Id));
                    completed += asset.Size;
                }
                finally { File.Delete(payload); if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }
        }
        finally { _install.Release(); }
    }

    public Task<LocalVoiceBank> ImportAsync(string folder, CancellationToken token) => Task.Run(async () =>
    {
        await _install.WaitAsync(token).ConfigureAwait(false);
        try { return await CopyBankAsync(folder, token).ConfigureAwait(false); }
        finally { _install.Release(); }
    }, token);

    private async Task<LocalVoiceBank> CopyBankAsync(string folder, CancellationToken token)
    {
        var model = FindModel(folder) ?? throw new InvalidDataException("Missing Kokoro model.");
        var bank = Describe("", Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)), model);
        Directory.CreateDirectory(_root);
        var stage = Path.Combine(_root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var files = new List<string> { model, Path.Combine(folder, "tokens.txt"), Path.Combine(folder, "voices.bin") };
            void AddData(string dir, int depth)
            {
                if (depth > 12 || File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Unsupported bank directory.");
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    token.ThrowIfCancellationRequested();
                    var flags = File.GetAttributes(entry);
                    if (flags.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked bank entries are unsupported.");
                    if (flags.HasFlag(FileAttributes.Directory)) AddData(entry, depth + 1);
                    else files.Add(entry);
                    if (files.Count > 5000) throw new InvalidDataException("Voice bank is too large.");
                }
            }
            AddData(Path.Combine(folder, "espeak-ng-data"), 0);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long bytes = 0;
            var buffer = new byte[81920];
            foreach (var path in files.Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked bank file.");
                var relative = Path.GetRelativePath(folder, path);
                var dest = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var input = File.OpenRead(path);
                bytes += input.Length;
                if (bytes > 1L << 30) throw new InvalidDataException("Voice bank exceeds its size limit.");
                using var output = File.Create(dest);
                using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    fileHash.AppendData(buffer, 0, read);
                }
                digest.AppendData(Encoding.UTF8.GetBytes(relative.ToLowerInvariant() + "\0"));
                digest.AppendData(fileHash.GetHashAndReset());
            }
            var id = "local-" + Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            var destDir = Path.Combine(_root, id);
            File.WriteAllText(Path.Combine(stage, "bank.json"), JsonSerializer.Serialize(bank.Name, Json));
            token.ThrowIfCancellationRequested();
            var installedModel = Path.Combine(destDir, Path.GetFileName(model));
            if (File.Exists(installedModel) && File.Exists(Path.Combine(destDir, "bank.json")))
            {
                try
                {
                    var name = JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(destDir, "bank.json")));
                    return Describe(id, name ?? bank.Name, installedModel);
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or JsonException)
                { AppLogger.Warning("Replacing incomplete imported voice data.", ex); }
            }
            var stagedBank = Describe(id, bank.Name, Path.Combine(stage, Path.GetFileName(model)));
            if (HasVoiceEngine)
            {
                var sample = await KokoroSpeech.RenderAsync(PathFor(Catalogue.VoiceEngine), stagedBank, "Hello.", new(), token).ConfigureAwait(false);
                if (sample.Length <= 44) throw new InvalidDataException("The imported bank produced no speech.");
            }
            token.ThrowIfCancellationRequested();
            ActivateStagedPackage(stage, destDir);
            return stagedBank with { Model = installedModel };
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static void ActivateStagedPackage(string stage, string destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.Move(stage, destination);
            return;
        }
        var previous = stage + ".previous";
        Directory.Move(destination, previous);
        try { Directory.Move(stage, destination); }
        catch { Directory.Move(previous, destination); throw; }
        try { Directory.Delete(previous, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLogger.Warning("Could not remove the previous speech installation after replacement.", ex); }
    }
}
