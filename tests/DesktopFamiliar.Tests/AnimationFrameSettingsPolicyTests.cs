using DesktopFamiliar.Models;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public sealed class AnimationFrameSettingsPolicyTests
{
    [Fact]
    public void IndividualFrameSettingsRequireImportedPngFrames()
    {
        Assert.False(AnimationFrameSettingsPolicy.SupportsIndividualFrames(null));
        Assert.False(AnimationFrameSettingsPolicy.SupportsIndividualFrames(new AnimationScene()));
        Assert.False(AnimationFrameSettingsPolicy.SupportsIndividualFrames(new AnimationScene
        {
            AnimationPath = "animation.gif"
        }));
        Assert.True(AnimationFrameSettingsPolicy.SupportsIndividualFrames(new AnimationScene
        {
            PngFrames = [new PngAnimationFrame { FilePath = "frame.png" }]
        }));
    }

    [Fact]
    public void SwitchingToFileSourceResetsEveryIndividualFrameSetting()
    {
        var scene = CreateConfiguredScene();
        scene.PngFrames.Clear();
        scene.AnimationPath = "animation.gif";

        Assert.True(AnimationFrameSettingsPolicy.ResetIfUnsupported(scene));
        Assert.Equal(AnimationFrameLoopMode.EntireAnimation, scene.FrameLoopMode);
        Assert.Equal(0, scene.LoopStartFrameIndex);
        Assert.Equal(0, scene.LoopEndFrameIndex);
        Assert.Equal(0, scene.DialogStartFrameIndex);
    }

    [Fact]
    public void SwitchingFromFileSourceToPngCanResetEveryIndividualFrameSetting()
    {
        var scene = CreateConfiguredScene();
        scene.PngFrames.Clear();
        scene.AnimationPath = "animation.gif";

        Assert.True(AnimationFrameSettingsPolicy.Reset(scene));
        scene.AnimationPath = string.Empty;
        scene.PngFrames.Add(new PngAnimationFrame { FilePath = "replacement.png" });

        Assert.Equal(AnimationFrameLoopMode.EntireAnimation, scene.FrameLoopMode);
        Assert.Equal(0, scene.LoopStartFrameIndex);
        Assert.Equal(0, scene.LoopEndFrameIndex);
        Assert.Equal(0, scene.DialogStartFrameIndex);
    }

    [Fact]
    public void PngSourcePreservesIndividualFrameSettings()
    {
        var scene = CreateConfiguredScene();

        Assert.False(AnimationFrameSettingsPolicy.ResetIfUnsupported(scene));
        Assert.Equal(AnimationFrameLoopMode.SelectedRange, scene.FrameLoopMode);
        Assert.Equal(1, scene.LoopStartFrameIndex);
        Assert.Equal(2, scene.LoopEndFrameIndex);
        Assert.Equal(1, scene.DialogStartFrameIndex);
    }

    [Fact]
    public void RuntimeIgnoresHiddenFrameSettingsForFileSource()
    {
        var scene = CreateConfiguredScene();
        scene.PngFrames.Clear();
        scene.AnimationPath = "animation.gif";

        Assert.False(AnimationFrameSettingsPolicy.TryGetRuntimeLoopRange(
            scene,
            frameCount: 4,
            out _,
            out _));
        Assert.Equal(0, AnimationFrameSettingsPolicy.GetRuntimeDialogStartFrame(scene));
    }

    private static AnimationScene CreateConfiguredScene() => new()
    {
        PngFrames =
        [
            new PngAnimationFrame { FilePath = "frame-1.png" },
            new PngAnimationFrame { FilePath = "frame-2.png" },
            new PngAnimationFrame { FilePath = "frame-3.png" }
        ],
        FrameLoopMode = AnimationFrameLoopMode.SelectedRange,
        LoopStartFrameIndex = 1,
        LoopEndFrameIndex = 2,
        DialogStartFrameIndex = 1
    };
}
