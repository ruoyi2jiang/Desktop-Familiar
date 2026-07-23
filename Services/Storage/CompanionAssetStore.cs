using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

/// <summary>Owns imported user assets and asset discovery.</summary>
public sealed class CompanionAssetStore
{
    public CompanionAssetStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string Import(Guid companionId, string sourcePath, string category)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(LocalizationService.Get("Storage.AssetMissing"), sourcePath);

        var directory = Path.Combine(RootDirectory, "Assets", companionId.ToString("N"), category);
        Directory.CreateDirectory(directory);
        var safeName = Path.GetFileName(sourcePath);
        var destination = Path.Combine(directory, safeName);
        if (File.Exists(destination)
            && !PathsEqual(sourcePath, destination))
        {
            destination = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(safeName)}-{Guid.NewGuid():N}{Path.GetExtension(safeName)}");
        }

        if (!PathsEqual(sourcePath, destination))
            File.Copy(sourcePath, destination, overwrite: false);
        return destination;
    }

    public IReadOnlyList<string> GetImportedBubbleImages(DesktopCompanionData data) => data.Companions
        .Select(companion => companion.BubbleImagePath)
        .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        .Cast<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(Path.GetFileName)
        .ToList();

    private static bool PathsEqual(string first, string second) => string.Equals(
        Path.GetFullPath(first),
        Path.GetFullPath(second),
        StringComparison.OrdinalIgnoreCase);
}
