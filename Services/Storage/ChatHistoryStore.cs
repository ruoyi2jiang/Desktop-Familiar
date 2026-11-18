using System.Text.Json;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Each read returns a detached snapshot. A change is published only after atomic replacement.</summary>
public sealed class ChatHistoryStore(string root)
{
    private sealed class Document
    {
        [System.Text.Json.Serialization.JsonRequired]
        public int Version { get; set; } = 1;
        public Dictionary<Guid, ChatConversation> Pets { get; set; } = [];
    }

    private readonly string _path = Path.Combine(root, "Chat", "history.json");
    private readonly object _gate = new();
    public event EventHandler? Changed;

    private Document Read()
    {
        if (!File.Exists(_path))
            return new();
        using var file = File.OpenRead(_path);
        var doc = JsonSerializer.Deserialize<Document>(file);
        if (doc?.Version != 1 || doc.Pets is null || doc.Pets.Values.Any(p => p?.Turns is null || p.Notes is null))
            throw new InvalidDataException("Invalid chat history document.");
        return doc;
    }

    public ChatConversation ForPet(Guid id)
    {
        lock (_gate)
            return Read().Pets.GetValueOrDefault(id) ?? new();
    }

    public IReadOnlyDictionary<Guid, string> Names()
    {
        lock (_gate)
            return Read().Pets.ToDictionary(p => p.Key, p => p.Value.Name);
    }

    public void Append(Guid id, string name, ChatTurn turn, string? note)
    {
        Mutate(pets =>
        {
            if (!pets.TryGetValue(id, out var pet))
                pets[id] = pet = new();
            pet.Name = name;
            pet.Turns.Add(turn);
            if (!string.IsNullOrWhiteSpace(note) && !pet.Notes.Contains(note, StringComparer.OrdinalIgnoreCase))
                pet.Notes.Add(note);
        });
    }

    public void Clear(IEnumerable<Guid> ids, bool includeNotes)
    {
        Mutate(pets =>
        {
            foreach (var id in ids)
            {
                if (!pets.TryGetValue(id, out var pet))
                    continue;
                if (includeNotes || pet.Notes.Count == 0)
                    pets.Remove(id);
                else
                    pet.Turns.Clear();
            }
        });
    }

    private void Mutate(Action<Dictionary<Guid, ChatConversation>> change)
    {
        lock (_gate)
        {
            var document = Read();
            change(document.Pets);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            try
            {
                using (var output = File.Create(temp))
                    JsonSerializer.Serialize(output, document);
                File.Move(temp, _path, overwrite: true);
            }
            finally { File.Delete(temp); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
