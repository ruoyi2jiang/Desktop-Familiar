using Microsoft.Win32;
using DesktopFamiliar.Localization;

namespace DesktopFamiliar.Services;

internal interface IStartupRegistration
{
    bool IsEnabled();
    // The caller can undo this change if saving the rest of the settings fails.
    Action SetEnabled(bool enabled);
}

internal sealed class StartupService(string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run") : IStartupRegistration
{
    private const string ValueName = "Desktop Familiar";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public Action SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true);
        var previous = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var previousKind = previous is null ? RegistryValueKind.None : key.GetValueKind(ValueName);
        var command = enabled ? GetLaunchCommand() : null;
        WriteValue(key, command, RegistryValueKind.String);

        return () =>
        {
            using var rollbackKey = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true);
            var current = rollbackKey.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (Equals(current, command) && (current is null || rollbackKey.GetValueKind(ValueName) == RegistryValueKind.String))
                WriteValue(rollbackKey, previous, previousKind);
        };
    }

    private static void WriteValue(RegistryKey key, object? value, RegistryValueKind kind)
    {
        if (value is null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else key.SetValue(ValueName, value, kind);
    }

    private static string GetLaunchCommand()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(LocalizationService.Get("Startup.ExecutableUnavailable"));
        var command = $"\"{executable}\"";
        if (string.Equals(Path.GetFileName(executable), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            command += $" \"{Path.Combine(AppContext.BaseDirectory, "DesktopFamiliar.dll")}\"";
        return command;
    }
}
