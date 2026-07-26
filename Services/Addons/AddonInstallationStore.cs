using System.IO.Compression;
using System.Text.Json;
using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>
/// Owns the local first-party addon directory and its small installation registry.
/// Package extraction is staged and only becomes visible after the manifest and checksum succeed.
/// </summary>
public sealed class AddonInstallationStore
{
    private const string RegistryFileName = "installed-addons.json";
    private const string PackagesDirectoryName = "packages";
    private const int MaximumPackageEntryCount = 5_000;
    private const long MaximumUncompressedPackageBytes = 512L * 1024 * 1024;
    private readonly string _addonsDirectory;
    private readonly string _packagesDirectory;
    private readonly string _registryPath;

    public AddonInstallationStore(string applicationDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataRoot);
        _addonsDirectory = Path.Combine(Path.GetFullPath(applicationDataRoot), "Addons");
        _packagesDirectory = Path.Combine(_addonsDirectory, PackagesDirectoryName);
        _registryPath = Path.Combine(_addonsDirectory, RegistryFileName);
        Directory.CreateDirectory(_packagesDirectory);
        CleanupPendingPackageRemovals();
        CleanupPendingDataRemovals();
    }

    public string AddonsDirectory => _addonsDirectory;
    public string RegistryPath => _registryPath;

    public IReadOnlyList<InstalledAddon> GetInstalled()
    {
        var registry = ReadRegistry();
        return registry.Addons
            .OrderBy(addon => addon.Manifest.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(addon => addon.Clone())
            .ToArray();
    }

    /// <summary>Resolves an installed package directory while keeping callers inside the addon package store.</summary>
    public string GetPackageDirectory(InstalledAddon addon)
    {
        ArgumentNullException.ThrowIfNull(addon);
        return GetSafePackageDirectory(addon.PackageDirectory);
    }

    /// <summary>
    /// Validates and installs a downloaded .zip package. The caller is responsible for downloading it only
    /// from the trusted catalogue URL; this method independently checks its advertised SHA-256 hash.
    /// </summary>
    public InstalledAddon Install(string packagePath, AddonPackageRelease release, AddonApiVersion hostApi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(release);
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("The downloaded addon package could not be found.", packagePath);

        var releaseResult = AddonPackageValidationPolicy.EvaluateRelease(release, hostApi);
        if (!releaseResult.IsCompatible)
            throw new InvalidDataException(releaseResult.Reason);

        using (var package = File.OpenRead(packagePath))
        {
            if (!AddonPackageValidationPolicy.MatchesSha256(package, release.Sha256))
                throw new InvalidDataException("The downloaded addon package did not match the catalogue checksum.");
        }

        var stagingDirectory = Path.Combine(_packagesDirectory, $".staging-{Guid.NewGuid():N}");
        try
        {
            ExtractPackageSafely(packagePath, stagingDirectory);
            var manifest = ReadPackageManifest(stagingDirectory);
            ValidatePackageManifest(manifest, release, hostApi);
            if (!File.Exists(Path.Combine(stagingDirectory, manifest.AssemblyFileName)))
                throw new InvalidDataException("The addon package does not contain its declared assembly at the package root.");

            var destinationDirectory = GetPackageDirectory(manifest.Id, manifest.Version);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationDirectory)!);
            var registry = ReadRegistry();
            var previous = registry.Addons.SingleOrDefault(addon =>
                string.Equals(addon.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
            var previousDirectory = previous is null ? null : GetSafePackageDirectory(previous.PackageDirectory);
            var pendingPreviousDirectory = previousDirectory is not null
                && !string.Equals(previousDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(previousDirectory)
                    ? Path.Combine(_packagesDirectory, $".remove-{Guid.NewGuid():N}")
                    : null;
            var replacedPackageDirectory = Directory.Exists(destinationDirectory)
                ? Path.Combine(_packagesDirectory, $".remove-{Guid.NewGuid():N}")
                : null;
            var newDestinationCreated = false;
            InstalledAddon installed;
            try
            {
                if (pendingPreviousDirectory is not null)
                    Directory.Move(previousDirectory!, pendingPreviousDirectory);
                if (replacedPackageDirectory is not null)
                    Directory.Move(destinationDirectory, replacedPackageDirectory);
                Directory.Move(stagingDirectory, destinationDirectory);
                newDestinationCreated = true;

                installed = new InstalledAddon
                {
                    Id = manifest.Id,
                    InstalledVersion = manifest.Version,
                    Enabled = previous?.Enabled ?? true,
                    PackageDirectory = Path.GetRelativePath(_addonsDirectory, destinationDirectory),
                    Manifest = manifest.Clone()
                };
                registry.Addons.RemoveAll(addon => string.Equals(addon.Id, installed.Id, StringComparison.OrdinalIgnoreCase));
                registry.Addons.Add(installed);
                WriteRegistry(registry);
            }
            catch
            {
                if (newDestinationCreated && Directory.Exists(destinationDirectory))
                    Directory.Delete(destinationDirectory, recursive: true);
                if (replacedPackageDirectory is not null && Directory.Exists(replacedPackageDirectory))
                    Directory.Move(replacedPackageDirectory, destinationDirectory);
                if (pendingPreviousDirectory is not null && Directory.Exists(pendingPreviousDirectory))
                    Directory.Move(pendingPreviousDirectory, previousDirectory!);
                throw;
            }

            if (replacedPackageDirectory is not null && Directory.Exists(replacedPackageDirectory))
                DeletePendingPackageDirectory(replacedPackageDirectory);
            if (pendingPreviousDirectory is not null && Directory.Exists(pendingPreviousDirectory))
                DeletePendingPackageDirectory(pendingPreviousDirectory);
            return installed.Clone();
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    public void SetEnabled(string addonId, bool enabled)
    {
        ValidateAddonId(addonId);
        var registry = ReadRegistry();
        var addon = registry.Addons.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, addonId, StringComparison.OrdinalIgnoreCase));
        if (addon is null)
            throw new InvalidOperationException($"Addon '{addonId}' is not installed.");
        if (addon.Enabled == enabled)
            return;

        addon.Enabled = enabled;
        WriteRegistry(registry);
    }

    public void Uninstall(string addonId, bool deleteSavedData)
    {
        ValidateAddonId(addonId);
        var registry = ReadRegistry();
        var addon = registry.Addons.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, addonId, StringComparison.OrdinalIgnoreCase));
        if (addon is null)
            return;

        var packageDirectory = GetSafePackageDirectory(addon.PackageDirectory);
        var pendingRemovalDirectory = Directory.Exists(packageDirectory)
            ? Path.Combine(_packagesDirectory, $".remove-{Guid.NewGuid():N}")
            : null;
        if (pendingRemovalDirectory is not null)
            Directory.Move(packageDirectory, pendingRemovalDirectory);
        registry.Addons.Remove(addon);
        try
        {
            WriteRegistry(registry);
        }
        catch
        {
            if (pendingRemovalDirectory is not null && Directory.Exists(pendingRemovalDirectory))
                Directory.Move(pendingRemovalDirectory, packageDirectory);
            throw;
        }

        if (pendingRemovalDirectory is not null && Directory.Exists(pendingRemovalDirectory))
            DeletePendingPackageDirectory(pendingRemovalDirectory);

        if (deleteSavedData)
        {
            var savedDataDirectory = Path.Combine(_addonsDirectory, "data", addon.Id);
            if (Directory.Exists(savedDataDirectory))
            {
                var pendingDataRemovalDirectory = Path.Combine(_addonsDirectory, "data", $".remove-{Guid.NewGuid():N}");
                try
                {
                    Directory.Move(savedDataDirectory, pendingDataRemovalDirectory);
                    DeletePendingDataDirectory(pendingDataRemovalDirectory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The addon is already uninstalled. Preserve the moved data for later cleanup instead of
                    // reporting a failed uninstall after its durable installation state has changed.
                    AppLogger.Warning($"Saved data for uninstalled addon '{addon.Id}' could not be deleted.", exception);
                }
            }
        }
    }

    private AddonInstallationRegistry ReadRegistry()
    {
        if (!File.Exists(_registryPath))
            return new AddonInstallationRegistry();

        try
        {
            var registry = JsonSerializer.Deserialize<AddonInstallationRegistry>(File.ReadAllText(_registryPath), AddonManifestJson.Options)
                ?? throw new InvalidDataException("The addon installation registry is empty.");
            if (registry.SchemaVersion != AddonInstallationRegistry.CurrentSchemaVersion)
                throw new InvalidDataException("The addon installation registry format is not supported.");
            return registry;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            throw new InvalidDataException("The addon installation registry could not be read.", exception);
        }
    }

    private void WriteRegistry(AddonInstallationRegistry registry)
    {
        Directory.CreateDirectory(_addonsDirectory);
        var temporaryPath = _registryPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(registry, AddonManifestJson.Options));
            if (File.Exists(_registryPath))
                File.Replace(temporaryPath, _registryPath, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, _registryPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void CleanupPendingPackageRemovals()
    {
        try
        {
            foreach (var directory in Directory.GetDirectories(_packagesDirectory, ".remove-*", SearchOption.TopDirectoryOnly))
                DeletePendingPackageDirectory(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning("Pending addon package cleanup could not be completed.", exception);
        }
    }

    private void CleanupPendingDataRemovals()
    {
        var dataDirectory = Path.Combine(_addonsDirectory, "data");
        if (!Directory.Exists(dataDirectory))
            return;

        try
        {
            foreach (var directory in Directory.GetDirectories(dataDirectory, ".remove-*", SearchOption.TopDirectoryOnly))
                DeletePendingDataDirectory(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning("Pending addon data cleanup could not be completed.", exception);
        }
    }

    private static void DeletePendingPackageDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning($"Pending addon package directory '{directory}' could not be deleted.", exception);
        }
    }

    private static void DeletePendingDataDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning($"Pending addon data directory '{directory}' could not be deleted.", exception);
        }
    }

    private static void ExtractPackageSafely(string packagePath, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count == 0)
            throw new InvalidDataException("The addon package is empty.");
        if (archive.Entries.Count > MaximumPackageEntryCount)
            throw new InvalidDataException("The addon package has too many files.");

        long uncompressedByteCount = 0;
        foreach (var entry in archive.Entries)
        {
            try
            {
                uncompressedByteCount = checked(uncompressedByteCount + entry.Length);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("The addon package has an invalid uncompressed size.", exception);
            }
            if (uncompressedByteCount > MaximumUncompressedPackageBytes)
                throw new InvalidDataException("The addon package expands beyond the supported size.");
        }

        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationDirectory);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;

            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The addon package contains a path outside its installation directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: false);
        }
    }

    private static AddonManifest ReadPackageManifest(string packageDirectory)
    {
        var manifestPath = Path.Combine(packageDirectory, "addon.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("The addon package does not contain addon.json at its root.");
        return AddonManifestJson.Deserialize(File.ReadAllText(manifestPath));
    }

    private static void ValidatePackageManifest(AddonManifest manifest, AddonPackageRelease release, AddonApiVersion hostApi)
    {
        var compatibility = AddonCompatibilityPolicy.Evaluate(manifest, hostApi);
        if (!compatibility.IsCompatible)
            throw new InvalidDataException(compatibility.Reason);
        if (!string.Equals(manifest.Id, release.Id, StringComparison.Ordinal)
            || !string.Equals(manifest.Version, release.Version, StringComparison.Ordinal)
            || manifest.MinimumHostApi != release.MinimumHostApi
            || manifest.MaximumHostApiExclusive != release.MaximumHostApiExclusive)
        {
            throw new InvalidDataException("The addon package manifest does not match its catalogue release.");
        }

        ValidateAddonId(manifest.Id);
        ValidateVersion(manifest.Version);
        ValidateAssemblyFileName(manifest.AssemblyFileName);
    }

    private string GetPackageDirectory(string addonId, string version)
    {
        ValidateAddonId(addonId);
        ValidateVersion(version);
        return Path.Combine(_packagesDirectory, addonId, version);
    }

    private string GetSafePackageDirectory(string relativeDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeDirectory))
            throw new InvalidDataException("The installed addon package directory is missing.");

        var path = Path.GetFullPath(Path.Combine(_addonsDirectory, relativeDirectory));
        var packagesRoot = Path.GetFullPath(_packagesDirectory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(packagesRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installed addon package directory is outside the addon package store.");
        return path;
    }

    private static void ValidateAddonId(string addonId)
    {
        if (string.IsNullOrWhiteSpace(addonId)
            || addonId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-'))
            || addonId.StartsWith(".", StringComparison.Ordinal)
            || addonId.EndsWith(".", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The addon ID is not safe for the addon data directory.");
        }
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)
            || version.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-'))
            || version.StartsWith(".", StringComparison.Ordinal)
            || version.EndsWith(".", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The addon version is not safe for the addon package directory.");
        }
    }

    private static void ValidateAssemblyFileName(string assemblyFileName)
    {
        if (string.IsNullOrWhiteSpace(assemblyFileName)
            || !string.Equals(Path.GetFileName(assemblyFileName), assemblyFileName, StringComparison.Ordinal)
            || !assemblyFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The addon assembly file name is not safe for the addon package directory.");
        }
    }
}

public sealed class AddonInstallationRegistry
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<InstalledAddon> Addons { get; set; } = [];
}

public sealed class InstalledAddon
{
    public string Id { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string PackageDirectory { get; set; } = string.Empty;
    public AddonManifest Manifest { get; set; } = new();

    public InstalledAddon Clone() => new()
    {
        Id = Id,
        InstalledVersion = InstalledVersion,
        Enabled = Enabled,
        PackageDirectory = PackageDirectory,
        Manifest = Manifest.Clone()
    };
}
