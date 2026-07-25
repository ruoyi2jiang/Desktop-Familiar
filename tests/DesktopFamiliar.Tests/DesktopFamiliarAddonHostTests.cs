using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Services.Addons;

namespace DesktopFamiliar.Tests;

public sealed class DesktopFamiliarAddonHostTests
{
    [Fact]
    public void Services_ExposesTheApi12AddonServices()
    {
        IAddonViewportActionRegistry registry = new AddonViewportActionRegistry();
        IAddonSettingsSectionRegistry sections = new AddonSettingsSectionRegistry();
        var settings = new TestSettingsStore();
        var strings = new TestStringProvider();
        var status = new TestStatusPresenter();
        var host = new DesktopFamiliarAddonHost(registry, sections, settings, strings, status);

        Assert.Equal(new AddonApiVersion(1, 2), host.ApiVersion);
        Assert.Same(registry, host.Services.GetService(typeof(IAddonViewportActionRegistry)));
        Assert.Same(sections, host.Services.GetService(typeof(IAddonSettingsSectionRegistry)));
        Assert.Same(settings, host.Services.GetService(typeof(IAddonSettingsStore)));
        Assert.Same(strings, host.Services.GetService(typeof(IAddonStringProvider)));
        Assert.Same(status, host.Services.GetService(typeof(IAddonCompanionStatusPresenter)));
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
}
