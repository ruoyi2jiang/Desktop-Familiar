namespace DesktopFamiliar.Localization;

internal static class FileSizeFormatter
{
    public static string Format(long bytes) => bytes switch
    {
        < 1_000 => LocalizationService.Format("FileSize.Bytes", bytes),
        < 1_000_000 => LocalizationService.Format("FileSize.Kilobytes", bytes / 1_000m),
        < 1_000_000_000 => LocalizationService.Format("FileSize.Megabytes", bytes / 1_000_000m),
        _ => LocalizationService.Format("FileSize.Gigabytes", bytes / 1_000_000_000m)
    };
}
