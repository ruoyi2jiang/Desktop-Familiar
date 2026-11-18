using System.Drawing;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services;

/// <summary>Calculates character, dialogue, and chat prompt geometry in device-independent units.</summary>
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

    public static (double Left, double Top, double Width, double DialogueClearance) PlaceChatPrompt(
        RectangleF character, RectangleF workArea, double promptHeight, double dialogueHeight)
    {
        const double gap = 6;
        var width = Math.Min(GetDialogWidth((int)character.Width), workArea.Width);
        var left = Math.Clamp(character.Left + (character.Width - width) / 2, workArea.Left, workArea.Right - width);
        var spaceAbove = character.Top - workArea.Top - dialogueHeight;
        var spaceBelow = workArea.Bottom - character.Bottom;
        var above = spaceAbove >= promptHeight + 2 * gap
            || (spaceBelow < promptHeight + gap && spaceAbove >= spaceBelow);
        var top = above ? character.Top - gap - promptHeight : character.Bottom + gap;
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - promptHeight));
        return (left, top, width, above ? Math.Max(0, character.Top - top + gap) : 0);
    }
}
