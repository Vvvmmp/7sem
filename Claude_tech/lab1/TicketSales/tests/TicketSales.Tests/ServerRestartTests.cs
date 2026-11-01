using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

/// <summary>Перезапуск процесса сервера поверх тех же БД: операции возобновляются, данные сохраняются.</summary>
public class ServerRestartTests
{
    [Fact]
    public async Task RestartServer_DataPersists_AndOperationsResume()
    {
        int ticketId;
        await using (var first = ReplicatedTestServer.Start())
        {
            using var client = await first.ConnectAsync();
            var reserve = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = 1,
                SeatId = 2,
                CustomerFullName = "Петр Петров",
                CustomerEmail = "petr@example.com"
            });
            Assert.True(reserve.Success);
            ticketId = reserve.Data!.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options)!.TicketId;
            Assert.True((await client.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId })).Success);
        }

        await using var second = ReplicatedTestServer.Start(resetDatabases: false);
        using var restartedClient = await second.ConnectAsync();

        var snapshot = await restartedClient.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = 2 });
        Assert.True(snapshot.Success);
        Assert.Equal(SeatStatus.Sold, snapshot.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options)!.Status);

        var buyAgain = await restartedClient.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId });
        Assert.False(buyAgain.Success);
        Assert.Equal(ErrorCodes.ValidationError, buyAgain.Error?.Code);

        var cancel = await restartedClient.SendAsync("CancelTicket", new CancelTicketRequestDto { TicketId = ticketId });
        Assert.True(cancel.Success);
    }

    [Fact]
    public async Task ServerStopped_ClientGetsNetworkError_AndReconnectReportsUnavailable()
    {
        var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();
        Assert.True((await client.SendAsync("GetEvents", new GetEventsRequestDto())).Success);

        await server.DisposeAsync();
        await Assert.ThrowsAsync<NetworkException>(() => client.SendAsync("GetEvents", new GetEventsRequestDto()));
        await Assert.ThrowsAsync<ServerUnavailableException>(() => client.ReconnectAsync(timeoutMs: 1000));
        await Assert.ThrowsAsync<NetworkException>(() => client.SendAsync("GetEvents", new GetEventsRequestDto()));
    }
}
