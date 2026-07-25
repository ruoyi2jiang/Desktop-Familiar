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
    }

    public string RootDirectory { get; }
    public string DataFilePath => _dataStore.FilePath;
    public StorageRecoveryNotice? LastRecoveryNotice { get; private set; }

    public DesktopCompanionData Load()
    {
        LastRecoveryNotice = null;
        DesktopCompanionData data;
        if (!_dataStore.Exists)
        {
            data = new DesktopCompanionData();
        }
        else
        {
            try
            {
                data = _dataStore.Read();
            }
            catch (Exception exception)
            {
                AppLogger.Error("The saved companion configuration could not be read.", exception);
                string? backupPath = null;
                try
                {
                    backupPath = _dataStore.BackupInvalidFile(DateTime.Now);
                }
                catch (Exception backupException)
                {
                    AppLogger.Error("The invalid configuration could not be backed up.", backupException);
                }

                LastRecoveryNotice = new StorageRecoveryNotice(backupPath, exception.Message);
                data = new DesktopCompanionData();
            }
        }

        var themeBeforeNormalization = data.Settings?.Theme;
        var selectedThemeIdBeforeNormalization = data.Settings?.SelectedThemeId;
        var savedThemeIdsBeforeNormalization = data.Settings?.SavedThemes?
            .Where(theme => theme is not null)
            .Select(theme => theme.Id)
            .Order()
            .ToArray() ?? [];
        var requiresNormalizationSave = data.SchemaVersion != DesktopCompanionData.CurrentSchemaVersion
            || data.Settings?.SavedThemes?.Count is not > 0;
        _normalizer.Normalize(data);
        var normalizedSettings = data.Settings
            ?? throw new InvalidOperationException("Configuration normalization did not create app settings.");
        var savedThemeIdsAfterNormalization = normalizedSettings.SavedThemes
            .Select(theme => theme.Id)
            .Order()
            .ToArray();
        var normalizedThemeSelection = themeBeforeNormalization != normalizedSettings.Theme
            || selectedThemeIdBeforeNormalization != normalizedSettings.SelectedThemeId
            || !savedThemeIdsBeforeNormalization.SequenceEqual(savedThemeIdsAfterNormalization);
        if (requiresNormalizationSave || normalizedThemeSelection)
            Save(data);
        return data;
    }

    public void Save(DesktopCompanionData data) => _dataStore.Write(data);

    public string ImportAsset(Guid companionId, string sourcePath, string category) =>
        _assetStore.Import(companionId, sourcePath, category);

    public IReadOnlyList<string> GetImportedBubbleImages(DesktopCompanionData data) =>
        _assetStore.GetImportedBubbleImages(data);

    public void DeleteCompanionResources(Guid companionId) =>
        _assetStore.DeleteCompanionResources(companionId);

    public void ExportCompanion(CompanionDefinition companion, string destinationPath) =>
        _bundleService.Export(companion, destinationPath);

    public CompanionBundlePreview InspectCompanionBundle(string bundlePath) =>
        _bundleService.Inspect(bundlePath);

    public CompanionBundleImportResult ImportCompanionBundle(
        string bundlePath,
        Guid destinationCompanionId,
        string? importedName = null) =>
        _bundleService.Import(bundlePath, destinationCompanionId, importedName);

    public void DiscardCompanionBundleImport(CompanionBundleImportResult importResult) =>
        _bundleService.DiscardImport(importResult);
}

public sealed record StorageRecoveryNotice(string? BackupPath, string ErrorMessage);
