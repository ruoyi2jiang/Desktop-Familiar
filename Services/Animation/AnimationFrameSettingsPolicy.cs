using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

/// <summary>
/// Owns the PNG-only contract for individual-frame settings. Callers at the
/// editor, migration, and playback boundaries share this rule so hidden frame
/// settings cannot remain active for GIF or static-image sources.
/// </summary>
public static class AnimationFrameSettingsPolicy
{
    public static bool SupportsIndividualFrames(AnimationScene? scene) =>
        scene?.PngFrames is { Count: > 0 };

    public static bool ResetIfUnsupported(AnimationScene? scene)
    {
        if (scene is null || SupportsIndividualFrames(scene))
            return false;

        return Reset(scene);
    }

    public static bool Reset(AnimationScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var changed = scene.FrameLoopMode != AnimationFrameLoopMode.EntireAnimation
            || scene.LoopStartFrameIndex != 0
            || scene.LoopEndFrameIndex != 0
            || scene.DialogStartFrameIndex != 0;
        scene.FrameLoopMode = AnimationFrameLoopMode.EntireAnimation;
        scene.LoopStartFrameIndex = 0;
        scene.LoopEndFrameIndex = 0;
        scene.DialogStartFrameIndex = 0;
        return changed;
    }

    public static bool TryGetRuntimeLoopRange(
        AnimationScene scene,
        int frameCount,
        out int startFrameIndex,
        out int endFrameIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (SupportsIndividualFrames(scene))
            return scene.TryGetLoopRange(frameCount, out startFrameIndex, out endFrameIndex);

        startFrameIndex = 0;
        endFrameIndex = 0;
        return false;
    }

    public static int GetRuntimeDialogStartFrame(AnimationScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return SupportsIndividualFrames(scene)
            ? Math.Max(scene.DialogStartFrameIndex, 0)
            : 0;
    }
}
