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

internal sealed record ChatModelTestResult(string Reply, TimeSpan Elapsed, double? TokensPerSecond, long PeakRamBytes);

/// <summary>A turn has its own CPU process and context. Idle companions consume no model RAM.</summary>
public sealed class LlamaChatEngine(ChatModelStore models) : IChatEngine
{
    public async Task<string> ReplyAsync(string modelId, IReadOnlyList<ChatMessage> messages, CancellationToken token) =>
        (await ReplyWithExpressionAsync(modelId, messages, [], token).ConfigureAwait(false)).Text;

    public Task<ModelChatReply> ReplyWithExpressionAsync(string modelId, IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<string> expressions, CancellationToken token)
    {
        if (!models.Installed(modelId)) throw new FileNotFoundException("Chat model unavailable.");
        return GenerateAsync(models.EnginePath(modelId), models.ModelPath(modelId), messages, expressions, 220, token);
    }

    internal static Task ProbeAsync(string enginePath, string modelPath, CancellationToken token) =>
        GenerateAsync(enginePath, modelPath, [new ChatMessage("user", "Hello")], [], 1, token);

    internal Task<ChatModelTestResult> TestAsync(string modelId, CancellationToken token)
    {
        if (!models.Installed(modelId)) throw new FileNotFoundException("Chat model unavailable.");
        var started = Stopwatch.GetTimestamp();
        return WithServerAsync(models.EnginePath(modelId), models.ModelPath(modelId), async (http, secret, process, ct) =>
        {
            ChatMessage[] sample = [new("user", "Describe a rainy afternoon in two short sentences.")];
            var reply = await CompleteAsync(http, secret, sample, 64, null, ct, temperature: 0).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidDataException("Model returned an empty test reply.");
            return new ChatModelTestResult(reply.Text.Trim(), Stopwatch.GetElapsedTime(started), reply.TokensPerSecond, process.PeakWorkingSet64);
        }, token);
    }

    private static Task<ModelChatReply> GenerateAsync(string enginePath, string modelPath,
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<string> expressions, int maxTokens, CancellationToken token) =>
        WithServerAsync(enginePath, modelPath, (http, secret, _, ct) =>
            RequestReplyAsync(http, secret, messages, expressions, maxTokens, ct), token);

    private static async Task<T> WithServerAsync<T>(string enginePath, string modelPath,
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
                 "-ngl", "0", "-t", "4", "-c", "12288", "-np", "1", "--api-key", secret, "--alias", "companion" })
            start.ArgumentList.Add(arg);
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

    private static async Task<(string Text, double? TokensPerSecond)> CompleteAsync(HttpClient http, string secret,
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

    internal static (string Text, double? TokensPerSecond) ReadCompletion(JsonElement root)
    {
        var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        double? speed = root.TryGetProperty("timings", out var timings) && timings.ValueKind == JsonValueKind.Object
            && timings.TryGetProperty("predicted_per_second", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var rate) && rate > 0 && double.IsFinite(rate) ? rate : null;
        return (text, speed);
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { /* Already exited during cancellation. */ }
    }
}
