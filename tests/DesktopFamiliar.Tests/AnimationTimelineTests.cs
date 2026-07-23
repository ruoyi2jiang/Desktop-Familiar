using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public sealed class AnimationTimelineTests
{
    [Fact]
    public void SelectedRange_PlaysIntroLoopsMiddleThenFinishesOutro()
    {
        var timeline = new AnimationTimeline(6, loop: true, loopStartIndex: 2, loopEndIndex: 3);

        Assert.Equal(1, timeline.Advance().FrameIndex);
        Assert.Equal(2, timeline.Advance().FrameIndex);
        Assert.Equal(3, timeline.Advance().FrameIndex);

        var looped = timeline.Advance();
        Assert.Equal(2, looped.FrameIndex);
        Assert.True(looped.CycleCompleted);
        Assert.False(looped.AnimationCompleted);

        Assert.True(timeline.TryFinishLoop());
        Assert.Equal(3, timeline.Advance().FrameIndex);
        Assert.Equal(4, timeline.Advance().FrameIndex);
        Assert.False(timeline.IsLooping);
        Assert.Equal(5, timeline.Advance().FrameIndex);
        Assert.True(timeline.Advance().AnimationCompleted);
    }

    [Fact]
    public void StaticLoop_NeverCompletes()
    {
        var timeline = new AnimationTimeline(1, loop: true);

        var result = timeline.Advance();

        Assert.Equal(0, result.FrameIndex);
        Assert.False(result.FrameChanged);
        Assert.False(result.AnimationCompleted);
        Assert.True(timeline.IsStaticLoop);
    }

    [Fact]
    public void SingleFrameOneShot_CompletesOnFirstAdvance()
    {
        var result = new AnimationTimeline(1, loop: false).Advance();

        Assert.True(result.CycleCompleted);
        Assert.True(result.AnimationCompleted);
    }

    [Fact]
    public void InvalidLoopRange_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnimationTimeline(3, loop: true, loopStartIndex: 2, loopEndIndex: 3));
    }

    [Fact]
    public void WholeAnimationLoop_FinishesAtEndOfCurrentPass()
    {
        var timeline = new AnimationTimeline(3, loop: true);
        Assert.Equal(1, timeline.Advance().FrameIndex);
        Assert.True(timeline.TryFinishLoop());
        Assert.Equal(2, timeline.Advance().FrameIndex);

        var completed = timeline.Advance();

        Assert.True(completed.AnimationCompleted);
        Assert.False(timeline.IsLooping);
        Assert.Equal(2, completed.FrameIndex);
    }

    [Fact]
    public void SelectedLastFrame_CanFinishWithoutOutro()
    {
        var timeline = new AnimationTimeline(3, loop: true, loopStartIndex: 2, loopEndIndex: 2);
        timeline.Advance();
        timeline.Advance();
        Assert.True(timeline.TryFinishLoop());

        Assert.True(timeline.Advance().AnimationCompleted);
    }

    [Theory]
    [InlineData(AnimationFrameLoopMode.EntireAnimation, 1, 3, false, 0, 0)]
    [InlineData(AnimationFrameLoopMode.SelectedRange, 1, 3, true, 1, 3)]
    [InlineData(AnimationFrameLoopMode.SingleFrame, 2, 4, true, 2, 2)]
    public void SceneLoopMode_ResolvesExpectedRange(
        AnimationFrameLoopMode mode,
        int configuredStart,
        int configuredEnd,
        bool expectedRange,
        int expectedStart,
        int expectedEnd)
    {
        var scene = new AnimationScene
        {
            FrameLoopMode = mode,
            LoopStartFrameIndex = configuredStart,
            LoopEndFrameIndex = configuredEnd
        };

        var hasRange = scene.TryGetLoopRange(5, out var start, out var end);

        Assert.Equal(expectedRange, hasRange);
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }

    [Fact]
    public void DialogStartFrame_MustBeBeforeSelectedLoopRepeats()
    {
        var scene = new AnimationScene
        {
            FrameLoopMode = AnimationFrameLoopMode.SelectedRange,
            LoopStartFrameIndex = 2,
            LoopEndFrameIndex = 4
        };

        Assert.Equal(4, scene.GetLastFrameReachableBeforeLoopRepeats(7));
    }
}
