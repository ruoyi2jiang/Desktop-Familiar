using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

internal sealed class NewerLibraryVersionException(int version) : NotSupportedException(
    $"Library schema {version} exceeds supported schema {DesktopCompanionData.CurrentSchemaVersion}.")
{
    public int Version { get; } = version;

    internal static void Check(int version)
    {
        if (version > DesktopCompanionData.CurrentSchemaVersion)
            throw new NewerLibraryVersionException(version);
    }
}
