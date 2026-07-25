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
}
