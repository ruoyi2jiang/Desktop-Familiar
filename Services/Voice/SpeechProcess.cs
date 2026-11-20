using System.Diagnostics;

namespace DesktopFamiliar.Services.Voice;

internal static class SpeechProcess
{
    public static async Task<string> RunAsync(string exe, IEnumerable<string> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8 };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var child = Process.Start(info) ?? throw new IOException("Could not start the offline speech engine.");
        using var stop = token.Register(() =>
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Already exited. */ }
            catch (System.ComponentModel.Win32Exception ex) { AppLogger.Warning("Could not interrupt the speech process.", ex); }
        });
        var output = child.StandardOutput.ReadToEndAsync();
        var diagnostics = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(output, diagnostics).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Engine diagnostics can contain private transcribed text.
        if (child.ExitCode != 0) throw new IOException($"The speech engine exited with code {child.ExitCode}.");
        return await output.ConfigureAwait(false);
    }
}
