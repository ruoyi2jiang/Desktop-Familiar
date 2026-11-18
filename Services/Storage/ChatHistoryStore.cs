using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

public sealed record ChatHistoryPetSummary(string Name, ArchivedPetPortrait? Portrait, long SizeBytes);

/// <summary>Each read returns a detached snapshot. A change is published only after atomic replacement.</summary>
public sealed class ChatHistoryStore(string root)
{
    private sealed class Document<TPet>
    {
        [JsonRequired]
        public int Version { get; set; } = 2;
        public Dictionary<Guid, TPet> Pets { get; set; } = [];
    }

    // Measure the actual stored UTF-8 record, including escapes and any legacy formatting.
    private sealed class SummaryReader : JsonConverter<ChatHistoryPetSummary>
    {
        public override bool HandleNull => true;

        public override ChatHistoryPetSummary Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var start = reader.TokenStartIndex;
            var pet = Validate(JsonSerializer.Deserialize<ChatConversation>(ref reader));
            return new(pet.Name, pet.ArchivedPortrait, reader.BytesConsumed - start);
        }

        public override void Write(Utf8JsonWriter writer, ChatHistoryPetSummary value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Chat history summaries are read-only.");
    }

    private static readonly JsonSerializerOptions SummaryOptions = new() { Converters = { new SummaryReader() } };
    private readonly string _path = Path.Combine(root, "Chat", "history.json");
    private readonly object _gate = new();
    public event EventHandler? Changed;

    private Document<TPet> ReadDocument<TPet>(JsonSerializerOptions? options = null)
    {
        if (!File.Exists(_path))
            return new();
        using var file = File.OpenRead(_path);
        var doc = JsonSerializer.Deserialize<Document<TPet>>(file, options);
        if (doc?.Version is not (1 or 2) || doc.Pets is null)
            throw new InvalidDataException("Invalid chat history document.");
        return doc;
    }

    private static ChatConversation Validate(ChatConversation? pet) =>
        pet?.Turns != null && pet.Notes != null ? pet : throw new InvalidDataException("Invalid chat history entry.");

    private Document<ChatConversation> Read()
    {
        var doc = ReadDocument<ChatConversation>();
        foreach (var pet in doc.Pets.Values)
        {
            Validate(pet);
            if (doc.Version == 1) pet.ArchivedPortrait = null;
        }
        doc.Version = 2;
        return doc;
    }

    public ChatConversation ForPet(Guid id)
    {
        lock (_gate)
            return Read().Pets.GetValueOrDefault(id) ?? new();
    }

    public IReadOnlyDictionary<Guid, ChatHistoryPetSummary> ListPets()
    {
        lock (_gate)
        {
            var doc = ReadDocument<ChatHistoryPetSummary>(SummaryOptions);
            return doc.Version == 1
                ? doc.Pets.ToDictionary(p => p.Key, p => p.Value with { Portrait = null })
                : doc.Pets;
        }
    }

    public long GetTotalSizeBytes()
    {
        lock (_gate)
        {
            try { return new FileInfo(_path).Length; }
            catch (FileNotFoundException) { return 0; }
            catch (DirectoryNotFoundException) { return 0; }
        }
    }

    public void RememberArchivedAppearance(CompanionDefinition companion)
    {
        Mutate(pets =>
        {
            if (!pets.TryGetValue(companion.Id, out var saved)) return false;
            saved.Name = companion.Name;
            saved.ArchivedPortrait = new(companion.ProfilePicturePath, companion.IdleDefaultAnimation?.GetPreviewPath());
            return true;
        });
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
            return true;
        });
    }

    public void ClearAll()
    {
        lock (_gate)
        {
            try
            {
                File.Delete(_path + ".tmp");
                File.Delete(_path);
            }
            catch (DirectoryNotFoundException) { /* No chat directory yet. */ }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear(IEnumerable<Guid> ids, ChatHistoryClearScope scope)
    {
        Mutate(pets =>
        {
            bool changed = false;
            foreach (var id in ids)
            {
                if (!pets.TryGetValue(id, out var pet))
                    continue;
                if ((scope is ChatHistoryClearScope.Messages or ChatHistoryClearScope.MessagesAndNotes) && pet.Turns.Count > 0)
                {
                    pet.Turns.Clear();
                    changed = true;
                }
                if ((scope is ChatHistoryClearScope.Notes or ChatHistoryClearScope.MessagesAndNotes) && pet.Notes.Count > 0)
                {
                    pet.Notes.Clear();
                    changed = true;
                }
                if (pet.Turns.Count == 0 && pet.Notes.Count == 0)
                {
                    pets.Remove(id);
                    changed = true;
                }
            }
            return changed;
        });
    }

    private void Mutate(Func<Dictionary<Guid, ChatConversation>, bool> change)
    {
        lock (_gate)
        {
            var document = Read();
            if (!change(document.Pets)) return;
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
