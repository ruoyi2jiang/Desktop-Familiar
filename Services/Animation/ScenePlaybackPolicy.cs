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
    Click,
    ChatReply
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
        var scenes = companion.ActiveScenes;
        return scenes.Animations
            .Where(scene => scenes.IdleRandomAnimationIds.Contains(scene.Id)
                && scene.Id != scenes.IdleDefaultAnimationId)
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

    public static bool ShouldReleaseModelLoopAfterDialogue(AnimationSceneRole role, ModelLoopMode mode) =>
        mode == ModelLoopMode.UntilDialogueEnds && role is
            AnimationSceneRole.Start or AnimationSceneRole.Click or AnimationSceneRole.IdleRandom or
            AnimationSceneRole.SelfBirthday or AnimationSceneRole.UserBirthday or AnimationSceneRole.Exit;

    public static bool ShouldPresentSceneDialog(
        bool focusModeEnabled,
        AnimationSceneRole role,
        CompanionAppearance appearance = CompanionAppearance.Images2D) =>
        role is not (AnimationSceneRole.ReminderFallback or AnimationSceneRole.ChatReply)
        && (appearance == CompanionAppearance.Model3D || !IsInteractionRole(role))
        && (!focusModeEnabled || role == AnimationSceneRole.Reminder);

    public static bool BeginIdleUnderDialogue(AnimationSceneRole role, CompanionActivityState state) =>
        role == AnimationSceneRole.Start && state == CompanionActivityState.Idle;

    internal static float ModelBlendSeconds(AnimationSceneRole from, AnimationSceneRole to)
    {
        if (from == AnimationSceneRole.None) return 0;
        if (from == AnimationSceneRole.ChatReply || to == AnimationSceneRole.ChatReply
            || (from == AnimationSceneRole.Start && to == AnimationSceneRole.IdleDefault))
            return ModelPoseTransition.DefaultDuration;
        return to == AnimationSceneRole.IdleDefault && from != AnimationSceneRole.IdleDefault
            ? ModelPoseTransition.ReturnToIdleDuration : 0;
    }

    public static bool IsInteractionRole(AnimationSceneRole role) => role is
        AnimationSceneRole.DragLeft or
        AnimationSceneRole.DragRight or
        AnimationSceneRole.DragHold or
        AnimationSceneRole.Click;

    public static bool CanStartChatReply(AnimationSceneRole role, CompanionActivityState activity,
        bool hasActiveDialogue, Guid? playingScene = null, Guid? requestedScene = null) =>
        activity == CompanionActivityState.Idle && !hasActiveDialogue
        && (role is AnimationSceneRole.IdleDefault or AnimationSceneRole.IdleRandom
            || role == AnimationSceneRole.ChatReply && requestedScene.HasValue && requestedScene != playingScene);

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
