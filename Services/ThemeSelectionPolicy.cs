using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services;

/// <summary>Maps a persisted saved-theme selection to the matching app theme kind.</summary>
public static class ThemeSelectionPolicy
{
    public static AppTheme Resolve(SavedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        if (!theme.IsBuiltIn)
            return AppTheme.Custom;

        if (string.Equals(theme.Name, LocalizationService.Get("Settings.Theme.SeaBlue"), StringComparison.OrdinalIgnoreCase))
            return AppTheme.SeaBlue;
        if (string.Equals(theme.Name, LocalizationService.Get("Settings.Theme.StellaDark"), StringComparison.OrdinalIgnoreCase))
            return AppTheme.StellaDark;

        return AppTheme.Crimson;
    }
}
