using System.Runtime.InteropServices;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services.Storage;
using Microsoft.VisualBasic.Devices;

namespace DesktopFamiliar.Services.Chat;

public sealed record ChatModelRequirements(int SuggestedFreeRamMb, int SuggestedThreads, int SetupSpaceMb, int SuggestedFreeVramMb)
{
    internal string Describe(bool forDownload)
    {
        static string Size(int mb) => FileSizeFormatter.Format(mb * 1_000_000L);
        return LocalizationService.Format(forDownload ? "Chat.Requirements.Download" : "Chat.Requirements.Installed",
            Size(SuggestedFreeRamMb), SuggestedThreads, Size(SuggestedFreeVramMb), Size(SetupSpaceMb));
    }
}

internal enum ModelFit { Unsupported, LowDisk, Limited, Unknown, Recommended }

internal sealed record ChatModelPc(bool Supported, int Threads, long? FreeRam, long? FreeDisk)
{
    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out long available, nint total, nint free);

    public static ChatModelPc Read(string directory, bool forDownload)
    {
        long? ram = null;
        try { ram = (long)new ComputerInfo().AvailablePhysicalMemory; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLogger.Warning("Available physical memory could not be read for the model check.", ex);
        }
        // A new installation may not have created its data folder yet.
        var driveDirectory = forDownload ? new DirectoryInfo(directory) : null;
        while (driveDirectory != null && !driveDirectory.Exists) driveDirectory = driveDirectory.Parent;
        long? disk = driveDirectory != null && GetDiskFreeSpaceEx(driveDirectory.FullName, out var bytes, 0, 0) ? bytes : null;
        return new(ChatModelStore.Supported, Environment.ProcessorCount, ram, disk);
    }

    public ModelFit Assess(ChatModelRequirements requirements, bool forDownload)
    {
        if (!Supported) return ModelFit.Unsupported;
        if (forDownload && FreeDisk < requirements.SetupSpaceMb * 1_000_000L) return ModelFit.LowDisk;
        if (FreeRam < requirements.SuggestedFreeRamMb * 1_000_000L || Threads < requirements.SuggestedThreads)
            return ModelFit.Limited;
        if (FreeRam == null || (forDownload && FreeDisk == null)) return ModelFit.Unknown;
        return ModelFit.Recommended;
    }
}
