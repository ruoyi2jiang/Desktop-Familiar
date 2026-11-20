using System.Xml.Linq;

namespace DesktopFamiliar.Tests;

public sealed class SettingsPageRoutingTests
{
    [Fact]
    public void ModelTestButtonUsesNormalButtonHeightAndAlignsWithModelField()
    {
        var markup = XDocument.Load(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml")));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var field = markup.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "ChatModelChoice");
        var button = markup.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "TestChatModel");
        Assert.Same(field.Parent, button.Parent);
        Assert.Equal("{x:Static controls:FormFieldMetrics.FieldMargin}", (string?)field.Parent!.Attribute("Margin"));
        Assert.Equal("0", (string?)field.Attribute("Margin"));
        Assert.Equal("Center", (string?)button.Attribute("VerticalAlignment"));
        Assert.Null(button.Attribute("Height"));
        Assert.Null(button.Attribute("Padding"));
    }

    [Fact]
    public void AddonsPage_ContainsTheMarketplaceAndAddonOwnedSettingsSections()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml.cs")));
        var markup = File.ReadAllText(FindRepositoryFile(Path.Combine("Views", "SettingsWindow.xaml")));

        Assert.Contains(
            "AddonsPanel.Visibility = page == SettingsPage.Addons ? Visibility.Visible : Visibility.Collapsed;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("IAddonSettingsSectionRegistry", source, StringComparison.Ordinal);
        Assert.Contains("RefreshAddonSettingsSections(", source, StringComparison.Ordinal);
        Assert.Contains("AddonSettingsSections", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoClickEnabledToggle", markup, StringComparison.Ordinal);
        Assert.Contains("AvailableAddonsTab", markup, StringComparison.Ordinal);
        Assert.Contains("InstalledAddonsTab", markup, StringComparison.Ordinal);
        Assert.Contains("SettingsAddonSwitcher", markup, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"1\" Height=\"20\"", markup, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RefreshAddonsSpinner\" Text=\"&#x27F3;\" FontSize=\"16\" Width=\"16\" Height=\"16\"", markup, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsAddonListItem\"", markup, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource PrimaryBackgroundBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{DynamicResource TextOnPrimaryBackgroundBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("<ContentPresenter TextElement.Foreground=\"{TemplateBinding Foreground}\" />", markup, StringComparison.Ordinal);
        Assert.Contains("Value=\"{DynamicResource PrimaryBackgroundActiveBrush}\"", markup, StringComparison.Ordinal);
        Assert.Contains("AvailableAddonDetailsPanel", markup, StringComparison.Ordinal);
        Assert.Contains("InstalledAddonsList", markup, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"240\" />", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AvailableAddonsListScrollViewer\"", markup, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"480\"", markup, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Disabled\"", markup, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("SettingsPageScrollViewer", markup, StringComparison.Ordinal);
        Assert.Contains("AvailableAddonDetailsBorder.Visibility = entry is null ? Visibility.Collapsed : Visibility.Visible;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.SetColumnSpan(AvailableAddonsListPanel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsPageScrollViewer.VerticalScrollBarVisibility = page == SettingsPage.Addons", source, StringComparison.Ordinal);
        Assert.Contains("if (_addonManager.NeedsCatalogueRefresh)", source, StringComparison.Ordinal);
        Assert.Contains("await RefreshAddonsAsync();", source, StringComparison.Ordinal);
        Assert.Contains("_isAddonCatalogueRefreshInProgress", source, StringComparison.Ordinal);
        Assert.Contains("ThemeTokens.PrimaryBackground", source, StringComparison.Ordinal);
        Assert.Contains("ThemeTokens.PrimaryAccent", source, StringComparison.Ordinal);
        Assert.Contains("ThemeTokens.PrimaryTextOnBackground", source, StringComparison.Ordinal);
        Assert.Contains("RefreshAddonSettingsSections(installed.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));", source, StringComparison.Ordinal);
        Assert.Contains("installedAddonIds.Contains(section.Id)", source, StringComparison.Ordinal);
        Assert.Contains("Settings.Addons.Uninstall.RestartPrompt", source, StringComparison.Ordinal);
        Assert.Contains("Settings.Addons.Install.RestartPrompt", source, StringComparison.Ordinal);
        Assert.Contains("Settings.Addons.Enable.RestartPrompt", source, StringComparison.Ordinal);
        Assert.Contains("EnableAddon_Click", source, StringComparison.Ordinal);
        Assert.Contains("PromptForRestart", source, StringComparison.Ordinal);
        Assert.Contains("_applicationRestartService.RestartCurrentProcess();", source, StringComparison.Ordinal);
        Assert.Contains("Application.Current.Shutdown();", source, StringComparison.Ordinal);
        Assert.Contains("IInlineAddonSettingsSection", source, StringComparison.Ordinal);
        Assert.Contains("CreateInstalledVersionDetails", source, StringComparison.Ordinal);
        Assert.Contains("Settings.Addons.InstalledVersion", source, StringComparison.Ordinal);
        Assert.Contains("Settings.Addons.LatestCompatibleVersion", source, StringComparison.Ordinal);
        Assert.Contains("Orientation = Orientation.Horizontal", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("panel.Children.Add(buttons);", StringComparison.Ordinal)
            < source.IndexOf("panel.Children.Add(inlineSettings.Settings);", StringComparison.Ordinal));
        Assert.DoesNotContain("ToggleAddonEnabled_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("?? AvailableAddonsList.Items.OfType<ListBoxItem>().FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("AvailableAddonsTab.Content = LocalizationService.Format(\"Settings.Addons.AvailableTab\", available.Length);", source, StringComparison.Ordinal);
        Assert.Contains("InstalledAddonsTab.Content = LocalizationService.Format(\"Settings.Addons.InstalledTab\", installed.Length);", source, StringComparison.Ordinal);
        Assert.Contains(
            "? LocalizationService.Get(\"Settings.Addons.NoneAvailable\")",
            source,
            StringComparison.Ordinal);
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
