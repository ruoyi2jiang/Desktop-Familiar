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

    // Grow in blocks for the duration of a clip so each pixel of travel doesn't resize a layered HWND.
    public static RectangleF ExpandModelExtent(RectangleF extent, RectangleF pose)
    {
        const float step = 64;
        pose.Inflate(2, 2);
        if (extent.Contains(pose)) return extent;
        return RectangleF.FromLTRB(
            Math.Min(extent.Left, MathF.Floor(pose.Left / step) * step),
            Math.Min(extent.Top, MathF.Floor(pose.Top / step) * step),
            Math.Max(extent.Right, MathF.Ceiling(pose.Right / step) * step),
            Math.Max(extent.Bottom, MathF.Ceiling(pose.Bottom / step) * step));
    }

    public static RectangleF GetDrawingWindow(RectangleF character, float dialogHeight,
        RectangleF modelExtent, RectangleF desktop)
    {
        var width = GetWindowWidth((int)character.Width);
        var logical = new RectangleF(character.X - (width - character.Width) / 2,
            character.Y - dialogHeight, width, character.Height + dialogHeight);
        if (modelExtent.IsEmpty) return logical;
        modelExtent.Offset(character.Location);
        var visible = RectangleF.Intersect(modelExtent, desktop);
        return visible.IsEmpty ? logical : RectangleF.Union(logical, visible);
    }

    public static (double Left, double Top, double Width, double DialogueClearance) PlaceChatPrompt(
        RectangleF character, RectangleF workArea, double promptHeight, double dialogueHeight)
    {
        const double gap = 6;
        const float minimumPromptWidth = 520;
        const float maximumPromptWidth = 640;
        var preferredWidth = Math.Clamp(character.Width, minimumPromptWidth, maximumPromptWidth);
        var width = Math.Min(preferredWidth, workArea.Width);
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
