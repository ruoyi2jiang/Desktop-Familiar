using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DesktopFamiliar.Services.Chat;

/// <summary>Verifies the server side of each loopback connection before HTTP can send any bytes.</summary>
internal static class LocalEngineConnection
{
    private const int TcpTableOwnerPidConnections = 4;
    private const uint Established = 5;
    private const uint InsufficientBuffer = 122;
    private static readonly uint Loopback = BitConverter.ToUInt32(IPAddress.Loopback.GetAddressBytes());

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpOwnerRow
    {
        public uint State, LocalAddress, LocalPort, RemoteAddress, RemotePort, OwnerPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable([Out] byte[] table, ref int size, bool sort,
        int addressFamily, int tableClass, uint reserved);

    public static async ValueTask<Stream> ConnectAsync(int serverPort, int processId, CancellationToken token)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(IPAddress.Loopback, serverPort, token).ConfigureAwait(false);
            var clientPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
            // The TCP handshake can finish just before Windows publishes the server's owning PID.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                uint? owner = FindServerOwner(serverPort, clientPort);
                if (owner == processId) return new NetworkStream(socket, ownsSocket: true);
                if (owner is > 0 || attempt == 9) break;
                await Task.Delay(20, token).ConfigureAwait(false);
            }
            throw new IOException("The local connection does not belong to the launched llama.cpp process.");
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static uint? FindServerOwner(int serverPort, int clientPort)
    {
        // MIB_TCPROW_OWNER_PID stores each 16-bit port in network byte order in a DWORD.
        static ushort NetworkPort(int port) => unchecked((ushort)IPAddress.HostToNetworkOrder((short)port));
        uint local = NetworkPort(serverPort), remote = NetworkPort(clientPort);
        var size = 16 * 1024;
        while (true)
        {
            var table = new byte[size];
            uint status = GetExtendedTcpTable(table, ref size, false, (int)AddressFamily.InterNetwork,
                TcpTableOwnerPidConnections, 0);
            if (status == InsufficientBuffer) continue;
            if (status != 0) throw new Win32Exception((int)status);

            int count = BitConverter.ToInt32(table);
            var rows = MemoryMarshal.Cast<byte, TcpOwnerRow>(table.AsSpan(sizeof(int)))[..count];
            foreach (var row in rows)
                if (row.State == Established && row.LocalAddress == Loopback && row.RemoteAddress == Loopback &&
                    row.LocalPort == local && row.RemotePort == remote)
                    return row.OwnerPid;
            return null;
        }
    }
}
