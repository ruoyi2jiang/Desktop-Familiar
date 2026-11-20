using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

public interface IAppDataStore
{
    string FilePath { get; }
    string ReadPath { get; }
    string? SourceFingerprint { get; }
    IEnumerable<string> OtherLibraryAssetReferences(Guid deletedCompanionId);
    DesktopCompanionData Read(string path);
    void Write(DesktopCompanionData data);
}
