using DesktopFamiliar.Services;

namespace DesktopFamiliar.Tests;

public sealed class ApplicationRestartServiceTests
{
    [Fact]
    public void CreateStartInfo_PreservesTheOriginalCommandLine()
    {
        var startInfo = ApplicationRestartService.CreateStartInfo(
            "DesktopFamiliar.exe",
            ["--profile", "Test profile"],
            "C:\\Desktop Familiar");

        Assert.Equal("DesktopFamiliar.exe", startInfo.FileName);
        Assert.Equal("C:\\Desktop Familiar", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(["--profile", "Test profile"], startInfo.ArgumentList);
    }

    [Fact]
    public void CreateRestartStartInfo_ReplacesAStaleWaitArgumentAndWaitsForTheCurrentProcess()
    {
        var startInfo = ApplicationRestartService.CreateRestartStartInfo(
            "DesktopFamiliar.exe",
            ["--profile", "Test profile", ApplicationRestartService.RestartWaitArgument, "42"],
            "C:\\Desktop Familiar",
            1234);

        Assert.Equal(
            ["--profile", "Test profile", ApplicationRestartService.RestartWaitArgument, "1234"],
            startInfo.ArgumentList);
    }

    [Fact]
    public void WaitForRestartParent_IgnoresAnExitedProcess()
    {
        Assert.True(ApplicationRestartService.WaitForRestartParent(
            [ApplicationRestartService.RestartWaitArgument, int.MaxValue.ToString()]));
    }
}
