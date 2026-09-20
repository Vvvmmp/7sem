using System.Linq;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Storage.InMemory;
using TicketSales.Shared.Dto;
using Xunit;

namespace TicketSales.Tests;

public class ReserveSeatConcurrencyTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public ReserveSeatConcurrencyTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentReserveSeat_OnSameSeat_OnlyOneSucceeds()
    {
        const int seatId = SeedData.FirstEventFirstSeatId + 2;
        const int eventId = SeedData.FirstEventId;
        const int clientsCount = 5;

        var tasks = Enumerable.Range(1, clientsCount).Select(async i =>
        {
            using var client = new NetworkClient();
            await client.ConnectAsync("127.0.0.1", _fixture.Port);
            return await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = eventId,
                SeatId = seatId,
                CustomerFullName = $"Concurrent Client {i}",
                CustomerEmail = $"concurrent{i}@example.com"
            });
        });

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(1, responses.Count(r => r.Success));
        Assert.Equal(clientsCount - 1, responses.Count(r => !r.Success));
    }
}
