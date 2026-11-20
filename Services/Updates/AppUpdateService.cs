using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace DesktopFamiliar.Services.Updates;

internal sealed record AppRelease(Version Version, AppReleaseAsset? Asset);
internal enum AppDeployment { SaveOnly, Portable, Installed }
internal sealed record AppReleaseAsset(string Name, Uri DownloadUri, string Sha256, long Size, bool IsInstaller)
{
    public bool CanApplyTo(AppDeployment deployment) =>
        deployment == (IsInstaller ? AppDeployment.Installed : AppDeployment.Portable);
}

/// <summary>Reads stable core releases and verifies the selected Windows download before exposing it.</summary>
internal sealed class AppUpdateService(HttpClient http)
{
    private const string Repository = "ruoyi2jiang/Desktop-Familiar";
    private const int PageSize = 100;
    private const long MaximumAssetSize = 512L * 1024 * 1024;

    public async Task<AppRelease?> FindLatestAsync(AppDeployment deployment, CancellationToken token)
    {
        AppRelease? latest = null;
        for (var page = 1; ; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repository}/releases?per_page={PageSize}&page={page}");
            request.Headers.UserAgent.ParseAdd("DesktopFamiliar/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var releases = await response.Content.ReadFromJsonAsync<ReleaseMetadata[]>(token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Empty GitHub releases response.");
            foreach (var release in releases)
            {
                if (release.Draft || release.Prerelease || release.Tag is not ['v', ..]
                    || !Version.TryParse(release.Tag[1..], out var version) || version.Build < 0
                    || version.Revision >= 0 || release.Tag != $"v{version}"
                    || latest != null && latest.Version >= version) continue;

                var assets = release.Assets ?? [];
                var portable = VerifiedAsset(assets, release.Tag, "DesktopFamiliar.exe", isInstaller: false);
                var installer = VerifiedAsset(assets, release.Tag, $"Desktop-Familiar-Setup-{version}.exe", isInstaller: true);
                var asset = deployment == AppDeployment.Installed ? installer ?? portable : portable ?? installer;
                latest = new AppRelease(version, asset);
            }
            if (releases.Length < PageSize) return latest;
        }
    }

    private static AppReleaseAsset? VerifiedAsset(AssetMetadata[] assets, string tag, string name, bool isInstaller)
    {
        var asset = assets.FirstOrDefault(item => item.Name == name && item.State == "uploaded");
        if (asset is not { Size: > 0 and <= MaximumAssetSize, Digest: { Length: 71 } digest }
            || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            || !digest[7..].All(char.IsAsciiHexDigit)) return null;
        var uri = new Uri($"https://github.com/{Repository}/releases/download/{tag}/{name}");
        if (asset.Url != uri.AbsoluteUri) return null;
        return new(name, uri, digest[7..], asset.Size, isInstaller);
    }

    /// <summary>Creates a new verified file. Callers own the returned path until they install or save it.</summary>
    public async Task<string> DownloadAsync(AppReleaseAsset asset, string directory,
        IProgress<int>? progress, CancellationToken token)
    {
        using var response = await http.GetAsync(asset.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length != asset.Size)
            throw new InvalidDataException("The download length differs from the GitHub release.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $".desktop-familiar-{Guid.NewGuid():N}.download");
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        try
        {
            await using var file = output;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            var lastPercent = -1;
            int count;
            while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                total += count;
                if (total > asset.Size) throw new InvalidDataException("The download exceeds the expected length.");
                hash.AppendData(buffer, 0, count);
                await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                var percent = (int)(total * 100 / asset.Size);
                if (percent != lastPercent) progress?.Report(percent);
                lastPercent = percent;
            }
            token.ThrowIfCancellationRequested();
            if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release download failed integrity verification.");
            return path;
        }
        catch
        {
            DiscardDownload(path);
            throw;
        }
    }

    internal static void DiscardDownload(string? path)
    {
        if (path == null) return;
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warning("Could not remove a temporary app update download.", exception);
        }
    }

    private sealed record ReleaseMetadata([property: JsonPropertyName("tag_name")] string? Tag,
        bool Draft, bool Prerelease, AssetMetadata[]? Assets);
    private sealed record AssetMetadata(string? Name, string? State, long Size, string? Digest,
        [property: JsonPropertyName("browser_download_url")] string? Url);
}
