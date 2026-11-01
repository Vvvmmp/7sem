using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class PostgresBuyTicketConcurrencyTests : IClassFixture<PostgresTestServerFixture>
{
    private readonly PostgresTestServerFixture _fixture;

    public PostgresBuyTicketConcurrencyTests(PostgresTestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentBuyTicket_OnSameTicket_OnlyOneSucceeds()
    {
        const int seatId = 6;
        int ticketId;

        using (var setupClient = new NetworkClient())
        {
            await setupClient.ConnectAsync("127.0.0.1", _fixture.Port);
            var reserveResponse = await setupClient.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = 1,
                SeatId = seatId,
                CustomerFullName = "Buy Race Setup Client",
                CustomerEmail = "buy-race-setup@example.com"
            });
            Assert.True(reserveResponse.Success);
            var reserveResult = reserveResponse.Data!.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options);
            ticketId = reserveResult!.TicketId;
        }

        const int clientsCount = 20;
        var clients = new NetworkClient[clientsCount];
        for (var i = 0; i < clientsCount; i++)
        {
            clients[i] = new NetworkClient();
            await clients[i].ConnectAsync("127.0.0.1", _fixture.Port);
        }

        try
        {
            var responses = await Task.WhenAll(clients.Select(c =>
                c.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId })));

            Assert.Equal(1, responses.Count(r => r.Success));
            Assert.Equal(clientsCount - 1, responses.Count(r => !r.Success));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }
}
