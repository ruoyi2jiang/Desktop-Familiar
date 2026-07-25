using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

public enum AnimationSceneRole
{
    None,
    Start,
    IdleDefault,
    Reminder,
    ReminderFallback,
    IdleRandom,
    SelfBirthday,
    UserBirthday,
    Exit,
    DragLeft,
    DragRight,
    DragHold,
    Click
}

/// <summary>
/// Owns scene-role playback decisions that must remain independent of WPF.
/// State-default scenes keep looping after dialogue because the state, rather
/// than the dialogue, owns their lifetime.
/// </summary>
public static class ScenePlaybackPolicy
{
    public static IReadOnlyList<AnimationScene> GetRandomIdleCandidates(CompanionDefinition companion)
    {
        ArgumentNullException.ThrowIfNull(companion);
        return companion.Animations
            .Where(scene => companion.IdleRandomAnimationIds.Contains(scene.Id)
                && scene.Id != companion.IdleDefaultAnimationId)
            .ToList();
    }

    public static bool ShouldFinishLoopWhenDialogEnds(
        AnimationSceneRole role,
        ScenePlaybackMode playbackMode) =>
        (role is AnimationSceneRole.IdleRandom
            or AnimationSceneRole.SelfBirthday
            or AnimationSceneRole.UserBirthday
            or AnimationSceneRole.Exit)
        && playbackMode == ScenePlaybackMode.LoopUntilDialogEnds;

    public static bool ShouldPresentSceneDialog(
        bool focusModeEnabled,
        AnimationSceneRole role) =>
        role != AnimationSceneRole.ReminderFallback
        && !IsInteractionRole(role)
        && (!focusModeEnabled || role == AnimationSceneRole.Reminder);

    public static bool IsInteractionRole(AnimationSceneRole role) => role is
        AnimationSceneRole.DragLeft or
        AnimationSceneRole.DragRight or
        AnimationSceneRole.DragHold or
        AnimationSceneRole.Click;

    /// <summary>
    /// A calendar reminder may replace the bubble only after its assigned scene
    /// has started and its configured dialogue has completed. An idle fallback
    /// has no reminder dialogue to wait for. A failed scene is also released.
    /// </summary>
    public static bool CanPresentReminder(
        AnimationSceneRole role,
        bool hasPendingSceneDialog,
        bool scenePlaybackFailed = false) =>
        scenePlaybackFailed
        || role == AnimationSceneRole.ReminderFallback
        || (role == AnimationSceneRole.Reminder && !hasPendingSceneDialog);
}
