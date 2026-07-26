using System.Diagnostics;
using System.Globalization;

namespace DesktopFamiliar.Services;

/// <summary>Starts a replacement process using the current process command line.</summary>
public interface IApplicationRestartService
{
    void RestartCurrentProcess();
}

public sealed class ApplicationRestartService : IApplicationRestartService
{
    public const string RestartWaitArgument = "--desktop-familiar-restart-wait-pid";
    private const int RestartWaitTimeoutMilliseconds = 30_000;

    public void RestartCurrentProcess()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current application executable path is unavailable.");
        var startInfo = CreateRestartStartInfo(
            executablePath,
            Environment.GetCommandLineArgs().Skip(1),
            AppContext.BaseDirectory,
            Environment.ProcessId);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("The replacement Desktop Familiar process could not be started.");
    }

    public static ProcessStartInfo CreateRestartStartInfo(
        string executablePath,
        IEnumerable<string> arguments,
        string workingDirectory,
        int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        var originalArguments = arguments.ToArray();
        var filteredArguments = new List<string>(originalArguments.Length);
        for (var index = 0; index < originalArguments.Length; index++)
        {
            if (!string.Equals(originalArguments[index], RestartWaitArgument, StringComparison.Ordinal))
            {
                filteredArguments.Add(originalArguments[index]);
                continue;
            }

            if (index + 1 < originalArguments.Length)
                index++;
        }

        var startInfo = CreateStartInfo(executablePath, filteredArguments, workingDirectory);
        startInfo.ArgumentList.Add(RestartWaitArgument);
        startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        return startInfo;
    }

    public static bool WaitForRestartParent(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = arguments.ToArray();
        int? processId = null;
        for (var index = 0; index + 1 < values.Length; index++)
        {
            if (string.Equals(values[index], RestartWaitArgument, StringComparison.Ordinal)
                && int.TryParse(values[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                processId = parsed;
            }
        }

        if (processId is null || processId == Environment.ProcessId)
            return true;

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            return process.HasExited || process.WaitForExit(RestartWaitTimeoutMilliseconds);
        }
        catch (ArgumentException)
        {
            return true;
        }
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
