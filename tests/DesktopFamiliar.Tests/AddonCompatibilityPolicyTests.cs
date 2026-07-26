using DesktopFamiliar.AddonSdk;
using DesktopFamiliar.Addons.AutoClicker.Models;

namespace DesktopFamiliar.Tests;

public sealed class AddonCompatibilityPolicyTests
{
    [Fact]
    public void FirstPartyAutoClicker_IsCompatibleWithTheCurrentHostApi()
    {
        var result = AddonCompatibilityPolicy.Evaluate(
            AutoClickerAddonDefinition.CreateManifest(),
            AddonSdkVersion.CurrentHostApi);

        Assert.True(result.IsCompatible);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void FirstPartyAutoClickerManifest_MatchesTheDocumentedPackageFormat()
    {
        var json = File.ReadAllText(FindRepositoryFile(Path.Combine("Addons", "AutoClicker", "addon.json")));
        var manifest = AddonManifestJson.Deserialize(json);

        Assert.Equal(AutoClickerAddonDefinition.Id, manifest.Id);
        Assert.Equal("1.0.2", manifest.Version);
        Assert.Equal(new AddonApiVersion(1, 4), manifest.MinimumHostApi);
        Assert.Equal(new AddonApiVersion(2, 0), manifest.MaximumHostApiExclusive);
        Assert.Contains(AddonCapability.GlobalHotkeys, manifest.Capabilities);
        Assert.Contains(AddonCapability.InputInjection, manifest.Capabilities);
        Assert.True(AddonCompatibilityPolicy.Evaluate(manifest, AddonSdkVersion.CurrentHostApi).IsCompatible);
    }

    [Fact]
    public void AutoClickerSettings_UsesTheThemedHelpIconForBackgroundMode()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Addons", "AutoClicker", "Views", "AutoClickerSettingsSection.cs")));
        var markup = File.ReadAllText(FindRepositoryFile(Path.Combine("Addons", "AutoClicker", "Views", "AutoClickerHelpIcon.xaml")));

        Assert.Contains("new AutoClickerHelpIcon(", source, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.InitialShowDelay\" Value=\"0\"", markup, StringComparison.Ordinal);
        Assert.Contains("DynamicResource PrimaryAccentBrush", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineSettings_TitleActionIsPlacedInTheHostTitleRow()
    {
        var sdkSource = File.ReadAllText(FindRepositoryFile(Path.Combine("DesktopFamiliar.AddonSdk", "AddonSettings.cs")));
        var settingsSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml.cs")));
        var autoClickerSource = File.ReadAllText(FindRepositoryFile(Path.Combine("Addons", "AutoClicker", "Views", "AutoClickerSettingsSection.cs")));

        Assert.Contains("FrameworkElement? TitleAction", sdkSource, StringComparison.Ordinal);
        Assert.Contains("inlineSettings?.TitleAction", settingsSource, StringComparison.Ordinal);
        Assert.Contains("DockPanel.SetDock(titleAction, Dock.Right)", settingsSource, StringComparison.Ordinal);
        Assert.Contains("TitleAction = control._enabledToggle", autoClickerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Compatibility_RejectsAnAddonThatRequiresANewerHostApi()
    {
        var manifest = AutoClickerAddonDefinition.CreateManifest();
        manifest.MinimumHostApi = new AddonApiVersion(1, 3);

        var result = AddonCompatibilityPolicy.Evaluate(manifest, new AddonApiVersion(1, 2));

        Assert.False(result.IsCompatible);
        Assert.Contains("1.3", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Compatibility_RejectsAnAddonOutsideItsMaximumHostApi()
    {
        var manifest = AutoClickerAddonDefinition.CreateManifest();

        var result = AddonCompatibilityPolicy.Evaluate(manifest, new AddonApiVersion(2, 0));

        Assert.False(result.IsCompatible);
        Assert.Contains("2.0", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Compatibility_RejectsAnUnknownManifestFormat()
    {
        var manifest = AutoClickerAddonDefinition.CreateManifest();
        manifest.ManifestVersion++;

        var result = AddonCompatibilityPolicy.Evaluate(manifest, AddonSdkVersion.CurrentHostApi);

        Assert.False(result.IsCompatible);
        Assert.NotNull(result.Reason);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
