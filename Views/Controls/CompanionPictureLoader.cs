using System.Windows.Media.Imaging;
using DesktopFamiliar.Services;

namespace DesktopFamiliar.Views.Controls;

internal static class CompanionPictureLoader
{
    public static ImageSource? Load(string? portrait, string? idleFramePath = null) =>
        LoadImage(portrait) ?? LoadImage(idleFramePath);

    private static ImageSource? LoadImage(string? path)
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
