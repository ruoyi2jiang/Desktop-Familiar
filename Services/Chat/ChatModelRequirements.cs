using System.Runtime.InteropServices;
using DesktopFamiliar.Localization;
using DesktopFamiliar.Services.Storage;
using Microsoft.VisualBasic.Devices;

namespace DesktopFamiliar.Services.Chat;

public sealed record ChatModelRequirements(int SuggestedFreeRamMb, int SuggestedThreads, int SetupSpaceMb);

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
        long? disk = forDownload && GetDiskFreeSpaceEx(directory, out var bytes, 0, 0) ? bytes : null;
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

    public string Describe(ChatModelRequirements? requirements, bool forDownload)
    {
        if (!Supported) return LocalizationService.Get("Chat.Unsupported");
        if (requirements == null) return LocalizationService.Get("Chat.Fit.Custom");
        string Size(long? bytes) => bytes.HasValue ? FileSizeFormatter.Format(bytes.Value) : LocalizationService.Get("Chat.Fit.Unavailable");
        var lines = new List<string> {
            LocalizationService.Get("Chat.Fit." + Assess(requirements, forDownload)),
            LocalizationService.Format("Chat.Fit.Hardware", Size(FreeRam), Size(requirements.SuggestedFreeRamMb * 1_000_000L),
                Threads, requirements.SuggestedThreads)
        };
        if (forDownload) lines.Add(LocalizationService.Format("Chat.Fit.Disk", Size(FreeDisk), Size(requirements.SetupSpaceMb * 1_000_000L)));
        return string.Join(Environment.NewLine, lines);
    }
}
