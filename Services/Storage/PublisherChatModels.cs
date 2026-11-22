using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopFamiliar.Services.Chat;

namespace DesktopFamiliar.Services.Storage;

internal sealed record PublisherChatRelease(string Repository, string Revision, string FileName, string Digest, long Bytes)
{
    private const string Size = @"[0-9]{1,3}(?:\.[0-9]{1,2})?[BM]";
    private static readonly Regex Liquid = new(@"\ALiquidAI/LFM2\.5-" + Size + @"(?:-Instruct|-JP(?:-[0-9]{6})?)?-GGUF\z");
    private static readonly Regex Coder = new(@"\AQwen/Qwen2\.5-Coder-" + Size + @"-Instruct-GGUF\z");

    internal static bool HasSupportedName(string? repo) => repo != null && (Liquid.IsMatch(repo) || Coder.IsMatch(repo));
    internal static bool IsHex(string? value, int length) => value?.Length == length && value.All(char.IsAsciiHexDigitLower);
    internal static bool IsModelId(string id) => id.StartsWith("hub-", StringComparison.Ordinal) && IsHex(id[4..], 64);
    internal string Name => Repository[(Repository.IndexOf('/') + 1)..^5];
    internal ChatModel? ToModel()
    {
        if (!HasSupportedName(Repository) || !IsHex(Revision, 40) || !IsHex(Digest, 64) ||
            Bytes is <= 0 or > 1_000_000_000_000 ||
            !string.Equals(FileName, Name + "-Q4_K_M.gguf", StringComparison.OrdinalIgnoreCase)) return null;

        int mb = (int)((Bytes + 999_999) / 1_000_000);
        string strength = Coder.IsMatch(Repository) ? "Chat.Hub.Coding" :
            Name.Contains("-JP", StringComparison.Ordinal) ? "Chat.Hub.Japanese" : "Chat.Hub.Liquid";
        return new("hub-" + Digest, "", $"https://huggingface.co/{Repository}/resolve/{Revision}/{FileName}", Digest,
            Bytes, $"https://huggingface.co/{Repository}")
        {
            PublisherRelease = this, StrengthsKey = strength,
            Requirements = new(mb * 2 + 1400, 4, mb + 250, mb + 1200)
        };
    }
}

internal sealed record PublisherSearchResult(IReadOnlyList<ChatModel> Models, bool Incomplete);

internal sealed class PublisherChatModels(HttpClient http)
{
    private sealed record Repository(string Id, string Sha);
    private sealed record HubFile(string Path, long Size, LfsFile? Lfs);
    private sealed record LfsFile(string Oid, long Size);

    internal async Task<PublisherSearchResult> SearchAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var sources = await Task.WhenAll(ReadPublisherAsync("LiquidAI", "LFM2.5", timeout.Token, token),
            ReadPublisherAsync("Qwen", "Qwen2.5-Coder", timeout.Token, token)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(sources.SelectMany(source => source.Models).OrderBy(m => m.FileBytes).ToArray(),
            sources.Any(source => source.Incomplete));
    }

    private async Task<PublisherSearchResult> ReadPublisherAsync(string author, string family, CancellationToken timeout, CancellationToken user)
    {
        var models = new List<ChatModel>();
        bool incomplete = false;
        try
        {
            var repos = await http.GetFromJsonAsync<Repository[]>(
                $"https://huggingface.co/api/models?author={author}&search={family}&filter=gguf&limit=60&expand=sha", timeout).ConfigureAwait(false);
            foreach (var repo in repos ?? [])
            {
                if (!PublisherChatRelease.HasSupportedName(repo?.Id) || !repo!.Id.StartsWith(author + "/", StringComparison.Ordinal)
                    || !PublisherChatRelease.IsHex(repo.Sha, 40)) continue;
                try
                {
                    var files = await http.GetFromJsonAsync<HubFile[]>($"https://huggingface.co/api/models/{repo.Id}/tree/{repo.Sha}", timeout).ConfigureAwait(false);
                    foreach (var file in files ?? [])
                        if (file?.Lfs != null && file.Lfs.Size == file.Size &&
                            new PublisherChatRelease(repo.Id, repo.Sha, file.Path, file.Lfs.Oid, file.Size).ToModel() is { } model)
                            models.Add(model);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
                {
                    AppLogger.Warning($"Could not read chat model metadata for {repo.Id}.", ex);
                    incomplete = true;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException || ex is OperationCanceledException && !user.IsCancellationRequested)
        {
            AppLogger.Warning($"Could not finish chat model discovery for {author}.", ex);
            incomplete = true;
        }
        return new(models, incomplete);
    }
}
