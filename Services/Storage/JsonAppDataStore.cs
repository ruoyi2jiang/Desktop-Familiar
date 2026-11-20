using System.Security.Cryptography;
using System.Text.Json;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Each schema saves its own library. Other versions and the old shared file are import sources.</summary>
public sealed class JsonAppDataStore : IAppDataStore
{
    private readonly JsonSerializerOptions _options;
    private readonly string _directory;
    private readonly string _legacyPath;
    private byte[]? _unreadableContents;

    public JsonAppDataStore(string rootDirectory, JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.MakeReadOnly(populateMissingResolver: true);
        rootDirectory = Path.GetFullPath(rootDirectory);
        _directory = Path.Combine(rootDirectory, "Library");
        _legacyPath = Path.Combine(rootDirectory, "desktop-familiar.json");
        FilePath = Path.Combine(_directory, $"desktop-familiar.v{DesktopCompanionData.CurrentSchemaVersion}.json");
        // Even a user who never saved a configuration already has a Library directory.
        IsFirstUse = !Directory.Exists(_directory) && !File.Exists(_legacyPath);
        Directory.CreateDirectory(_directory);
    }

    public string FilePath { get; }
    public bool IsFirstUse { get; }
    public string? SourceFingerprint { get; private set; }
    public string ReadPath
    {
        get
        {
            if (File.Exists(FilePath)) return FilePath;
            return VersionedFiles().OrderByDescending(file => file.Version)
                .Select(file => file.Path).FirstOrDefault() ?? _legacyPath;
        }
    }

    private IEnumerable<(string Path, int Version)> VersionedFiles()
    {
        const string prefix = "desktop-familiar.v";
        foreach (var path in Directory.EnumerateFiles(_directory, prefix + "*.json"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(path)[prefix.Length..], out var version) && version > 0)
                yield return (path, version);
    }

    public IEnumerable<string> OtherLibraryAssetReferences(Guid deletedCompanionId)
    {
        var paths = VersionedFiles().Select(file => file.Path)
            .Where(path => !string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase));
        if (File.Exists(_legacyPath)) paths = paths.Append(_legacyPath);
        foreach (var path in paths)
        {
            using var input = File.OpenRead(path);
            using var document = JsonDocument.Parse(input);
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (!field.Name.Equals("Companions", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var pet in field.Value.EnumerateArray())
                {
                    var isDeletedPet = pet.ValueKind == JsonValueKind.Object && pet.EnumerateObject().Any(p =>
                        p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String
                        && p.Value.TryGetGuid(out var id) && id == deletedCompanionId);
                    if (isDeletedPet) continue;
                    foreach (var asset in AbsolutePaths(pet)) yield return asset;
                }
            }
        }
    }

    // Future versions can store references under fields this build does not understand.
    private static IEnumerable<string> AbsolutePaths(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String && element.GetString() is { } path && Path.IsPathFullyQualified(path))
            yield return path;
        else if (element.ValueKind == JsonValueKind.Object)
            foreach (var field in element.EnumerateObject())
                foreach (var child in AbsolutePaths(field.Value)) yield return child;
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var child in AbsolutePaths(item)) yield return child;
    }

    public DesktopCompanionData Read() => Read(ReadPath);

    public DesktopCompanionData Read(string path)
    {
        SourceFingerprint = null;
        var bytes = File.ReadAllBytes(path);
        try
        {
            using var input = new MemoryStream(bytes);
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;
            var data = GetVersion(root) > DesktopCompanionData.CurrentSchemaVersion
                ? CompatibleLibraryReader.Read(root, _options)
                : root.Deserialize<DesktopCompanionData>(_options)
                    ?? throw new InvalidDataException("The saved configuration is empty.");
            SourceFingerprint = Convert.ToHexString(SHA256.HashData(bytes));
            return data;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            if (path == FilePath) _unreadableContents = bytes;
            throw;
        }
    }

    private static int GetVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return 0;
        var version = 0;
        foreach (var field in root.EnumerateObject())
            if (field.Name.Equals(nameof(DesktopCompanionData.SchemaVersion), StringComparison.OrdinalIgnoreCase)
                && field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var number))
                version = Math.Max(version, number);
        return version;
    }

    public void Write(DesktopCompanionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        NewerLibraryVersionException.Check(data.SchemaVersion);
        var exists = File.Exists(FilePath);
        string? backupPath = null;
        if (exists)
        {
            var bytes = File.ReadAllBytes(FilePath);
            var recovering = _unreadableContents != null && bytes.AsSpan().SequenceEqual(_unreadableContents);
            try
            {
                using var input = new MemoryStream(bytes);
                using var existing = JsonDocument.Parse(input);
                NewerLibraryVersionException.Check(GetVersion(existing.RootElement));
            }
            catch (JsonException) when (recovering)
            {
                // The user is saving over the same unreadable file. File.Replace will archive it atomically.
            }
            if (recovering) backupPath = Path.Combine(_directory, $"desktop-familiar.invalid-{Guid.NewGuid():N}.json");
        }

        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, _options);
            File.WriteAllBytes(temporaryPath, bytes);
            if (exists) File.Replace(temporaryPath, FilePath, backupPath);
            else File.Move(temporaryPath, FilePath);
            _unreadableContents = null;
            SourceFingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

}
