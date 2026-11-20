using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Tests;

public sealed class FrameAnimationPlayerTests
{
    [Fact]
    public void ClearThenPlay_ReplacesRetainedFrameSynchronously()
    {
        var oldFrame = CreateFrame(16);
        var startFrame = CreateFrame(240);
        var presented = new List<BitmapSource?>();
        var loader = new QueueFrameLoader(CreateAnimation(oldFrame), CreateAnimation(startFrame));
        using var player = new FrameAnimationPlayer(presented.Add, loader);

        player.Play(new AnimationScene(), loop: true);
        player.Stop(clearFrame: true);
        player.Play(new AnimationScene(), loop: true);

        Assert.Collection(
            presented,
            frame => Assert.Same(oldFrame, frame),
            Assert.Null,
            frame => Assert.Same(startFrame, frame));
    }

    private static LoadedAnimationFrames CreateAnimation(BitmapSource frame) =>
        new([frame], [TimeSpan.FromSeconds(1)]);

    [Fact]
    public void PreparedOpeningFramesPlayWithoutReadingTheSourceAgain()
    {
        var frame = CreateFrame(120);
        using var player = new FrameAnimationPlayer(shown => { if (shown != null) Assert.Same(frame, shown); },
            new QueueFrameLoader());
        player.Play(new AnimationScene(), loop: true, CreateAnimation(frame));
        Assert.Equal(1, player.FrameCount);
    }

    private static BitmapSource CreateFrame(byte value)
    {
        var frame = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Gray8,
            palette: null,
            new byte[] { value },
            stride: 1);
        frame.Freeze();
        return frame;
    }

    private sealed class QueueFrameLoader(params LoadedAnimationFrames[] animations) : IAnimationFrameLoader
    {
        private int _index;

        public LoadedAnimationFrames Load(string path) => Next();
        public LoadedAnimationFrames Load(AnimationScene scene) => Next();

        private LoadedAnimationFrames Next() => animations[_index++];
    }
}
