using System.Net;
using System.Net.Http.Json;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Tests;

public sealed class PublisherDiscoveryReadTests
{
    [Fact]
    public async Task InterruptedRepositoryAndFileListingsKeepOtherModelsAvailable()
    {
        using var http = new HttpClient(new InterruptedListingsHandler());
        var result = await new PublisherChatModels(http).SearchAsync(CancellationToken.None);

        Assert.True(result.Incomplete);
        Assert.Equal("LFM2.5-350M", Assert.Single(result.Models).Name);
    }

    private sealed class InterruptedListingsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            var revision = new string('b', 40);
            HttpContent body;
            if (uri.Query.Contains("author=Qwen") || uri.AbsolutePath.Contains("230M"))
                body = new StreamContent(new InterruptedBody());
            else if (uri.Query.Contains("author=LiquidAI"))
                body = JsonContent.Create(new[] { new { id = "LiquidAI/LFM2.5-230M-GGUF", sha = revision },
                    new { id = "LiquidAI/LFM2.5-350M-GGUF", sha = revision } });
            else
                body = JsonContent.Create(new[] { new { path = "LFM2.5-350M-Q4_K_M.gguf", size = 128,
                    lfs = new { oid = new string('a', 64), size = 128 } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = body });
        }
    }

    private sealed class InterruptedBody() : MemoryStream("["u8.ToArray())
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position == 0 ? base.ReadAsync(buffer, cancellationToken)
                : ValueTask.FromException<int>(new HttpIOException(HttpRequestError.ResponseEnded));
    }
}
