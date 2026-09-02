using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class UnknownOperationTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public UnknownOperationTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendUnknownOperation_ReturnsUnknownOperationError()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("DoSomethingUnknown", new { });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.UnknownOperation, response.Error?.Code);
    }
}
