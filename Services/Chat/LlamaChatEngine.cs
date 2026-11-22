using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using DesktopFamiliar.Services.Storage;

namespace DesktopFamiliar.Services.Chat;

internal enum ChatComputeBackend { Cpu, Vulkan, CpuFallback }

internal sealed record ChatModelTestResult(string Reply, TimeSpan Elapsed, double? TokensPerSecond, long PeakRamBytes,
    ChatComputeBackend Backend, double? AverageReplyTokens);

internal sealed record ChatCompletion(string Text, double? TokensPerSecond, int? OutputTokens);

/// <summary>A turn has its own engine process and context. Idle companions consume no model RAM.</summary>
public sealed class LlamaChatEngine(ChatModelStore models) : IChatEngine
{
    public async Task<string> ReplyAsync(string modelId, IReadOnlyList<ChatMessage> messages, CancellationToken token) =>
        (await ReplyWithExpressionAsync(modelId, messages, [], token).ConfigureAwait(false)).Text;

    public Task<ModelChatReply> ReplyWithExpressionAsync(string modelId, IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<string> expressions, CancellationToken token)
    {
        return RunModelAsync(modelId, (http, secret, _, _, ct) =>
            RequestReplyAsync(http, secret, messages, expressions, 220, ct), token);
    }

    internal static Task ProbeAsync(string enginePath, string modelPath, CancellationToken token) =>
        WithServerAsync(enginePath, modelPath, false, false, (http, secret, _, ct) =>
            RequestReplyAsync(http, secret, [new ChatMessage("user", "Hello")], [], 1, ct), token);

    internal static Task ProbePublisherAsync(string enginePath, string modelPath, CancellationToken token) =>
        WithServerAsync(enginePath, modelPath, false, false, async (http, secret, _, ct) =>
        {
            var reply = await RequestReplyAsync(http, secret, [new ChatMessage("user", "Say hello.")], [], 32, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidDataException("Model returned an empty setup reply.");
            return reply;
        }, token);

    internal Task<ChatModelTestResult> TestAsync(string modelId, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        return RunModelAsync(modelId, async (http, secret, process, backend, ct) =>
        {
            var replies = new List<ChatCompletion>();
            var firstReplyTime = TimeSpan.Zero;
            foreach (var prompt in new[] { "Describe a rainy afternoon in two short sentences.",
                "I finished a hard task today. Cheer me on in one sentence.",
                "In one sentence, suggest a quiet activity for a break." })
            {
                var reply = await CompleteAsync(http, secret, [new ChatMessage("user", prompt)], 64, null, ct, temperature: 0).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidDataException("Model returned an empty test reply.");
                if (replies.Count == 0) firstReplyTime = Stopwatch.GetElapsedTime(started);
                replies.Add(reply);
            }
            var metrics = SummarizeSamples(replies);
            return new ChatModelTestResult(replies[0].Text.Trim(), firstReplyTime, metrics.Speed,
                process.PeakWorkingSet64, backend, metrics.AverageTokens);
        }, token);
    }

    internal static (double? Speed, double? AverageTokens) SummarizeSamples(IReadOnlyList<ChatCompletion> samples)
    {
        // Report a mean over the same prompts for every model, even when a runtime omits one metric.
        double? speed = samples.Count > 0 && samples.All(s => s.TokensPerSecond.HasValue)
            ? samples.Average(s => s.TokensPerSecond!.Value) : null;
        double? tokens = samples.Count > 0 && samples.All(s => s.OutputTokens.HasValue)
            ? samples.Average(s => s.OutputTokens!.Value) : null;
        return (speed, tokens);
    }

    private Task<T> RunModelAsync<T>(string id,
        Func<HttpClient, string, Process, ChatComputeBackend, CancellationToken, Task<T>> request, CancellationToken token)
    {
        if (!models.Installed(id)) throw new FileNotFoundException("Chat model unavailable.");
        var weights = models.ModelPath(id);
        var direct = ChatModelStore.CatalogueEntry(id)?.DisableThinking == true;
        return RunWithFallbackAsync(models.GpuEnabled, backend =>
        {
            bool gpu = backend == ChatComputeBackend.Vulkan;
            return WithServerAsync(gpu ? models.VulkanEnginePath : models.EnginePath(id), weights, gpu, direct,
                (http, secret, process, ct) => request(http, secret, process, backend, ct), token);
        }, token);
    }

    internal static async Task<T> RunWithFallbackAsync<T>(bool gpuEnabled, Func<ChatComputeBackend, Task<T>> run, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!gpuEnabled) return await run(ChatComputeBackend.Cpu).ConfigureAwait(false);
        try { return await run(ChatComputeBackend.Vulkan).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or TimeoutException
            or HttpRequestException { StatusCode: null or HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable })
        {
            token.ThrowIfCancellationRequested();
            AppLogger.Warning("Vulkan inference failed. Retrying this request with the CPU engine.", ex);
            return await run(ChatComputeBackend.CpuFallback).ConfigureAwait(false);
        }
    }

    private static async Task<T> WithServerAsync<T>(string enginePath, string modelPath, bool gpu, bool disableThinking,
        Func<HttpClient, string, Process, CancellationToken, Task<T>> request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(enginePath) {
            WorkingDirectory = Path.GetDirectoryName(enginePath)!,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-m", modelPath, "--host", "127.0.0.1", "--port", port.ToString(),
                 "-ngl", gpu ? "auto" : "0", "-t", "4", "-c", "12288", "-np", "1", "--api-key", secret, "--alias", "companion" })
            start.ArgumentList.Add(arg);
        if (gpu)
            foreach (var arg in new[] { "--device", "Vulkan0", "--split-mode", "none" }) start.ArgumentList.Add(arg);
        if (disableThinking)
        {
            start.ArgumentList.Add("--reasoning");
            start.ArgumentList.Add("off");
        }
        using var process = Process.Start(start) ?? throw new IOException("Cannot start llama.cpp.");
        using var stopOnCancel = timeout.Token.Register(() => Stop(process));
        try
        {
            using var http = new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false, AllowAutoRedirect = false,
                ConnectCallback = (_, ct) => LocalEngineConnection.ConnectAsync(port, process.Id, ct)
            }) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = Timeout.InfiniteTimeSpan };
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException($"llama.cpp exited with code {process.ExitCode}.");
                try
                {
                    using var health = await http.GetAsync("/health", timeout.Token).ConfigureAwait(false);
                    if (health.IsSuccessStatusCode) break;
                }
                catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) { }
                await Task.Delay(60, timeout.Token).ConfigureAwait(false);
            }
            return await request(http, secret, process, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (timeout.IsCancellationRequested && ex is OperationCanceledException or HttpRequestException or IOException or InvalidOperationException)
        {
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("Local model response timed out.", ex);
        }
        finally
        {
            Stop(process);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    internal static async Task<ModelChatReply> RequestReplyAsync(HttpClient http, string secret,
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<string> expressions, int maxTokens, CancellationToken token)
    {
        if (expressions.Count != 0)
        {
            string[] choices = ["neutral", .. expressions];
            var schema = new { type = "object", properties = new {
                expression = new { type = "string", @enum = choices }, reply = new { type = "string" }
            }, required = new[] { "expression", "reply" }, additionalProperties = false };
            var instructions = "Return JSON with expression and reply. Choose your own expression for this response from "
                + string.Join(", ", choices) + ". Use neutral for ordinary conversation. Put only your spoken words in reply.";
            var structured = messages.ToList();
            if (structured.Count > 0 && structured[0].Role == "system")
                structured[0] = structured[0] with { Content = structured[0].Content + "\n" + instructions };
            else structured.Insert(0, new("system", instructions));
            var format = new { type = "json_schema", json_schema = new { name = "companion_reply", strict = true, schema } };
            try
            {
                var content = await CompleteAsync(http, secret, structured, maxTokens, format, token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(content.Text);
                var obj = json.RootElement;
                if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("reply", out var spoken)
                    && spoken.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(spoken.GetString()))
                {
                    var key = obj.TryGetProperty("expression", out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString() : null;
                    return new(spoken.GetString()!, key != null && expressions.Contains(key) ? key : null);
                }
            }
            catch (JsonException exception) { AppLogger.Warning("Invalid structured chat reply; retrying as text.", exception); }
            catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            { AppLogger.Warning("The local engine rejected the expression format; retrying as text.", exception); }
        }
        return new((await CompleteAsync(http, secret, messages, maxTokens, null, token).ConfigureAwait(false)).Text);
    }

    private static async Task<ChatCompletion> CompleteAsync(HttpClient http, string secret,
        IReadOnlyList<ChatMessage> messages, int maxTokens, object? format, CancellationToken token, double temperature = 0.65)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var body = new Dictionary<string, object> { ["model"] = "companion", ["messages"] = messages,
            ["max_tokens"] = maxTokens, ["temperature"] = temperature, ["stream"] = false };
        if (format != null) body["response_format"] = format;
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false));
        return ReadCompletion(json.RootElement);
    }

    internal static ChatCompletion ReadCompletion(JsonElement root)
    {
        var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        double? speed = root.TryGetProperty("timings", out var timings) && timings.ValueKind == JsonValueKind.Object
            && timings.TryGetProperty("predicted_per_second", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var rate) && rate > 0 && double.IsFinite(rate) ? rate : null;
        int? count = root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("completion_tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Number
            && tokens.TryGetInt32(out var n) && n > 0 ? n : null;
        return new(text, speed, count);
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { /* Already exited during cancellation. */ }
    }
}
