using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Services;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static bool _initialized;

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopFamiliar");
    public static string LogDirectory { get; } = Path.Combine(RootDirectory, "Logs");
    public static string LogFilePath { get; } = Path.Combine(LogDirectory, "latest.log");
    public static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    public static void Initialize()
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var header = new StringBuilder()
                    .AppendLine("Desktop Familiar session log")
                    .AppendLine($"Started: {DateTimeOffset.Now:O}")
                    .AppendLine($"App version: {AppVersion}")
                    .AppendLine($"OS: {RuntimeInformation.OSDescription}")
                    .AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}")
                    .AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}")
                    .AppendLine($"Executable: {Environment.ProcessPath ?? "unknown"}")
                    .AppendLine($"Data directory: {RootDirectory}")
                    .AppendLine(new string('-', 78))
                    .ToString();
                File.WriteAllText(LogFilePath, header, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                _initialized = true;
            }
            catch
            {
                _initialized = false;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception exception) => Write("ERROR", message, exception);

    public static void Fatal(string context, Exception exception) => Write("FATAL", context, exception);

    public static string ReadCurrentLog()
    {
        lock (Sync)
        {
            try
            {
                return File.Exists(LogFilePath)
                    ? File.ReadAllText(LogFilePath)
                    : LocalizationService.Get("Crash.LogMissing");
            }
            catch (Exception exception)
            {
                return LocalizationService.Format("Crash.LogReadFailed", exception.Message);
            }
        }
    }

    public static string BuildCrashReport()
    {
        return LocalizationService.Get("Crash.Report.Title") + "\n" +
               LocalizationService.Format("Crash.Report.Version", AppVersion) + "\n" +
               LocalizationService.Format("Crash.Report.Generated", $"{DateTimeOffset.Now:O}") + "\n" +
               LocalizationService.Format("Crash.Report.LogFile", LogFilePath) + "\n\n" +
               ReadCurrentLog();
    }

    private static void Write(string level, string message, Exception? exception = null)
    {
        lock (Sync)
        {
            try
            {
                if (!_initialized)
                    Initialize();
                var entry = new StringBuilder()
                    .AppendLine($"[{DateTimeOffset.Now:O}] [{level}] [Thread {Environment.CurrentManagedThreadId}] {message}");
                if (exception is not null)
                    AppendException(entry, exception, 0);
                File.AppendAllText(LogFilePath, entry.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch
            {
                // Logging must never trigger another application failure.
            }
        }
    }

    private static void AppendException(StringBuilder output, Exception exception, int depth)
    {
        var indent = new string(' ', depth * 2);
        output.AppendLine($"{indent}Exception: {exception.GetType().FullName}");
        output.AppendLine($"{indent}Message: {exception.Message}");
        output.AppendLine($"{indent}HResult: 0x{exception.HResult:X8}");
        if (!string.IsNullOrWhiteSpace(exception.Source))
            output.AppendLine($"{indent}Source: {exception.Source}");
        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            output.AppendLine($"{indent}Stack trace:\n{exception.StackTrace}");
        if (exception.Data.Count > 0)
        {
            output.AppendLine($"{indent}Data:");
            foreach (System.Collections.DictionaryEntry item in exception.Data)
                output.AppendLine($"{indent}  {item.Key}: {item.Value}");
        }
        if (exception.InnerException is not null)
        {
            output.AppendLine($"{indent}Inner exception:");
            AppendException(output, exception.InnerException, depth + 1);
        }
    }
}
