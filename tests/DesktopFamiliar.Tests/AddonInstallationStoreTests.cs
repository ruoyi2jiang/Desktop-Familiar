using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Addons.AutoClicker;
using DesktopFamiliar.Addons.AutoClicker.Models;
using DesktopFamiliar.Services.Addons;

namespace DesktopFamiliar.Tests;

public sealed class AddonInstallationStoreTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"DesktopFamiliar.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Install_RegistersOnlyAValidatedMatchingPackage()
    {
        var packagePath = CreatePackage(AutoClickerAddonDefinition.CreateManifest());
        var store = new AddonInstallationStore(_temporaryDirectory);

        var installed = store.Install(packagePath, CreateRelease(packagePath), AddonSdkVersion.CurrentHostApi);

        Assert.True(installed.Enabled);
        Assert.Equal(AutoClickerAddonDefinition.Id, installed.Id);
        var fromRegistry = Assert.Single(store.GetInstalled());
        Assert.Equal(installed.InstalledVersion, fromRegistry.InstalledVersion);
        Assert.True(File.Exists(Path.Combine(store.AddonsDirectory, fromRegistry.PackageDirectory, "addon.json")));
    }

    [Fact]
    public void Install_RejectsChecksumMismatchWithoutMutatingRegistry()
    {
        var packagePath = CreatePackage(AutoClickerAddonDefinition.CreateManifest());
        var release = CreateRelease(packagePath);
        release.Sha256 = new string('0', 64);
        var store = new AddonInstallationStore(_temporaryDirectory);

        var exception = Assert.Throws<InvalidDataException>(() =>
            store.Install(packagePath, release, AddonSdkVersion.CurrentHostApi));

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.GetInstalled());
    }

    [Fact]
    public void Install_RejectsPackageManifestThatDiffersFromCatalogue()
    {
        var manifest = AutoClickerAddonDefinition.CreateManifest();
        manifest.Version = "1.0.1";
        var packagePath = CreatePackage(manifest);
        var store = new AddonInstallationStore(_temporaryDirectory);

        var exception = Assert.Throws<InvalidDataException>(() =>
            store.Install(packagePath, CreateRelease(packagePath), AddonSdkVersion.CurrentHostApi));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.GetInstalled());
    }

    [Fact]
    public void Uninstall_WithSavedData_RemovesOnlyTheSelectedAddonData()
    {
        var packagePath = CreatePackage(AutoClickerAddonDefinition.CreateManifest());
        var store = new AddonInstallationStore(_temporaryDirectory);
        store.Install(packagePath, CreateRelease(packagePath), AddonSdkVersion.CurrentHostApi);
        var dataPath = Path.Combine(store.AddonsDirectory, "data", AutoClickerAddonDefinition.Id, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
        File.WriteAllText(dataPath, "{}");

        store.Uninstall(AutoClickerAddonDefinition.Id, deleteSavedData: true);

        Assert.Empty(store.GetInstalled());
        Assert.False(File.Exists(dataPath));
    }

    [Fact]
    public void RuntimeLoader_ActivatesAnEnabledAddonFromItsInstalledPackage()
    {
        var manifest = AutoClickerAddonDefinition.CreateManifest();
        var packagePath = CreatePackage(manifest, typeof(AutoClickerAddon).Assembly.Location);
        var store = new AddonInstallationStore(_temporaryDirectory);
        store.Install(packagePath, CreateRelease(packagePath), AddonSdkVersion.CurrentHostApi);

        var runtimeIdentity = LoadInstalledAddon(store);

        Assert.Equal($"{manifest.Id}|{typeof(AutoClickerAddon).FullName}", runtimeIdentity);
    }

    [Fact]
    public void RuntimeLoader_ReportsAnUnreadableInstallationRegistryWithoutCrashingTheHost()
    {
        var store = new AddonInstallationStore(_temporaryDirectory);
        File.WriteAllText(store.RegistryPath, "not valid json");
        var loader = new AddonRuntimeLoader(store);

        var failures = loader.LoadEnabled(new TestAddonHost());

        var failure = Assert.Single(failures);
        Assert.Equal("installation-registry", failure.AddonId);
        Assert.Empty(loader.LoadedAddons);
    }

    [Fact]
    public async Task FirstPartyPackageInstaller_DownloadsAndInstallsTheVerifiedRelease()
    {
        var packagePath = CreatePackage(AutoClickerAddonDefinition.CreateManifest());
        var release = CreateRelease(packagePath);
        var store = new AddonInstallationStore(_temporaryDirectory);
        using var client = new HttpClient(new StaticResponseHandler(File.ReadAllBytes(packagePath)));
        var installer = new FirstPartyAddonPackageInstaller(client, store);

        var installed = await installer.InstallAsync(release, AddonSdkVersion.CurrentHostApi);

        Assert.Equal(AutoClickerAddonDefinition.Id, installed.Id);
        Assert.Single(store.GetInstalled());
    }

    [Fact]
    public async Task FirstPartyCatalogueClient_NotFoundReturnsAnEmptyCatalogue()
    {
        using var client = new HttpClient(new StatusResponseHandler(HttpStatusCode.NotFound));
        var catalogueClient = new FirstPartyAddonCatalogueClient(client);

        var catalogue = await catalogueClient.GetAsync();

        Assert.True(catalogueClient.LastFetchWasNotFound);
        Assert.Equal(FirstPartyAddonCataloguePolicy.Publisher, catalogue.Publisher);
        Assert.Empty(catalogue.Releases);
    }

    [Fact]
    public void FirstPartyCataloguePolicy_UsesTheCaseSensitiveCataloguePath()
    {
        Assert.Equal(
            "https://raw.githubusercontent.com/ruoyi2jiang/Desktop-Familiar/main/Addons/catalogue.json",
            FirstPartyAddonCataloguePolicy.CatalogueUri.AbsoluteUri);
    }

    [Fact]
    public void FirstPartyCataloguePolicy_RejectsThirdPartyPackageUrls()
    {
        var catalogue = new AddonPackageCatalogue
        {
            Publisher = FirstPartyAddonCataloguePolicy.Publisher,
            Releases =
            [
                new AddonPackageRelease
                {
                    Id = AutoClickerAddonDefinition.Id,
                    DisplayName = "Auto clicker",
                    Description = "Repeatedly click an area of the screen.",
                    Version = "1.0.0",
                    MinimumHostApi = new AddonApiVersion(1, 2),
                    MaximumHostApiExclusive = new AddonApiVersion(2, 0),
                    PackageUrl = "https://github.com/someone/other/releases/download/v1/auto-clicker.zip",
                    ReleaseNotesUrl = "https://github.com/ruoyi2jiang/Desktop-Familiar/releases/tag/auto-clicker-v1.0.0",
                    Sha256 = new string('A', 64)
                }
            ]
        };

        var result = new FirstPartyAddonCataloguePolicy().Evaluate(catalogue, AddonSdkVersion.CurrentHostApi);

        Assert.False(result.IsCompatible);
        Assert.Contains("trusted", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private string CreatePackage(AddonManifest manifest, string? assemblyPath = null)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var packagePath = Path.Combine(_temporaryDirectory, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("addon.json");
        using (var writer = new StreamWriter(entry.Open()))
            writer.Write(JsonSerializer.Serialize(manifest, AddonManifestJson.Options));
        if (assemblyPath is not null)
            archive.CreateEntryFromFile(assemblyPath, manifest.AssemblyFileName);
        return packagePath;
    }

    private static AddonPackageRelease CreateRelease(string packagePath) => new()
    {
        Id = AutoClickerAddonDefinition.Id,
        DisplayName = "Auto clicker",
        Description = "Repeatedly click an area of the screen.",
        Version = "1.0.0",
        MinimumHostApi = new AddonApiVersion(1, 2),
        MaximumHostApiExclusive = new AddonApiVersion(2, 0),
        PackageUrl = "https://github.com/ruoyi2jiang/Desktop-Familiar/releases/download/auto-clicker-v1.0.0/auto-clicker.zip",
        ReleaseNotesUrl = "https://github.com/ruoyi2jiang/Desktop-Familiar/releases/tag/auto-clicker-v1.0.0",
        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath)))
    };

    private static string LoadInstalledAddon(AddonInstallationStore store)
    {
        string? identity = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var loader = new AddonRuntimeLoader(store);
                var failures = loader.LoadEnabled(new TestAddonHost());
                Assert.Empty(failures);
                var loaded = Assert.Single(loader.LoadedAddons);
                identity = $"{loaded.Installation.Id}|{loaded.Addon.GetType().FullName}";
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
        return identity ?? throw new Xunit.Sdk.XunitException("The addon runtime did not report an identity.");
    }

    private sealed class TestAddonHost : IAddonHost, IServiceProvider
    {
        public AddonApiVersion ApiVersion => AddonSdkVersion.CurrentHostApi;
        private readonly IAddonViewportActionRegistry _viewportActions = new AddonViewportActionRegistry();
        private readonly IAddonSettingsSectionRegistry _settingsSections = new AddonSettingsSectionRegistry();
        private readonly IAddonSettingsStore _settingsStore = new TestSettingsStore();
        private readonly IAddonStringProvider _strings = new TestStringProvider();
        private readonly IAddonCompanionStatusPresenter _statusPresenter = new TestStatusPresenter();
        public IServiceProvider Services => this;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IAddonViewportActionRegistry))
                return _viewportActions;
            if (serviceType == typeof(IAddonSettingsSectionRegistry))
                return _settingsSections;
            if (serviceType == typeof(IAddonSettingsStore))
                return _settingsStore;
            if (serviceType == typeof(IAddonStringProvider))
                return _strings;
            return serviceType == typeof(IAddonCompanionStatusPresenter) ? _statusPresenter : null;
        }
    }

    private sealed class TestSettingsStore : IAddonSettingsStore
    {
        public T? Read<T>(string addonId) where T : class => null;

        public void Write<T>(string addonId, T settings) where T : class
        {
        }
    }

    private sealed class TestStringProvider : IAddonStringProvider
    {
        public string Get(string key) => key;

        public string Format(string key, params object[] arguments) => string.Format(key, arguments);
    }

    private sealed class TestStatusPresenter : IAddonCompanionStatusPresenter
    {
        public void Show(string message)
        {
        }
    }

    private sealed class StaticResponseHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
    }

    private sealed class StatusResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
