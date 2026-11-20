using System.Text.Json;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Calendar;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Records observed expirations without committing user configuration or migrations.</summary>
internal sealed class CalendarExpirationStore(string libraryDirectory)
{
    private readonly string _path = Path.Combine(libraryDirectory,
        $"calendar-expirations.v{DesktopCompanionData.CurrentSchemaVersion}.json");

    private readonly record struct Entry(Guid EventId, DateTime Occurrence);
    private sealed record Document(string SourceFingerprint, List<Entry> Entries);

    public void Apply(string fingerprint, DesktopCompanionData data)
    {
        var events = data.CalendarEvents.ToLookup(item => item.Id);
        List<Entry> entries;
        try { entries = Read(fingerprint); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning("The saved calendar expirations are unavailable.", exception);
            return;
        }
        foreach (var entry in entries)
            foreach (var item in events[entry.EventId])
                CalendarOccurrenceStatusTracker.MarkExpired(item, entry.Occurrence);
    }

    public void ClearAfterSave()
    {
        try { File.Delete(_path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The committed library includes these observations. A changed fingerprint also prevents stale replay.
            AppLogger.Warning("The committed calendar expiration record could not be removed.", exception);
        }
    }

    public void Record(string fingerprint, Guid eventId, DateTime occurrence)
    {
        var entries = Read(fingerprint);
        var entry = new Entry(eventId, occurrence);
        if (entries.Contains(entry)) return;
        if (occurrence.Year > 1)
            entries.RemoveAll(item => item.Occurrence.Date <= occurrence.Date.AddYears(-1));
        entries.Add(entry);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temp))
                JsonSerializer.Serialize(output, new Document(fingerprint, entries));
            File.Move(temp, _path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }

    private List<Entry> Read(string fingerprint)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            using var input = File.OpenRead(_path);
            var doc = JsonSerializer.Deserialize<Document>(input);
            return doc?.SourceFingerprint == fingerprint && doc.Entries is not null
                ? doc.Entries : [];
        }
        catch (JsonException exception)
        {
            AppLogger.Warning("The calendar expiration record could not be read.", exception);
            return [];
        }
    }
}
