using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using TicketSales.Client;
using Xunit;

namespace TicketSales.Tests;

public class ServerUnavailableTests
{
    [Fact]
    public async Task Connect_ToClosedPort_ThrowsServerUnavailableException()
    {
        var freePort = GetFreeTcpPort();
        using var client = new NetworkClient();

        await Assert.ThrowsAsync<ServerUnavailableException>(async () =>
        {
            await client.ConnectAsync("127.0.0.1", freePort, timeoutMs: 1000);
        });
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
