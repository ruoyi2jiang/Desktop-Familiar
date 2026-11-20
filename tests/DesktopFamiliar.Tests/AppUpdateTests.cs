using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DesktopFamiliar.Services.Updates;

namespace DesktopFamiliar.Tests;

public sealed class AppUpdateTests : IDisposable
{
    private const string Portable = "DesktopFamiliar.exe";
    private const string BaseUrl = "https://github.com/ruoyi2jiang/Desktop-Familiar/releases/download";
    private readonly string _directory = Directory.CreateTempSubdirectory("df-app-update-").FullName;
    private static readonly byte[] Payload = [10, 20, 30];
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task FindsNewestStableCoreReleaseAcrossPagesAndIgnoresOtherTags()
    {
        var pages = new List<string>();
        using var http = new HttpClient(new Responder(request =>
        {
            Assert.NotEmpty(request.Headers.UserAgent);
            pages.Add(request.RequestUri!.Query);
            object[] releases = pages.Count == 1
                ? Enumerable.Repeat(Release("auto-clicker-v99.0.0"), 99).Append(Release("v0.9.9")).ToArray()
                : [Release("v0.10.0", [Asset("v0.10.0")]), Release("v1.0.0", prerelease: true),
                   Release("v2.0.0", draft: true), Release("v3.0.0-beta.1"), Release("v9.0.0.1")];
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(releases) };
        }));
        var latest = await new AppUpdateService(http).FindLatestAsync(AppDeployment.Portable, default);
        Assert.Equal(["?per_page=100&page=1", "?per_page=100&page=2"], pages);
        Assert.Equal(new Version(0, 10, 0), latest!.Version);
        Assert.Equal(Portable, latest.Asset!.Name);
    }

    [Theory]
    [InlineData("Installed", true, true, true, true)]
    [InlineData("Installed", true, false, false, false)]
    [InlineData("Portable", true, true, false, true)]
    [InlineData("Portable", false, true, true, false)]
    [InlineData("SaveOnly", true, true, false, false)]
    [InlineData("Portable", false, false, null, false)]
    public async Task OffersVerifiedAssetsAndOnlyAppliesMatchingPackages(string build, bool portable, bool installer,
        bool? expectedInstaller, bool canApply)
    {
        const string tag = "v1.0.0";
        const string setup = "Desktop-Familiar-Setup-1.0.0.exe";
        var assets = new[] { Asset(tag, setup, uri: installer ? null : "https://example.org/setup.exe"),
            Asset(tag, digest: portable ? null : "sha256:invalid"), Asset(tag, "Other.exe") };
        using var http = JsonClient([Release(tag, assets), Release("v0.9.9", [Asset("v0.9.9")])]);
        var deployment = Enum.Parse<AppDeployment>(build);
        var release = await new AppUpdateService(http).FindLatestAsync(deployment, default);
        Assert.Equal(new Version(1, 0, 0), release!.Version);
        Assert.Equal(expectedInstaller, release.Asset?.IsInstaller);
        Assert.Equal(canApply, release.Asset?.CanApplyTo(deployment) == true);
    }

    [Fact]
    public async Task EmptyReleaseListAndNetworkFailureHaveDifferentOutcomes()
    {
        using var empty = JsonClient([]);
        Assert.Null(await new AppUpdateService(empty).FindLatestAsync(AppDeployment.Portable, default));
        using var unavailable = new HttpClient(new Responder(_ => new(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new AppUpdateService(unavailable).FindLatestAsync(AppDeployment.Portable, default));
    }

    [Theory]
    [InlineData(3, true, true)]
    [InlineData(3, false, false)]
    [InlineData(4, true, false)]
    [InlineData(2, true, false)]
    public async Task DownloadExposesOnlyCompleteVerifiedBytes(long expectedSize, bool correctHash, bool succeeds)
    {
        using var http = new HttpClient(new Responder(_ =>
        {
            var content = new ByteArrayContent(Payload);
            content.Headers.ContentLength = null; // A streamed asset may omit this header.
            return new(HttpStatusCode.OK) { Content = content };
        }));
        var service = new AppUpdateService(http);
        var asset = new AppReleaseAsset(Portable, new Uri($"{BaseUrl}/v1.0.0/{Portable}"),
            correctHash ? Hash(Payload) : new string('0', 64), expectedSize, false);
        if (succeeds)
        {
            var path = await service.DownloadAsync(asset, _directory, null, default);
            Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
            Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(_directory)));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(asset, _directory, null, default));
            Assert.Empty(Directory.EnumerateFiles(_directory));
        }
    }

    [Fact]
    public async Task CancellingAnInProgressDownloadDiscardsOnlyItsStagedFile()
    {
        var existing = Path.Combine(_directory, Portable);
        await File.WriteAllBytesAsync(existing, Payload);
        var bytes = new byte[200000];
        using var http = new HttpClient(new Responder(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        using var cancel = new CancellationTokenSource();
        var asset = new AppReleaseAsset(Portable, new Uri($"{BaseUrl}/v1.0.0/{Portable}"), Hash(bytes), bytes.Length, false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AppUpdateService(http)
            .DownloadAsync(asset, _directory, new CancelOnProgress(cancel), cancel.Token));
        Assert.Equal(existing, Assert.Single(Directory.EnumerateFiles(_directory)));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(existing));
    }

    [Theory]
    [InlineData("portable")]
    [InlineData("installer")]
    [InlineData("invalid executable")]
    [InlineData("failed startup")]
    [InlineData("changed after verification")]
    public async Task WindowsHandoffAppliesVerifiedFilesAndRecoversFromFailure(string scenario)
    {
        // These short-lived Windows utilities stand in for app releases inside a disposable directory.
        var target = Path.Combine(_directory, "pet's copy [1] $test.exe");
        var staged = Path.Combine(_directory, "release.download");
        var oldBytes = await File.ReadAllBytesAsync(Path.Combine(Environment.SystemDirectory, "hostname.exe"));
        var newBytes = scenario == "invalid executable" ? Payload : await File.ReadAllBytesAsync(
            Path.Combine(Environment.SystemDirectory, scenario == "failed startup" ? "where.exe" : "whoami.exe"));
        await File.WriteAllBytesAsync(target, oldBytes);
        await File.WriteAllBytesAsync(staged, newBytes);
        var digest = scenario == "changed after verification" ? Hash(oldBytes) : Hash(newBytes);
        var asset = new AppReleaseAsset(Portable, new Uri($"{BaseUrl}/v1.0.0/{Portable}"), digest, newBytes.Length, scenario == "installer");
        var fails = scenario is "invalid executable" or "failed startup" or "changed after verification";

        using var parent = scenario == "portable" ? Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "findstr.exe"), "fixture")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true }) : null;
        var start = AppUpdateHandoff.CreateStartInfo(parent?.Id ?? int.MaxValue, target, staged, asset, "Test", "Update failed");
        // Exercise the real command line while sending any modal error message to the test runner.
        start.ArgumentList[^1] = start.ArgumentList[^1].Replace(
            "[void][Windows.MessageBox]::Show($message, $env:DF_UPDATE_TITLE)", "[Console]::Error.WriteLine($message)");
        start.RedirectStandardError = true;
        using var worker = Process.Start(start)!;
        var error = worker.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            if (parent != null)
            {
                await Task.Delay(600, timeout.Token);
                Assert.False(parent.HasExited);
                Assert.Equal(oldBytes, await File.ReadAllBytesAsync(target));
                parent.StandardInput.Close();
            }
            await worker.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (parent is { HasExited: false }) parent.StandardInput.Close();
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
        }
        var diagnostic = await error;
        Assert.True(worker.ExitCode == (fails ? 1 : 0), diagnostic);
        if (scenario == "changed after verification") Assert.Contains("SHA-256", diagnostic);
        if (scenario == "failed startup") Assert.Contains("App startup exit code", diagnostic);
        Assert.Equal(scenario == "portable" ? newBytes : oldBytes, await File.ReadAllBytesAsync(target));
        Assert.Equal(target, Assert.Single(Directory.EnumerateFiles(_directory)));
    }

    [Fact]
    public void DevelopmentBuildCannotStartAnInPlaceUpdate()
    {
        Assert.Equal(AppDeployment.SaveOnly, AppUpdateHandoff.GetDeployment());
    }

    [Theory]
    [InlineData(@"D:\Custom App\", @"d:\CUSTOM APP\DesktopFamiliar.exe", "Installed")]
    [InlineData(@"D:\Custom App", @"D:\Downloads\DesktopFamiliar.exe", "Portable")]
    [InlineData(@"D:\Custom App", @"D:\Custom App\Backup.exe", "Portable")]
    [InlineData(null, @"D:\Custom App\DesktopFamiliar.exe", "SaveOnly")]
    public void RegisteredPathDistinguishesTheInstalledExecutableFromSeparateCopies(string? folder, string exe, string expected)
    {
        Assert.Equal(Enum.Parse<AppDeployment>(expected), AppUpdateHandoff.ClassifyRegisteredPath(exe, folder));
    }

    [Fact]
    public void ApplicationsCarryTheBundledRendererLicense()
    {
        using var notice = typeof(AppUpdateService).Assembly.GetManifestResourceStream("DesktopFamiliar.Licenses.SharpGLTF.txt");
        Assert.NotNull(notice);
        using var reader = new StreamReader(notice);
        var license = reader.ReadToEnd();
        Assert.Contains("Copyright (c) 2025 Vicente Penades", license);
        Assert.Contains("Permission is hereby granted", license);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DesktopFamiliar.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var installer = File.ReadAllText(Path.Combine(root.FullName, "Installer", "DesktopFamiliar.iss"));
        Assert.Contains(@"\Resources\Licenses\SharpGLTF.txt", installer);
    }

    private static object Release(string tag, object[]? assets = null, bool draft = false, bool prerelease = false)
        => new { tag_name = tag, draft, prerelease, assets };

    private static object Asset(string tag, string name = Portable, string? uri = null, string? digest = null)
        => new { name, state = "uploaded", size = Payload.Length, digest = digest ?? $"sha256:{Hash(Payload)}",
            browser_download_url = uri ?? $"{BaseUrl}/{tag}/{name}" };

    private static HttpClient JsonClient(object[] releases) => new(new Responder(_ =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(releases) }));

    private sealed class Responder(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(reply(request));
    }

    private sealed class CancelOnProgress(CancellationTokenSource source) : IProgress<int>
    {
        public void Report(int value) => source.Cancel();
    }
}
