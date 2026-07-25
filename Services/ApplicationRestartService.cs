using System.Diagnostics;

namespace DesktopFamiliar.Services;

/// <summary>Starts a replacement process using the current process command line.</summary>
public interface IApplicationRestartService
{
    void RestartCurrentProcess();
}

public sealed class ApplicationRestartService : IApplicationRestartService
{
    public void RestartCurrentProcess()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current application executable path is unavailable.");
        var startInfo = CreateStartInfo(
            executablePath,
            Environment.GetCommandLineArgs().Skip(1),
            AppContext.BaseDirectory);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("The replacement Desktop Familiar process could not be started.");
    }

    public static ProcessStartInfo CreateStartInfo(
        string executablePath,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
