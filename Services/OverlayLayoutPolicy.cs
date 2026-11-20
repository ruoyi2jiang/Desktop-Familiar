using System.Drawing;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services;

/// <summary>Calculates character, dialogue, and chat prompt geometry in device-independent units.</summary>
public static class OverlayLayoutPolicy
{
    private const float DrawingBlock = 64;
    // Room for the prompt, its scrolling sent message, and a reply above it.
    private const float InitialDialogueSpace = 320;

    public static int GetDialogWidth(int characterViewportWidth) => Math.Clamp(
        characterViewportWidth,
        AppSettings.MinimumDialogBubbleWidth,
        AppSettings.MaximumDialogBubbleWidth);

    public static int GetWindowWidth(int characterViewportWidth) => Math.Max(
        characterViewportWidth,
        GetDialogWidth(characterViewportWidth));

    public static double GetCharacterHorizontalOffset(int characterViewportWidth) =>
        (GetWindowWidth(characterViewportWidth) - characterViewportWidth) / 2d;

    internal static RectangleF ReserveDrawingExtent(RectangleF previous, int characterWidth, int characterHeight,
        float dialogueHeight, bool hasModel)
    {
        var motionMargin = hasModel ? DrawingBlock : 0;
        var width = Math.Max(GetWindowWidth(characterWidth), characterWidth + 2 * motionMargin);
        var above = Math.Max(InitialDialogueSpace, MathF.Ceiling(dialogueHeight / DrawingBlock) * DrawingBlock);
        var required = new RectangleF((characterWidth - width) / 2, -above, width, above + characterHeight + motionMargin);
        return previous.IsEmpty ? required : RectangleF.Union(previous, required);
    }

    // Round the configured motion bounds outward, allowing a small rasterization margin.
    public static RectangleF ExpandModelExtent(RectangleF extent, RectangleF pose)
    {
        pose.Inflate(2, 2);
        if (extent.Contains(pose)) return extent;
        return RectangleF.FromLTRB(
            Math.Min(extent.Left, MathF.Floor(pose.Left / DrawingBlock) * DrawingBlock),
            Math.Min(extent.Top, MathF.Floor(pose.Top / DrawingBlock) * DrawingBlock),
            Math.Max(extent.Right, MathF.Ceiling(pose.Right / DrawingBlock) * DrawingBlock),
            Math.Max(extent.Bottom, MathF.Ceiling(pose.Bottom / DrawingBlock) * DrawingBlock));
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
