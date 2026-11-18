using System.Windows.Media.Imaging;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;

namespace DesktopFamiliar.Views.Controls;

internal static class CompanionBubbleAppearance
{
    internal static void Apply(Border surface, CompanionDefinition? pet)
    {
        surface.SetResourceReference(Border.BackgroundProperty, ThemeTokens.OverlaySurface);
        surface.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, ThemeTokens.TextOnOverlay);
        if (pet == null) return;
        surface.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, ResolveFont(pet));
        surface.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,
            TryBrush(pet.FontColor, (Brush)surface.FindResource(ThemeTokens.TextOnOverlay)));
        if (!File.Exists(pet.BubbleImagePath)) return;
        try { surface.Background = new ImageBrush(LoadBitmap(pet.BubbleImagePath!)) { Stretch = Stretch.Fill }; }
        catch (Exception ex) { AppLogger.Warning("Could not display chat bubble artwork.", ex); }
    }

    internal static FontFamily ResolveFont(CompanionDefinition companion)
    {
        if (!string.IsNullOrWhiteSpace(companion.ImportedFontPath) && File.Exists(companion.ImportedFontPath))
        {
            try
            {
                var folder = new Uri(Path.GetDirectoryName(companion.ImportedFontPath)! + Path.DirectorySeparatorChar, UriKind.Absolute);
                return new FontFamily(folder, $"./#{companion.FontFamilyName}");
            }
            catch (Exception exception)
            {
                AppLogger.Warning("The imported font could not be loaded; an installed font will be used.", exception);
                // Fall through to an installed font.
            }
        }
        return new FontFamily(companion.FontFamilyName);
    }

    internal static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    internal static Brush TryBrush(string value, Brush fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"The saved color '{value}' could not be parsed.", exception);
            return fallback;
        }
    }
}
