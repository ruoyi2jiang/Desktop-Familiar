using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopFamiliar.Services.Updates;

internal static class AppUpdateHandoff
{
    // An SDK build has a separate managed DLL. Only a published, bundled Windows app can replace itself.
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty assembly location identifies the bundled executable.")]
    public static AppDeployment GetDeployment()
    {
        if (RuntimeInformation.OSArchitecture != Architecture.X64 || !string.IsNullOrEmpty(typeof(App).Assembly.Location)
            || Environment.ProcessPath is not { } executable)
            return AppDeployment.SaveOnly;

        try
        {
            // The per-user, 64-bit Inno Setup installer registers this AppId, including for custom folders.
            using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var uninstall = user.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{92AE6C24-0462-4D93-B029-5F9DF31B4B56}_is1");
            if (uninstall == null) return AppDeployment.Portable;
            return ClassifyRegisteredPath(executable, uninstall.GetValue("InstallLocation") as string);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLogger.Warning("Could not identify the registered installation for app updates.", exception);
            return AppDeployment.SaveOnly;
        }
    }

    internal static AppDeployment ClassifyRegisteredPath(string executable, string? installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory) || !Path.IsPathFullyQualified(installDirectory))
            return AppDeployment.SaveOnly;
        var registered = Path.Combine(installDirectory, "DesktopFamiliar.exe");
        return string.Equals(registered, executable, StringComparison.OrdinalIgnoreCase)
            ? AppDeployment.Installed : AppDeployment.Portable;
    }

    public static void Start(string stagedPath, AppReleaseAsset asset, string failureTitle, string failureMessage)
    {
        if (!asset.CanApplyTo(GetDeployment())) throw new InvalidOperationException("Update does not match this deployment.");
        var start = CreateStartInfo(Environment.ProcessId, Environment.ProcessPath!, stagedPath,
            asset, failureTitle, failureMessage);
        using var worker = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
    }

    internal static ProcessStartInfo CreateStartInfo(int parentId, string target, string stagedPath,
        AppReleaseAsset asset, string failureTitle, string failureMessage)
    {
        using var script = typeof(AppUpdateHandoff).Assembly.GetManifestResourceStream("DesktopFamiliar.Updates.ApplyAppUpdate.ps1")
            ?? throw new InvalidOperationException("Missing embedded update helper.");
        using var reader = new StreamReader(script);
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.SystemDirectory
        };
        var scriptText = reader.ReadToEnd();
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
            "try { & {\n" + scriptText + "\n} } catch {\n" + ReportFailure + "\n}" })
            info.ArgumentList.Add(arg);
        info.Environment["DF_UPDATE_PARENT"] = parentId.ToString(CultureInfo.InvariantCulture);
        info.Environment["DF_UPDATE_TARGET"] = Path.GetFullPath(target);
        info.Environment["DF_UPDATE_STAGE"] = Path.GetFullPath(stagedPath);
        info.Environment["DF_UPDATE_SHA256"] = asset.Sha256;
        info.Environment["DF_UPDATE_KIND"] = asset.IsInstaller ? "installer" : "portable";
        info.Environment["DF_UPDATE_TITLE"] = failureTitle;
        info.Environment["DF_UPDATE_ERROR"] = failureMessage;
        return info;
    }

    private const string ReportFailure = """
        Add-Type -AssemblyName PresentationFramework
        $message = $env:DF_UPDATE_ERROR + [Environment]::NewLine + $_.Exception.ToString()
        [void][Windows.MessageBox]::Show($message, $env:DF_UPDATE_TITLE)
        exit 1
        """;
}
