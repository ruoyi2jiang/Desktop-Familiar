using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Services.Addons;

namespace DesktopFamiliar.Tests;

public sealed class AddonManagerEntryTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.2", true)]
    [InlineData("1.0.2", "1.0.1", false)]
    [InlineData("1.0.2", "1.0.2", false)]
    [InlineData("1.0.2-beta.1", "1.0.2", true)]
    [InlineData("1.0.2-beta.2", "1.0.2-beta.10", true)]
    public void HasUpdate_RequiresTheAvailableVersionToBeNewer(
        string installedVersion,
        string availableVersion,
        bool expected)
    {
        var entry = new AddonManagerEntry(
            "desktopfamiliar.test",
            "Test addon",
            "Test description",
            new InstalledAddon
            {
                Id = "desktopfamiliar.test",
                InstalledVersion = installedVersion,
                Manifest = new AddonManifest { DisplayName = "Test addon", Version = installedVersion }
            },
            new AddonPackageRelease
            {
                Id = "desktopfamiliar.test",
                DisplayName = "Test addon",
                Version = availableVersion
            });

        Assert.Equal(expected, entry.HasUpdate);
    }
}
