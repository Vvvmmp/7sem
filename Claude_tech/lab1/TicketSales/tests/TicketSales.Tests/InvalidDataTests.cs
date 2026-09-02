using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class InvalidDataTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public InvalidDataTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReserveSeat_WithEmptyFields_ReturnsValidationError()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 0,
            SeatId = 0,
            CustomerFullName = string.Empty,
            CustomerEmail = string.Empty
        });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ValidationError, response.Error?.Code);
    }

    [Fact]
    public async Task BuyTicket_WithUnknownTicketId_ReturnsNotFoundError()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("BuyTicket", new BuyTicketRequestDto
        {
            TicketId = 999999
        });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.NotFound, response.Error?.Code);
    }
}
