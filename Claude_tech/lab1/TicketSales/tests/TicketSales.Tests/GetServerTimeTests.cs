using System;
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class GetServerTimeTests : IClassFixture<PostgresTestServerFixture>
{
    private readonly PostgresTestServerFixture _fixture;

    public GetServerTimeTests(PostgresTestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetServerTime_ReturnsCurrentUtcTime()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var before = DateTime.UtcNow;
        var response = await client.SendAsync("GetServerTime", new GetServerTimeRequestDto());
        var after = DateTime.UtcNow;

        Assert.True(response.Success);
        var result = response.Data!.Value.Deserialize<GetServerTimeResponseDto>(JsonDefaults.Options);
        Assert.NotNull(result);
        Assert.InRange(result!.ServerTimeUtc, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
