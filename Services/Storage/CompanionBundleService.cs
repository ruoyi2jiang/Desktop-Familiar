using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;
using DesktopFamiliar.Services.Validation;

namespace DesktopFamiliar.Services.Storage;

public sealed record CompanionBundlePreview(string Name, Guid CompanionId, int AnimationCount);
public sealed record CompanionBundleImportResult(CompanionDefinition Companion, string AssetDirectory);

/// <summary>
/// Reads and writes self-contained companion archives. Bundle manifests always use
/// relative asset paths; imports extract only referenced files into a unique
/// local asset directory and reject traversal, duplicate entries, and zip bombs.
/// </summary>
public sealed class CompanionBundleService
{
    public const string FileExtension = ".companionpack";
    public const string FormatIdentifier = "DesktopCompanion.Bundle";
    public const int CurrentFormatVersion = 6;

    private const string ManifestEntryName = "manifest.json";
    private const int MaximumEntryCount = 5000;
    private const long MaximumManifestBytes = 1024 * 1024;
    private const long MaximumAssetBytes = 512L * 1024 * 1024;
    private const long MaximumTotalAssetBytes = 1024L * 1024 * 1024;

    private readonly string _rootDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public CompanionBundleService(string rootDirectory, JsonSerializerOptions jsonOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _jsonOptions = new JsonSerializerOptions(jsonOptions);
        _jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }

    public void Export(CompanionDefinition companion, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(companion);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var errors = CompanionValidator.Validate(companion);
        if (errors.Count > 0)
            throw new InvalidDataException(LocalizationService.Format("CompanionBundle.InvalidCompanion", string.Join(" ", errors)));

        var portableCompanion = companion.Clone();
        var assets = MakeAssetsPortable(portableCompanion);
        var manifest = new CompanionBundleManifest
        {
            FormatVersion = portableCompanion.Voice != new CompanionVoice() ? CurrentFormatVersion
                : companion.ChatExpressions.Count > 0 || companion.Model3D.ChatExpressions.Count > 0 ? 5
                : companion.Appearance == CompanionAppearance.Images2D && companion.Model3D.FilePath is null
                    && companion.Model3D.Animations.Count == 0 ? 1 : 4,
            BundleId = $"companion.{companion.Id:N}",
            BundleVersion = 1,
            Companion = portableCompanion
        };

        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporaryPath = fullDestination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
            {
                WriteManifest(archive, manifest);
                foreach (var asset in assets)
                {
                    var entry = archive.CreateEntry(asset.EntryName, CompressionLevel.Optimal);
                    using var input = new FileStream(asset.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var output = entry.Open();
                    input.CopyTo(output);
                }
            }

            if (File.Exists(fullDestination))
                File.Replace(temporaryPath, fullDestination, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, fullDestination);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public CompanionBundlePreview Inspect(string bundlePath)
    {
        using var archive = OpenArchive(bundlePath);
        var manifest = ReadAndValidateManifest(archive);
        var companion = manifest.Companion!;
        return new CompanionBundlePreview(
            companion.Name,
            companion.Id,
            ModelAnimationCatalog.VisibleScenes(companion.ActiveScenes, showParts: false).Count());
    }

    public CompanionBundleImportResult Import(
        string bundlePath,
        Guid destinationCompanionId,
        string? importedName = null)
    {
        using var archive = OpenArchive(bundlePath);
        var manifest = ReadAndValidateManifest(archive);
        var entries = BuildEntryMap(archive);
        var imported = manifest.Companion!.Clone();
        imported.Id = destinationCompanionId;
        if (!string.IsNullOrWhiteSpace(importedName))
            imported.Name = importedName.Trim();

        var importId = Guid.NewGuid().ToString("N");
        var assetParent = Path.Combine(_rootDirectory, "Assets");
        var stagingRoot = Path.Combine(assetParent, $".bundle-import-{importId}");
        var finalRoot = Path.Combine(
            assetParent,
            destinationCompanionId.ToString("N"),
            "Bundles",
            importId);
        Directory.CreateDirectory(assetParent);

        var finalCreated = false;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var extractedPaths = ExtractReferencedAssets(imported, entries, stagingRoot, finalRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(finalRoot)!);
            Directory.Move(stagingRoot, finalRoot);
            finalCreated = true;
            ApplyImportedAssetPaths(imported, extractedPaths);

            var errors = CompanionValidator.Validate(imported);
            if (errors.Count > 0)
                throw new InvalidDataException(LocalizationService.Format("CompanionBundle.InvalidCompanion", string.Join(" ", errors)));
            return new CompanionBundleImportResult(imported, finalRoot);
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);
            if (finalCreated)
                TryDeleteDirectory(finalRoot);
            throw;
        }
    }

    public void DiscardImport(CompanionBundleImportResult importResult)
    {
        ArgumentNullException.ThrowIfNull(importResult);
        var importDirectory = Path.GetFullPath(importResult.AssetDirectory);
        var expectedBundleRoot = Path.GetFullPath(Path.Combine(
                _rootDirectory,
                "Assets",
                importResult.Companion.Id.ToString("N"),
                "Bundles"))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!importDirectory.StartsWith(expectedBundleRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only a bundle import directory can be discarded.");
        TryDeleteDirectory(importDirectory);
    }

    private IReadOnlyList<BundleAsset> MakeAssetsPortable(CompanionDefinition companion)
    {
        var assets = new List<BundleAsset>();
        var mappedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? Register(string? sourcePath, string category)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return null;

            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullSourcePath))
                throw new FileNotFoundException(LocalizationService.Format("CompanionBundle.AssetMissing", Path.GetFileName(sourcePath)), fullSourcePath);
            if (mappedPaths.TryGetValue(fullSourcePath, out var existingEntry))
                return existingEntry;

            var safeName = SanitizeFileName(Path.GetFileName(fullSourcePath));
            var entryName = $"assets/{category}/{assets.Count + 1:D4}-{safeName}";
            mappedPaths[fullSourcePath] = entryName;
            assets.Add(new BundleAsset(fullSourcePath, entryName));
            return entryName;
        }

        companion.Model3D.FilePath = Register(companion.Model3D.FilePath, "models");
        companion.ImportedFontPath = Register(companion.ImportedFontPath, "fonts");
        companion.ProfilePicturePath = Register(companion.ProfilePicturePath, "portraits");
        companion.BubbleImagePath = Register(companion.BubbleImagePath, "bubbles");
        foreach (var scene in companion.Animations.Concat(companion.Model3D.Animations))
        {
            scene.AnimationPath = Register(scene.AnimationPath, "animations") ?? string.Empty;
            foreach (var frame in scene.PngFrames)
                frame.FilePath = Register(frame.FilePath, "frames") ?? string.Empty;
            foreach (var line in scene.DialogLines)
                line.VoicePath = Register(line.VoicePath, "voices");
        }
        return assets;
    }

    private Dictionary<string, string> ExtractReferencedAssets(
        CompanionDefinition companion,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string stagingRoot,
        string finalRoot)
    {
        var referencedPaths = companion.GetAssetPaths()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeAssetEntryName(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        long totalBytes = 0;
        var extractedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entryName in referencedPaths)
        {
            if (!entries.TryGetValue(entryName, out var entry))
                throw new InvalidDataException(LocalizationService.Format("CompanionBundle.AssetMissing", entryName));
            if (entry.Length > MaximumAssetBytes)
                throw new InvalidDataException(LocalizationService.Get("CompanionBundle.TooLarge"));
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumTotalAssetBytes)
                throw new InvalidDataException(LocalizationService.Get("CompanionBundle.TooLarge"));

            var relativeAssetPath = entryName["assets/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var stagingPath = EnsureUnderRoot(stagingRoot, relativeAssetPath);
            var finalPath = EnsureUnderRoot(finalRoot, relativeAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            using var input = entry.Open();
            using var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            extractedPaths[entryName] = finalPath;
        }
        return extractedPaths;
    }

    private static void ApplyImportedAssetPaths(
        CompanionDefinition companion,
        IReadOnlyDictionary<string, string> extractedPaths)
    {
        string? Resolve(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var normalized = NormalizeAssetEntryName(path);
            return extractedPaths.TryGetValue(normalized, out var extracted)
                ? extracted
                : throw new InvalidDataException(LocalizationService.Format("CompanionBundle.AssetMissing", normalized));
        }

        companion.Model3D.FilePath = Resolve(companion.Model3D.FilePath);
        companion.ImportedFontPath = Resolve(companion.ImportedFontPath);
        companion.ProfilePicturePath = Resolve(companion.ProfilePicturePath);
        companion.BubbleImagePath = Resolve(companion.BubbleImagePath);
        foreach (var scene in companion.Animations.Concat(companion.Model3D.Animations))
        {
            scene.AnimationPath = Resolve(scene.AnimationPath) ?? string.Empty;
            foreach (var frame in scene.PngFrames)
                frame.FilePath = Resolve(frame.FilePath) ?? string.Empty;
            foreach (var line in scene.DialogLines)
                line.VoicePath = Resolve(line.VoicePath);
        }
    }

    private CompanionBundleManifest ReadAndValidateManifest(ZipArchive archive)
    {
        var entries = BuildEntryMap(archive);
        if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry)
            || manifestEntry.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.Invalid"));
        }

        CompanionBundleManifest manifest;
        try
        {
            using var stream = manifestEntry.Open();
            manifest = JsonSerializer.Deserialize<CompanionBundleManifest>(stream, _jsonOptions)
                ?? throw new InvalidDataException(LocalizationService.Get("CompanionBundle.Invalid"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.Invalid"), exception);
        }

        if (!string.Equals(manifest.Format, FormatIdentifier, StringComparison.Ordinal)
            || manifest.FormatVersion is < 1 or > CurrentFormatVersion
            || manifest.Companion is not { } companion
            || string.IsNullOrWhiteSpace(companion.Name)
            || companion.Model3D is null || !Enum.IsDefined(companion.Appearance)
            || (manifest.FormatVersion == 1 && (companion.Appearance != CompanionAppearance.Images2D || companion.Model3D.FilePath is not null))
            || !HasRequiredCollections(companion) || !HasRequiredCollections(companion.Model3D))
        {
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.Unsupported"));
        }

        if (manifest.FormatVersion == 2) CompanionDataNormalizer.MigrateModelClips(companion.Model3D);
        foreach (var assetPath in companion.GetAssetPaths().Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var normalized = NormalizeAssetEntryName(assetPath!);
            if (!entries.ContainsKey(normalized))
                throw new InvalidDataException(LocalizationService.Format("CompanionBundle.AssetMissing", normalized));
        }
        return manifest;
    }

    private static bool HasRequiredCollections(CompanionScenes companion)
    {
        var sceneIds = new HashSet<Guid>();
        return companion.Animations is not null
            && companion.ChatExpressions is not null
            && companion.IdleRandomAnimationIds is not null
            && companion.InteractionStates?.ClickAreas?.Any(area => area == null) != true
            && companion.Animations.All(scene =>
                scene is not null && sceneIds.Add(scene.Id)
                && scene.PngFrames is not null && scene.PngFrames.All(frame => frame != null)
                && scene.DialogLines is not null && scene.DialogLines.All(line => line != null)
                && (scene.ModelParts == null || scene.ModelParts.All(name => !string.IsNullOrWhiteSpace(name))));
    }

    private static IReadOnlyDictionary<string, ZipArchiveEntry> BuildEntryMap(ZipArchive archive)
    {
        if (archive.Entries.Count > MaximumEntryCount)
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.TooLarge"));

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            var name = NormalizeArchiveEntryName(entry.FullName);
            if (!entries.TryAdd(name, entry))
                throw new InvalidDataException(LocalizationService.Get("CompanionBundle.DuplicateEntry"));
        }
        return entries;
    }

    private void WriteManifest(ZipArchive archive, CompanionBundleManifest manifest)
    {
        var entry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, _jsonOptions);
    }

    private static ZipArchive OpenArchive(string bundlePath)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            throw new FileNotFoundException(LocalizationService.Get("CompanionBundle.FileMissing"), bundlePath);
        try
        {
            return ZipFile.OpenRead(bundlePath);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.Invalid"), exception);
        }
    }

    private static string NormalizeAssetEntryName(string path)
    {
        var normalized = NormalizeArchiveEntryName(path);
        if (!normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
            || normalized.Length == "assets/".Length)
        {
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.UnsafePath"));
        }
        return normalized;
    }

    private static string NormalizeArchiveEntryName(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.UnsafePath"));
        var normalized = path.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.Contains(':')))
        {
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.UnsafePath"));
        }
        return string.Join('/', segments);
    }

    private static string EnsureUnderRoot(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(LocalizationService.Get("CompanionBundle.UnsafePath"));
        return path;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(fileName.Select(character => invalid.Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(sanitized) ? "asset" : sanitized;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"An incomplete companion bundle directory could not be removed: '{path}'.", exception);
        }
    }

    private sealed record BundleAsset(string SourcePath, string EntryName);

    private sealed class CompanionBundleManifest
    {
        [JsonRequired]
        public string Format { get; set; } = FormatIdentifier;
        [JsonRequired]
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public string BundleId { get; set; } = string.Empty;
        public int BundleVersion { get; set; } = 1;

        public CompanionDefinition? Companion { get; set; }
    }
}
