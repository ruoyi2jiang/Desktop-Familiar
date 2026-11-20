using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public class ModelClipTimelineTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AmbiguousGroupIdsInAnExistingLibraryLeaveTheirPartsVisible(bool duplicateIsGroup)
    {
        var group = new AnimationScene { ModelParts = ["loop"] };
        var duplicate = duplicateIsGroup ? group.Clone() : new AnimationScene { Id = group.Id };
        var child = new AnimationScene { ModelClip = "loop", PartOfSceneId = group.Id };
        var model = new ModelAppearance { Animations = [group, duplicate, child] };
        Assert.Equal(model.Animations, ModelAnimationCatalog.VisibleScenes(model, showParts: false).ToList());
        Assert.Equal(model.Animations, ModelAnimationCatalog.VisibleScenes(model, showParts: true).ToList());
    }

    private static ModelClipTimeline Make(ModelLoopMode mode, bool loop = false) => new(new AnimationScene {
        ModelParts = ["start", "middle", "end"], ModelLoop = new() {
            Mode = mode, StartSeconds = 1, EndSeconds = 3, DurationSeconds = 5 } },
        name => name == "middle" ? 2 : 1, loop);

    [Fact]
    public void OneActivitySupportsScrubbingOnceAndFixedDuration()
    {
        var once = Make(ModelLoopMode.Once);
        Assert.Equal(4, once.Duration);
        Assert.Equal("middle", once.SampleSource(1.3).Clip);
        Assert.Equal("end", once.Sample(3).Clip);
        Assert.True(once.Sample(4).Complete);
        var timed = Make(ModelLoopMode.FixedDuration);
        Assert.Equal("middle", timed.Sample(5.5).Clip);
        Assert.Equal(0.5, timed.Sample(5.5).Time);
        Assert.Equal("end", timed.Sample(6).Clip);
        Assert.True(timed.Sample(7).Complete);
    }

    [Theory]
    [InlineData(0.25, 3)]
    [InlineData(5.5, 7)]
    [InlineData(5, 5)]
    public void DialogueReleaseCompletesCurrentMiddleCycleAndOutro(double at, double outro)
    {
        var timeline = Make(ModelLoopMode.UntilDialogueEnds, true);
        Assert.True(timeline.FinishLoop(at));
        Assert.True(timeline.FinishLoop(at + 0.5));
        Assert.Equal("middle", timeline.Sample(outro - 0.01).Clip);
        Assert.Equal("end", timeline.Sample(outro).Clip);
        Assert.True(timeline.Sample(outro + 1).Complete);
    }

    [Fact]
    public void CustomRangeCanCrossASourceBoundaryAndCatalogueRequiresAllParts()
    {
        var source = new AnimationScene { ModelParts = ["first", "second"],
            ModelLoop = new() { StartSeconds = 0.5, EndSeconds = 1.5, DurationSeconds = 3, Mode = ModelLoopMode.FixedDuration } };
        var timeline = new ModelClipTimeline(source, _ => 1, false);
        Assert.Equal("second", timeline.Sample(1.4).Clip);
        Assert.Equal("first", timeline.Sample(1.6).Clip);
        Assert.Equal(0.5, timeline.Sample(3.5).Time);
        var clips = new Dictionary<string, double> { ["rest_start"] = 1, ["rest_loop"] = 2, ["rest_end"] = 3, ["fishing_start"] = 1 };
        var full = Assert.Single(ModelAnimationCatalog.List(clips), item => item.Combined);
        Assert.Equal(["rest_start","rest_loop","rest_end"], full.Clips);
        Assert.Equal(3, full.LoopEnd);
        Assert.False(ModelClipTimeline.ValidSettings(new() { StartSeconds = 1, EndSeconds = 0.5 }, 3));
    }

    [Fact]
    public void PartsStayAccessibleAndReleaseOfARestPoseNeedsNoOutro()
    {
        var full = new AnimationScene { Name = "Rest", ModelParts = ["in", "wait", "out"] };
        var part = new AnimationScene { Name = "Rest middle", ModelClip = "wait", PartOfSceneId = full.Id };
        var other = new AnimationScene { Name = "Another use of the same clip", ModelClip = "wait" };
        var scenes = new ModelAppearance { Animations = [part, full, other] };
        Assert.Equal([full, other], ModelAnimationCatalog.VisibleScenes(scenes, false).ToList());
        Assert.Equal([full, part, other], ModelAnimationCatalog.VisibleScenes(scenes, true).ToList());
        scenes.Animations.Remove(full);
        Assert.Contains(part, ModelAnimationCatalog.VisibleScenes(scenes, false));
        var rest = new ModelClipTimeline(new() { ModelLoop = new() { Mode = ModelLoopMode.UntilDialogueEnds } }, _ => 0, true);
        Assert.False(rest.FinishLoop(2));
    }

    [Theory]
    [InlineData(AnimationSceneRole.Start, CompanionActivityState.Idle, true)]
    [InlineData(AnimationSceneRole.Start, CompanionActivityState.Reminder, false)]
    [InlineData(AnimationSceneRole.Click, CompanionActivityState.Idle, false)]
    public void OnlyTheFinishedEntranceRunsIdleBeneathItsGreeting(AnimationSceneRole role, CompanionActivityState state, bool expected) =>
        Assert.Equal(expected, ScenePlaybackPolicy.BeginIdleUnderDialogue(role, state));

    [Fact]
    public void CuesUseSceneTimeAndCanInterruptAutomaticLines()
    {
        var scene = new AnimationScene { DialogStartSeconds = 0.4, DialogLines = [
            new(), new() { Text = "first" }, new() { Text = "automatic" },
            new() { Text = "on the beat", StartSeconds = 1.0 }, new() { Text = "next" }] };
        Assert.True(ModelDialogueTiming.HasValidCues(scene));
        Assert.Equal([(1, 0.4), (3, 1d)], ModelDialogueTiming.Cues(scene).ToArray());
        var captions = ModelDialogueTiming.PreviewCaptions(scene, text => text);
        Assert.Equal(["first", "on the beat", "next"], captions.Select(caption => caption.Text));
        Assert.Equal(1, captions[0].End);
        Assert.Equal(captions[1].End, captions[2].Start);
        scene.DialogLines[^1].StartSeconds = 0.9;
        Assert.False(ModelDialogueTiming.HasValidCues(scene));
    }
}
