namespace DesktopFamiliar.Tests;

public sealed class SettingsPageRoutingTests
{
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
        Assert.Contains("RefreshAddonSettingsSections();", source, StringComparison.Ordinal);
        Assert.Contains("AddonSettingsSections", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoClickEnabledToggle", markup, StringComparison.Ordinal);
        Assert.Contains("AvailableAddonsTab", markup, StringComparison.Ordinal);
        Assert.Contains("InstalledAddonsTab", markup, StringComparison.Ordinal);
        Assert.Contains("SettingsAddonSwitcher", markup, StringComparison.Ordinal);
        Assert.Contains("<Border Width=\"1\" Height=\"20\"", markup, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Orientation=\"Horizontal\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\">", markup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RefreshAddonsSpinner\" Text=\"&#x27F3;\" FontSize=\"16\" Width=\"16\" Height=\"16\"", markup, StringComparison.Ordinal);
        Assert.Contains("AvailableAddonDetailsPanel", markup, StringComparison.Ordinal);
        Assert.Contains("InstalledAddonsList", markup, StringComparison.Ordinal);
        Assert.Contains("AvailableAddonDetailsBorder.Visibility = entry is null ? Visibility.Collapsed : Visibility.Visible;", source, StringComparison.Ordinal);
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
