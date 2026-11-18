using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopFamiliar.Services.Storage;

public sealed record AvailableChatModel(string Id, string Name, string Path);

/// <summary>Owns references to user-selected files. The files themselves stay under user ownership.</summary>
internal sealed class LocalChatModelIndex
{
    private sealed record Document([property: JsonRequired] int Version, [property: JsonRequired] Dictionary<string, string> Files);
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string> _files;
    private readonly Exception? _loadError;

    public LocalChatModelIndex(string root)
    {
        _path = Path.Combine(root, "Chat", "local-models.json");
        _files = [];
        if (!File.Exists(_path)) return;
        try
        {
            using var input = File.OpenRead(_path);
            var doc = JsonSerializer.Deserialize<Document>(input);
            if (doc?.Version != 1 || doc.Files == null || doc.Files.Any(p =>
                !IsLocalId(p.Key) || string.IsNullOrEmpty(p.Value) || !Path.IsPathFullyQualified(p.Value)))
                throw new InvalidDataException("Invalid local chat model index.");
            _files = doc.Files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            AppLogger.Warning("Could not read local model references.", ex);
            _loadError = ex;
        }
    }

    private static bool IsLocalId(string id) => id.Length == 70 && id.StartsWith("local:", StringComparison.Ordinal)
        && id.AsSpan(6).IndexOfAnyExcept("0123456789abcdef") < 0;

    public KeyValuePair<string, string>[] Entries() { lock (_gate) return _files.ToArray(); }
    public string? Find(string id) { lock (_gate) return _files.GetValueOrDefault(id); }

    public void Link(string id, string path)
    {
        lock (_gate)
        {
            if (_loadError != null) throw new InvalidDataException("Cannot update unreadable local model index.", _loadError);
            var updated = new Dictionary<string, string>(_files);
            foreach (var entry in _files.Where(p => string.Equals(p.Value, path, StringComparison.OrdinalIgnoreCase)))
                updated.Remove(entry.Key);
            updated[id] = path;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(new Document(1, updated)));
                File.Move(temp, _path, overwrite: true);
                _files = updated;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public static string[] Scan(string folder, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseInsensitive
        };
        // Enumerate all entries so cancellation is observed even in folders without GGUF files.
        var files = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(Path.GetFullPath(folder), "*", options))
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetExtension(path).Equals(".gguf", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                files.Add(path);
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files.ToArray();
    }
}
