using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Minimal persistence contract used by the application state manager.</summary>
public interface ICompanionRepository
{
    DesktopCompanionData Load();
    void Save(DesktopCompanionData data);
}
