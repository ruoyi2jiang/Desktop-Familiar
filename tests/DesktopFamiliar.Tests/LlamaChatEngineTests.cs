using System.Net;
using System.Net.Sockets;
using DesktopFamiliar.Services.Chat;

namespace DesktopFamiliar.Tests;

public sealed class LlamaChatEngineTests
{
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
