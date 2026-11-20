using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Services;

/// <summary>
/// Application-facing persistence facade. JSON I/O, normalization, imported
/// assets, and portable bundles are implemented by focused services.
/// </summary>
public sealed class CompanionStorage : ICompanionRepository
{
    private readonly IAppDataStore _dataStore;
    private readonly CompanionAssetStore _assetStore;
    private readonly CompanionBundleService _bundleService;
    private readonly CompanionDataNormalizer _normalizer;
    private readonly CalendarExpirationStore _expirations;
    private readonly HashSet<Guid> _finishedCleanups = [];

    public CompanionStorage()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopFamiliar"))
    {
    }

    public CompanionStorage(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        _dataStore = new JsonAppDataStore(RootDirectory, jsonOptions);
        _assetStore = new CompanionAssetStore(RootDirectory);
        _bundleService = new CompanionBundleService(RootDirectory, jsonOptions);
        _normalizer = new CompanionDataNormalizer();
        _expirations = new CalendarExpirationStore(Path.GetDirectoryName(_dataStore.FilePath)!);
    }

    public string RootDirectory { get; }
    public string DataFilePath => _dataStore.FilePath;
    public StorageRecoveryNotice? LastRecoveryNotice { get; private set; }

    public DesktopCompanionData Load()
    {
        LastRecoveryNotice = null;
        DesktopCompanionData data;
        var source = _dataStore.ReadPath;
        if (!File.Exists(source))
        {
            data = new DesktopCompanionData();
        }
        else
        {
            try
            {
                data = _dataStore.Read(source);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                AppLogger.Error("The saved companion configuration could not be read.", exception);
                LastRecoveryNotice = new StorageRecoveryNotice(source, exception.Message);
                data = new DesktopCompanionData();
            }
        }

        RequiresExplicitSave = source != DataFilePath || data.SchemaVersion != DesktopCompanionData.CurrentSchemaVersion
            || LastRecoveryNotice != null;
        // The typed projection of a newer file is edited only in this build's own format.
        if (data.SchemaVersion > DesktopCompanionData.CurrentSchemaVersion)
            data.SchemaVersion = DesktopCompanionData.CurrentSchemaVersion;
        _normalizer.Normalize(data);
        if (LastRecoveryNotice == null && File.Exists(source) && _dataStore.SourceFingerprint is { } fingerprint)
            _expirations.Apply(fingerprint, data);
        return data;
    }

    public bool RequiresExplicitSave { get; private set; }

    public void RecordCalendarExpiration(Guid eventId, DateTime occurrence)
    {
        if (_dataStore.SourceFingerprint is { } fingerprint)
            _expirations.Record(fingerprint, eventId, occurrence);
    }

    public void Save(DesktopCompanionData data)
    {
        _dataStore.Write(data);
        RequiresExplicitSave = false;
        _expirations.ClearAfterSave();
        foreach (var id in _finishedCleanups.ToArray())
        {
            if (data.PendingCompanionCleanup.Contains(id)) continue;
            try
            {
                File.Delete(HistoryWipeReceipt(id));
                _finishedCleanups.Remove(id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLogger.Warning("The completed chat history wipe receipt could not be removed.", exception);
            }
        }
    }

    public string ImportAsset(Guid companionId, string sourcePath, string category) =>
        _assetStore.Import(companionId, sourcePath, category);

    public IReadOnlyList<string> GetImportedBubbleImages(DesktopCompanionData data) =>
        _assetStore.GetImportedBubbleImages(data);

    public bool DeleteCompanionResources(Guid companionId, IEnumerable<string?> retainedPaths) =>
        _assetStore.DeleteCompanionResources(companionId,
            retainedPaths.Concat(_dataStore.OtherLibraryAssetReferences(companionId)));

    internal string HistoryWipeReceipt(Guid companionId) => Path.Combine(
        Path.GetDirectoryName(DataFilePath)!, $"history-wipe.v{DesktopCompanionData.CurrentSchemaVersion}.{companionId:N}");

    internal void FinishWipeOnNextSave(Guid companionId) => _finishedCleanups.Add(companionId);

    public void ExportCompanion(CompanionDefinition companion, string destinationPath) =>
        _bundleService.Export(companion, destinationPath);

    public CompanionBundlePreview InspectCompanionBundle(string bundlePath) =>
        _bundleService.Inspect(bundlePath);

    public CompanionBundleImportResult ImportCompanionBundle(
        string bundlePath,
        Guid? overwriteCompanionId = null,
        string? importedName = null) =>
        _bundleService.Import(bundlePath, overwriteCompanionId ?? Guid.NewGuid(), importedName);

    public void DiscardCompanionBundleImport(CompanionBundleImportResult importResult) =>
        _bundleService.DiscardImport(importResult);
}

public sealed record StorageRecoveryNotice(string FilePath, string ErrorMessage);
