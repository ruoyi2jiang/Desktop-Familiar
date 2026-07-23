using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

public interface IAppDataStore
{
    string FilePath { get; }
    bool Exists { get; }
    DesktopCompanionData Read();
    void Write(DesktopCompanionData data);
    string BackupInvalidFile(DateTime timestamp);
}
