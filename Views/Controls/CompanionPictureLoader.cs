using System.Windows.Media.Imaging;
using DesktopFamiliar.Models;
using DesktopFamiliar.Services;
using DesktopFamiliar.Services.Animation;

namespace DesktopFamiliar.Views.Controls;

internal static class CompanionPictureLoader
{
    private sealed record SnapshotKey(string Path, string? Clip, DateTime LastWrite, long Length);
    private static readonly Dictionary<SnapshotKey, BitmapSource> Snapshots = new();

    public static ImageSource? Load(CompanionPortrait? portrait, Func<BitmapSource>? captureIdle = null)
    {
        if (portrait == null) return null;
        if (LoadImage(portrait.PicturePath) is { } imported) return imported;
        if (portrait.ModelPath is not { } path) return LoadImage(portrait.IdleFramePath);

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            var key = new SnapshotKey(file.FullName, portrait.ModelClip, file.LastWriteTimeUtc, file.Length);
            lock (Snapshots)
            {
                if (Snapshots.TryGetValue(key, out var cached)) return cached;
                var frame = captureIdle?.Invoke() ?? CompanionModelView.CaptureFirstFrame(path, key.Clip);
                if (Snapshots.Count >= 32) Snapshots.Remove(Snapshots.Keys.First());
                Snapshots.Add(key, frame);
                return frame;
            }
        }
        catch (Exception ex) when (GlbModelFile.IsLoadError(ex) || ex is System.Runtime.InteropServices.COMException)
        {
            AppLogger.Warning($"Could not render companion portrait from '{path}'.", ex);
            return null;
        }
    }

    public static ImageSource? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            frame?.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                    or FormatException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            AppLogger.Warning($"Could not load companion picture '{path}'.", ex);
            return null;
        }
    }
}
