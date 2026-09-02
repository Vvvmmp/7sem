using System.Threading.Tasks;
using System.Text.Json;
using TicketSales.Client;
using TicketSales.Server.Storage.InMemory;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class SuccessfulScenarioTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public SuccessfulScenarioTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReserveThenBuyTicket_Succeeds()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var eventsResponse = await client.SendAsync("GetEvents", new GetEventsRequestDto());
        Assert.True(eventsResponse.Success);

        var reserveResponse = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = SeedData.FirstEventId,
            SeatId = SeedData.FirstEventFirstSeatId,
            CustomerFullName = "Иван Иванов",
            CustomerEmail = "ivan@example.com"
        });
        Assert.True(reserveResponse.Success);

        var reserveResult = reserveResponse.Data!.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options);
        Assert.NotNull(reserveResult);

        var buyResponse = await client.SendAsync("BuyTicket", new BuyTicketRequestDto
        {
            TicketId = reserveResult!.TicketId
        });
        Assert.True(buyResponse.Success);

        var buyResult = buyResponse.Data!.Value.Deserialize<BuyTicketResponseDto>(JsonDefaults.Options);
        Assert.NotNull(buyResult);
        Assert.Equal(PaymentStatus.Completed, buyResult!.Status);
    }
}
