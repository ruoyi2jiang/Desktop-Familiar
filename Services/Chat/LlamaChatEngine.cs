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

/// <summary>A turn has its own CPU process and context. Idle companions consume no model RAM.</summary>
public sealed class LlamaChatEngine(ChatModelStore models) : IChatEngine
{
    public Task<string> ReplyAsync(string modelId, IReadOnlyList<ChatMessage> messages, CancellationToken token)
    {
        if (!models.Installed(modelId)) throw new FileNotFoundException("Chat model unavailable.");
        return GenerateAsync(models.EnginePath(modelId), models.ModelPath(modelId), messages, 220, token);
    }

    internal static Task ProbeAsync(string enginePath, string modelPath, CancellationToken token) =>
        GenerateAsync(enginePath, modelPath, [new ChatMessage("user", "Hello")], 1, token);

    private static async Task<string> GenerateAsync(string enginePath, string modelPath,
        IReadOnlyList<ChatMessage> messages, int maxTokens, CancellationToken token)
    {
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
            return await RequestReplyAsync(http, secret, messages, maxTokens, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Local model response timed out.", ex);
        }
        finally
        {
            Stop(process);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string> RequestReplyAsync(HttpClient http, string secret,
        IReadOnlyList<ChatMessage> messages, int maxTokens, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Content = JsonContent.Create(new { model = "companion", messages, max_tokens = maxTokens, temperature = 0.65, stream = false });
        using var response = await http.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false));
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { /* Already exited during cancellation. */ }
    }
}
