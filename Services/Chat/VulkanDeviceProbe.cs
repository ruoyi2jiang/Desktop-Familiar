using System.Diagnostics;

namespace DesktopFamiliar.Services.Chat;

internal static class VulkanDeviceProbe
{
    public static async Task<bool> IsAvailableAsync(string enginePath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(enginePath, "--list-devices")
        {
            WorkingDirectory = Path.GetDirectoryName(enginePath)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        };
        using var process = Process.Start(start) ?? throw new IOException("Cannot list llama.cpp devices.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var listing = await process.StandardOutput.ReadToEndAsync(limit.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return process.ExitCode == 0 && listing.Split('\n').Any(line => line.TrimStart().StartsWith("Vulkan0:", StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Vulkan device enumeration timed out.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
    }
}
