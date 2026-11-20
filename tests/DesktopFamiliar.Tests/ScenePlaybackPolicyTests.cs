using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public sealed class ScenePlaybackPolicyTests
{
    [Theory]
    [InlineData(AnimationSceneRole.IdleDefault, false)]
    [InlineData(AnimationSceneRole.Reminder, false)]
    [InlineData(AnimationSceneRole.ReminderFallback, false)]
    [InlineData(AnimationSceneRole.DragLeft, false)]
    [InlineData(AnimationSceneRole.DragRight, false)]
    [InlineData(AnimationSceneRole.DragHold, false)]
    [InlineData(AnimationSceneRole.ChatReply, false)]
    [InlineData(AnimationSceneRole.Start, true)]
    [InlineData(AnimationSceneRole.Click, true)]
    [InlineData(AnimationSceneRole.IdleRandom, true)]
    [InlineData(AnimationSceneRole.SelfBirthday, true)]
    [InlineData(AnimationSceneRole.UserBirthday, true)]
    [InlineData(AnimationSceneRole.Exit, true)]
    public void ModelDialogueReleasesOnlyTransientDialogueLoops(AnimationSceneRole role, bool release)
    {
        Assert.Equal(release, ScenePlaybackPolicy.ShouldReleaseModelLoopAfterDialogue(role, ModelLoopMode.UntilDialogueEnds));
        Assert.False(ScenePlaybackPolicy.ShouldReleaseModelLoopAfterDialogue(role, ModelLoopMode.FixedDuration));
    }

    [Theory]
    [InlineData(AnimationSceneRole.IdleRandom)]
    [InlineData(AnimationSceneRole.Click)]
    [InlineData(AnimationSceneRole.DragLeft)]
    [InlineData(AnimationSceneRole.DragRight)]
    [InlineData(AnimationSceneRole.DragHold)]
    [InlineData(AnimationSceneRole.SelfBirthday)]
    [InlineData(AnimationSceneRole.UserBirthday)]
    public void OrdinaryModelScenesBlendBackToIdle(AnimationSceneRole from) =>
        Assert.Equal(0.6f, ScenePlaybackPolicy.ModelBlendSeconds(from, AnimationSceneRole.IdleDefault));

    [Theory]
    [InlineData(AnimationSceneRole.Start, AnimationSceneRole.IdleDefault, 0.3f)]
    [InlineData(AnimationSceneRole.ChatReply, AnimationSceneRole.IdleDefault, 0.3f)]
    [InlineData(AnimationSceneRole.IdleDefault, AnimationSceneRole.ChatReply, 0.3f)]
    [InlineData(AnimationSceneRole.None, AnimationSceneRole.IdleDefault, 0f)]
    [InlineData(AnimationSceneRole.IdleDefault, AnimationSceneRole.IdleDefault, 0f)]
    public void ModelBlendsRespectExistingRoleBoundaries(AnimationSceneRole from, AnimationSceneRole to, float seconds) =>
        Assert.Equal(seconds, ScenePlaybackPolicy.ModelBlendSeconds(from, to));

    [Fact]
    public void RandomIdleCandidates_ExcludeDefaultAndUnassignedScenes()
    {
        var idleId = Guid.NewGuid();
        var randomId = Guid.NewGuid();
        var unassignedId = Guid.NewGuid();
        var companion = new CompanionDefinition
        {
            IdleDefaultAnimationId = idleId,
            IdleRandomAnimationIds = [idleId, randomId],
            Animations =
            [
                new AnimationScene { Id = idleId, Name = "Idle" },
                new AnimationScene { Id = randomId, Name = "Random" },
                new AnimationScene { Id = unassignedId, Name = "Unassigned" }
            ]
        };

        var candidate = Assert.Single(ScenePlaybackPolicy.GetRandomIdleCandidates(companion));
        Assert.Equal(randomId, candidate.Id);
    }

    [Fact]
    public void RandomIdleCandidates_AreEmptyWhenOnlyDefaultIsAssigned()
    {
        var idleId = Guid.NewGuid();
        var companion = new CompanionDefinition
        {
            IdleDefaultAnimationId = idleId,
            IdleRandomAnimationIds = [idleId],
            Animations = [new AnimationScene { Id = idleId, Name = "Idle" }]
        };

        Assert.Empty(ScenePlaybackPolicy.GetRandomIdleCandidates(companion));
    }

    [Theory]
    [InlineData(AnimationSceneRole.IdleDefault)]
    [InlineData(AnimationSceneRole.Reminder)]
    [InlineData(AnimationSceneRole.ReminderFallback)]
    public void StateDefault_DialogCompletionDoesNotFinishAnimation(AnimationSceneRole role)
    {
        Assert.False(ScenePlaybackPolicy.ShouldFinishLoopWhenDialogEnds(
            role,
            ScenePlaybackMode.LoopUntilDialogEnds));
    }

    [Theory]
    [InlineData(AnimationSceneRole.IdleRandom)]
    [InlineData(AnimationSceneRole.SelfBirthday)]
    [InlineData(AnimationSceneRole.UserBirthday)]
    [InlineData(AnimationSceneRole.Exit)]
    public void LoopingTransientScene_DialogCompletionFinishesAnimation(AnimationSceneRole role)
    {
        Assert.True(ScenePlaybackPolicy.ShouldFinishLoopWhenDialogEnds(
            role,
            ScenePlaybackMode.LoopUntilDialogEnds));
    }

    [Theory]
    [InlineData(AnimationSceneRole.Start)]
    [InlineData(AnimationSceneRole.IdleRandom)]
    public void PlayOnce_DialogCompletionDoesNotRequestLoopFinish(AnimationSceneRole role)
    {
        Assert.False(ScenePlaybackPolicy.ShouldFinishLoopWhenDialogEnds(
            role,
            ScenePlaybackMode.PlayOnce));
    }

    [Fact]
    public void Reminder_WaitsUntilAssignedReminderDialogCompletes()
    {
        Assert.False(ScenePlaybackPolicy.CanPresentReminder(
            AnimationSceneRole.IdleDefault,
            hasPendingSceneDialog: false));
        Assert.False(ScenePlaybackPolicy.CanPresentReminder(
            AnimationSceneRole.Reminder,
            hasPendingSceneDialog: true));
        Assert.True(ScenePlaybackPolicy.CanPresentReminder(
            AnimationSceneRole.Reminder,
            hasPendingSceneDialog: false));
        Assert.True(ScenePlaybackPolicy.CanPresentReminder(
            AnimationSceneRole.ReminderFallback,
            hasPendingSceneDialog: false));
    }

    [Fact]
    public void Reminder_IsReleasedWhenReminderScenePlaybackFails()
    {
        Assert.True(ScenePlaybackPolicy.CanPresentReminder(
            AnimationSceneRole.None,
            hasPendingSceneDialog: true,
            scenePlaybackFailed: true));
    }

    [Theory]
    [InlineData(false, AnimationSceneRole.IdleDefault, true)]
    [InlineData(false, AnimationSceneRole.ReminderFallback, false)]
    [InlineData(false, AnimationSceneRole.ChatReply, false)]
    [InlineData(true, AnimationSceneRole.IdleDefault, false)]
    [InlineData(true, AnimationSceneRole.Start, false)]
    [InlineData(true, AnimationSceneRole.Exit, false)]
    [InlineData(true, AnimationSceneRole.Reminder, true)]
    public void SceneDialogue_FocusAllowsOnlyAssignedReminderState(
        bool focusModeEnabled,
        AnimationSceneRole role,
        bool expected)
    {
        Assert.Equal(
            expected,
            ScenePlaybackPolicy.ShouldPresentSceneDialog(focusModeEnabled, role));
    }

    [Theory]
    [InlineData(AnimationSceneRole.DragLeft)]
    [InlineData(AnimationSceneRole.DragRight)]
    [InlineData(AnimationSceneRole.DragHold)]
    [InlineData(AnimationSceneRole.Click)]
    public void InteractionRoles_DoNotPresentSceneDialogue(AnimationSceneRole role)
    {
        Assert.True(ScenePlaybackPolicy.IsInteractionRole(role));
        Assert.False(ScenePlaybackPolicy.ShouldPresentSceneDialog(
            focusModeEnabled: false,
            role));
    }

    [Theory]
    [InlineData(AnimationSceneRole.IdleDefault, CompanionActivityState.Idle, false, true)]
    [InlineData(AnimationSceneRole.IdleRandom, CompanionActivityState.Idle, false, true)]
    [InlineData(AnimationSceneRole.IdleRandom, CompanionActivityState.Idle, true, false)]
    [InlineData(AnimationSceneRole.IdleDefault, CompanionActivityState.Reminder, false, false)]
    [InlineData(AnimationSceneRole.DragLeft, CompanionActivityState.Idle, false, false)]
    [InlineData(AnimationSceneRole.ChatReply, CompanionActivityState.Idle, false, false)]
    public void ChatCanReplaceSilentIdleAndKeepsTheCurrentTalkLoopAcrossPages(AnimationSceneRole role,
        CompanionActivityState activity, bool dialogue, bool expected) =>
        Assert.Equal(expected, ScenePlaybackPolicy.CanStartChatReply(role, activity, dialogue));
}
