namespace DesktopFamiliar.Models;

public sealed class DesktopCompanionData
{
    public const int CurrentSchemaVersion = 10;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public AppSettings Settings { get; set; } = new();

    public List<CompanionDefinition> Companions { get; set; } = [];

    public List<CalendarEvent> CalendarEvents { get; set; } = [];

    public DesktopCompanionData Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Settings = Settings.Clone(),
        Companions = Companions.Select(companion => companion.Clone()).ToList(),
        CalendarEvents = CalendarEvents.Select(calendarEvent => calendarEvent.Clone()).ToList()
    };

    public void RestoreFrom(DesktopCompanionData snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var restored = snapshot.Clone();
        SchemaVersion = restored.SchemaVersion;
        Settings = restored.Settings;
        Companions = restored.Companions;
        CalendarEvents = restored.CalendarEvents;
    }
}
