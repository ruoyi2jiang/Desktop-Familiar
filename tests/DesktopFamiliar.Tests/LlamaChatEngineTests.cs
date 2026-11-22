using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DesktopFamiliar.Services.Chat;

namespace DesktopFamiliar.Tests;

public sealed class LlamaChatEngineTests
{
    [Theory]
    [InlineData(false, false, "Cpu")]
    [InlineData(true, false, "Vulkan")]
    [InlineData(true, true, "CpuFallback")]
    public async Task SelectedRuntimeAndFallbackAreReported(bool enabled, bool fail, string expected)
    {
        var attempts = new List<ChatComputeBackend>();
        var result = await LlamaChatEngine.RunWithFallbackAsync(enabled, backend =>
        {
            attempts.Add(backend);
            return fail && backend == ChatComputeBackend.Vulkan
                ? Task.FromException<ChatComputeBackend>(new IOException("Driver exited"))
                : Task.FromResult(backend);
        }, default);
        Assert.Equal(expected, result.ToString());
        Assert.Equal(fail ? 2 : 1, attempts.Count);
    }

    [Fact]
    public async Task CancelledGpuAttemptNeverLaunchesACpuRetry()
    {
        using var cancel = new CancellationTokenSource();
        var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LlamaChatEngine.RunWithFallbackAsync<bool>(true, _ =>
            {
                attempts++;
                cancel.Cancel();
                throw new IOException("Engine stopped while cancelling");
            }, cancel.Token));
        Assert.Equal(1, attempts);
    }
    [Theory]
    [InlineData("""{"predicted_per_second":25.5}""", 25.5)]
    [InlineData("""{"predicted_per_second":0}""", null)]
    [InlineData("""{"predicted_per_second":"unknown"}""", null)]
    [InlineData("null", null)]
    public void SampleUsesEngineReportedGenerationSpeedWhenAvailable(string timings, double? expected)
    {
        using var response = JsonDocument.Parse("""{"choices":[{"message":{"content":"Hello!"}}],"timings":""" + timings + "}");
        var result = LlamaChatEngine.ReadCompletion(response.RootElement);
        Assert.Equal("Hello!", result.Text);
        Assert.Equal(expected, result.TokensPerSecond);
    }

    [Theory]
    [InlineData("""{"completion_tokens":12,"prompt_tokens":44}""", 12)]
    [InlineData("""{"completion_tokens":-3}""", null)]
    [InlineData("""{"completion_tokens":"12"}""", null)]
    [InlineData("""{"prompt_tokens":12}""", null)]
    [InlineData("null", null)]
    public void SampleLengthUsesOnlyReportedOutputTokens(string usage, int? expected)
    {
        using var response = JsonDocument.Parse("""{"choices":[{"message":{"content":"Hello!"}}],"usage":""" + usage + "}");
        Assert.Equal(expected, LlamaChatEngine.ReadCompletion(response.RootElement).OutputTokens);
    }

    [Fact]
    public void SampleMeansRequireAllMeasurementsForEachMetric()
    {
        ChatCompletion[] samples = [new("One", 10, 8), new("Two", 20, 12), new("Three", 30, 16)];
        Assert.Equal((20d, 12d), LlamaChatEngine.SummarizeSamples(samples));
        samples[1] = samples[1] with { OutputTokens = null };
        Assert.Equal((20d, (double?)null), LlamaChatEngine.SummarizeSamples(samples));
        samples[1] = samples[1] with { OutputTokens = 12, TokensPerSecond = null };
        Assert.Equal(((double?)null, 12d), LlamaChatEngine.SummarizeSamples(samples));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyTheExpectedProcessReceivesData(bool expectedOwner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = listener.AcceptTcpClientAsync(timeout.Token);
        int owner = expectedOwner ? Environment.ProcessId : -1;
        var connect = LocalEngineConnection.ConnectAsync(port, owner, timeout.Token).AsTask();
        using var peer = await accept;
        if (expectedOwner)
        {
            using var verified = await connect;
            await verified.WriteAsync(new byte[] { 42 }, timeout.Token);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => connect);
        }
        var received = new byte[1];
        var count = await peer.GetStream().ReadAsync(received, timeout.Token);
        Assert.Equal(expectedOwner ? 1 : 0, count);
        if (expectedOwner) Assert.Equal(42, received[0]);
    }
}
