using System.Text.Json;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Storage;

/// <summary>
/// Owns the on-disk JSON document and replaces it atomically so a failed write
/// cannot destroy the last usable configuration.
/// </summary>
public sealed class JsonAppDataStore : IAppDataStore
{
    private readonly JsonSerializerOptions _options;

    public JsonAppDataStore(string rootDirectory, JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(options);
        RootDirectory = Path.GetFullPath(rootDirectory);
        FilePath = Path.Combine(RootDirectory, "desktop-familiar.json");
        _options = options;
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }
    public string FilePath { get; }
    public bool Exists => File.Exists(FilePath);

    public DesktopCompanionData Read()
    {
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<DesktopCompanionData>(json, _options)
            ?? throw new InvalidDataException("The saved configuration is empty.");
    }

    public void Write(DesktopCompanionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(RootDirectory);

        var temporaryPath = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, _options));
            if (File.Exists(FilePath))
                File.Replace(temporaryPath, FilePath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, FilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public string BackupInvalidFile(DateTime timestamp)
    {
        var backupPath = Path.Combine(
            RootDirectory,
            $"desktop-familiar.invalid-{timestamp:yyyyMMdd-HHmmss}.json");
        File.Copy(FilePath, backupPath, overwrite: true);
        return backupPath;
    }
}
