using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public sealed class ScenePlaybackPolicyTests
{
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
}
