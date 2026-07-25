using System.Reflection;
using System.Runtime.Loader;
using DesktopFamiliar.AddonSdk;

namespace DesktopFamiliar.Services.Addons;

/// <summary>
/// Loads compatible, enabled addon entry points from the validated local package store.
/// An addon never loads directly from a download location or an arbitrary repository.
/// </summary>
public sealed class AddonRuntimeLoader : IDisposable
{
    private readonly AddonInstallationStore _installationStore;
    private readonly Action<string, Exception>? _logFailure;
    private readonly List<LoadedAddon> _loadedAddons = [];
    private bool _disposed;

    public AddonRuntimeLoader(AddonInstallationStore installationStore, Action<string, Exception>? logFailure = null)
    {
        _installationStore = installationStore ?? throw new ArgumentNullException(nameof(installationStore));
        _logFailure = logFailure;
    }

    public IReadOnlyList<LoadedAddon> LoadedAddons => _loadedAddons.AsReadOnly();

    public IReadOnlyList<AddonLoadFailure> LoadEnabled(IAddonHost host)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(host);
        if (_loadedAddons.Count > 0)
            throw new InvalidOperationException("Enabled addons have already been loaded for this host session.");

        var failures = new List<AddonLoadFailure>();
        IReadOnlyList<InstalledAddon> installedAddons;
        try
        {
            installedAddons = _installationStore.GetInstalled();
        }
        catch (Exception exception)
        {
            _logFailure?.Invoke("Installed addon packages could not be read.", exception);
            return [new AddonLoadFailure("installation-registry", exception.Message)];
        }

        foreach (var installed in installedAddons.Where(addon => addon.Enabled))
        {
            try
            {
                var loaded = Load(installed, host);
                _loadedAddons.Add(loaded);
            }
            catch (Exception exception)
            {
                _logFailure?.Invoke($"Addon '{installed.Id}' could not be loaded.", exception);
                failures.Add(new AddonLoadFailure(installed.Id, exception.Message));
            }
        }

        return failures;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        for (var index = _loadedAddons.Count - 1; index >= 0; index--)
            _loadedAddons[index].Dispose();
        _loadedAddons.Clear();
    }

    private LoadedAddon Load(InstalledAddon installed, IAddonHost host)
    {
        var compatibility = AddonCompatibilityPolicy.Evaluate(installed.Manifest, host.ApiVersion);
        if (!compatibility.IsCompatible)
            throw new InvalidDataException(compatibility.Reason);

        var packageDirectory = _installationStore.GetPackageDirectory(installed);
        var assemblyPath = Path.GetFullPath(Path.Combine(packageDirectory, installed.Manifest.AssemblyFileName));
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException("The installed addon assembly could not be found.", assemblyPath);

        var loadContext = new AddonLoadContext(assemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(installed.Manifest.EntryPoint, throwOnError: true)
                ?? throw new InvalidDataException("The addon entry-point type could not be found.");
            if (!typeof(IAddon).IsAssignableFrom(type))
                throw new InvalidDataException("The addon entry point does not implement the Desktop Familiar addon contract.");
            if (Activator.CreateInstance(type) is not IAddon addon)
                throw new InvalidDataException("The addon entry point could not be created.");

            try
            {
                ValidateRuntimeManifest(installed.Manifest, addon.Manifest);
                addon.Initialize(host);
                return new LoadedAddon(installed, addon, loadContext);
            }
            catch
            {
                addon.Dispose();
                throw;
            }
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    private static void ValidateRuntimeManifest(AddonManifest installed, AddonManifest runtime)
    {
        if (!string.Equals(installed.Id, runtime.Id, StringComparison.Ordinal)
            || !string.Equals(installed.Version, runtime.Version, StringComparison.Ordinal)
            || !string.Equals(installed.EntryPoint, runtime.EntryPoint, StringComparison.Ordinal)
            || !string.Equals(installed.AssemblyFileName, runtime.AssemblyFileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The loaded addon identity does not match the installed package manifest.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AddonRuntimeLoader));
    }

    private sealed class AddonLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public AddonLoadContext(string mainAssemblyPath)
            : base($"Desktop Familiar addon: {Path.GetFileNameWithoutExtension(mainAssemblyPath)}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(
                    assemblyName.Name,
                    typeof(IAddon).Assembly.GetName().Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var resolvedAssemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedAssemblyPath is null ? null : LoadFromAssemblyPath(resolvedAssemblyPath);
        }
    }
}

public sealed class LoadedAddon : IDisposable
{
    private readonly AssemblyLoadContext _loadContext;
    private bool _disposed;

    internal LoadedAddon(InstalledAddon installation, IAddon addon, AssemblyLoadContext loadContext)
    {
        Installation = installation.Clone();
        Addon = addon;
        _loadContext = loadContext;
    }

    public InstalledAddon Installation { get; }
    public IAddon Addon { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Addon.Dispose();
        _loadContext.Unload();
    }
}

public sealed record AddonLoadFailure(string AddonId, string ErrorMessage);
