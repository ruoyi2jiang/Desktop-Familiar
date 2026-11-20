using System.Windows.Media.Imaging;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Models;

namespace DesktopFamiliar.Services.Animation;

public sealed record LoadedAnimationFrames(
    IReadOnlyList<BitmapSource> Frames,
    IReadOnlyList<TimeSpan> Delays);

public interface IAnimationFrameLoader
{
    LoadedAnimationFrames Load(string path);
    LoadedAnimationFrames Load(AnimationScene scene);
}

/// <summary>
/// Owns WPF image decoding and frame-duration extraction. Playback timing and
/// state transitions live in <see cref="AnimationTimeline"/>.
/// </summary>
public sealed class WpfAnimationFrameLoader : IAnimationFrameLoader
{
    public LoadedAnimationFrames Load(AnimationScene scene) => Load(scene, default);

    internal LoadedAnimationFrames Load(AnimationScene scene, CancellationToken cancellation) => scene.PngFrames.Count > 0
        ? LoadPngSequence(scene.PngFrames, cancellation)
        : Load(scene.AnimationPath, cancellation);

    public LoadedAnimationFrames Load(string path) => Load(path, default);

    private static LoadedAnimationFrames Load(string path, CancellationToken cancellation)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(LocalizationService.Get("Animation.FileMissing"), path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        BitmapDecoder decoder = Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)
            ? new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad)
            : BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var frames = new List<BitmapSource>();
        var delays = new List<TimeSpan>();
        foreach (var sourceFrame in decoder.Frames)
        {
            cancellation.ThrowIfCancellationRequested();
            var frame = BitmapFrame.Create(sourceFrame);
            frame.Freeze();
            frames.Add(frame);
            delays.Add(ReadGifDelay(sourceFrame.Metadata as BitmapMetadata));
        }

        if (frames.Count == 0)
            throw new InvalidDataException(LocalizationService.Get("Animation.NoReadableFrames"));
        return new LoadedAnimationFrames(frames, delays);
    }

    private static LoadedAnimationFrames LoadPngSequence(IEnumerable<PngAnimationFrame> sequence, CancellationToken cancellation)
    {
        var frames = new List<BitmapSource>();
        var delays = new List<TimeSpan>();
        foreach (var item in sequence)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Path.GetExtension(item.FilePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(LocalizationService.Format("Animation.NotPngFrame", item.FileName));
            if (!File.Exists(item.FilePath))
                throw new FileNotFoundException(LocalizationService.Get("Animation.PngFrameMissing"), item.FilePath);
            if (item.DurationSeconds is < 0.01 or > 60)
                throw new InvalidDataException(LocalizationService.Format("Animation.InvalidFrameDuration", item.FileName));

            using var stream = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                throw new InvalidDataException(LocalizationService.Format("Animation.PngNoReadableFrame", item.FileName));
            var frame = BitmapFrame.Create(decoder.Frames[0]);
            frame.Freeze();
            frames.Add(frame);
            delays.Add(TimeSpan.FromSeconds(item.DurationSeconds));
        }

        if (frames.Count == 0)
            throw new InvalidDataException(LocalizationService.Get("Animation.NoReadableFrames"));
        return new LoadedAnimationFrames(frames, delays);
    }

    private static TimeSpan ReadGifDelay(BitmapMetadata? metadata)
    {
        try
        {
            if (metadata?.GetQuery("/grctlext/Delay") is object raw)
            {
                var hundredths = Convert.ToInt32(raw);
                return TimeSpan.FromMilliseconds(Math.Clamp(hundredths * 10, 20, 10_000));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning("A GIF frame delay could not be read; 100 ms will be used.", exception);
        }
        return TimeSpan.FromMilliseconds(100);
    }
}
