using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Minimal persistence contract used by the application state manager.</summary>
public interface ICompanionRepository
{
    bool RequiresExplicitSave => false;
    DesktopCompanionData Load();
    void Save(DesktopCompanionData data);
    void RecordCalendarExpiration(Guid eventId, DateTime occurrence) { }
}
