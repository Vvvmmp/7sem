using System.Threading.Tasks;
using Xunit;

namespace TicketSales.Tests;

public class PostgresTestServerFixture : IAsyncLifetime
{
    private ReplicatedTestServer? _server;

    public int Port => _server!.Port;

    public Task InitializeAsync()
    {
        _server = ReplicatedTestServer.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}
