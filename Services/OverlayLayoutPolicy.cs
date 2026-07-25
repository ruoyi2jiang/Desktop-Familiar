using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services;

/// <summary>Calculates the independent character and dialogue layout widths.</summary>
public static class OverlayLayoutPolicy
{
    public static int GetDialogWidth(int characterViewportWidth) => Math.Clamp(
        characterViewportWidth,
        AppSettings.MinimumDialogBubbleWidth,
        AppSettings.MaximumDialogBubbleWidth);

    public static int GetWindowWidth(int characterViewportWidth) => Math.Max(
        characterViewportWidth,
        GetDialogWidth(characterViewportWidth));

    public static double GetCharacterHorizontalOffset(int characterViewportWidth) =>
        (GetWindowWidth(characterViewportWidth) - characterViewportWidth) / 2d;
}
