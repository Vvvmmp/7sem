using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class PostgresTransactionTests : IClassFixture<PostgresTestServerFixture>
{
    private readonly PostgresTestServerFixture _fixture;

    public PostgresTransactionTests(PostgresTestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WithoutTransaction_SimulatedError_LeavesTicketCreatedButSeatStillFree()
    {
        const int seatId = 2;
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "No Tx Client",
            CustomerEmail = "no-tx@example.com",
            UseTransaction = false,
            SimulateErrorAfterTicketInsert = true
        });

        Assert.False(response.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        Assert.True(snapshotResponse.Success);
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        Assert.Equal(SeatStatus.Free, snapshot!.Status);
        Assert.Equal(1, snapshot.TicketCount);
    }

    [Fact]
    public async Task WithTransaction_NoError_CommitsBothChanges()
    {
        const int seatId = 3;
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Tx Commit Client",
            CustomerEmail = "tx-commit@example.com",
            UseTransaction = true,
            SimulateErrorAfterTicketInsert = false
        });

        Assert.True(response.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        Assert.Equal(SeatStatus.Reserved, snapshot!.Status);
        Assert.Equal(1, snapshot.TicketCount);
    }

    [Fact]
    public async Task WithTransaction_SimulatedError_RollsBackBothChanges()
    {
        const int seatId = 4;
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Tx Rollback Client",
            CustomerEmail = "tx-rollback@example.com",
            UseTransaction = true,
            SimulateErrorAfterTicketInsert = true
        });

        Assert.False(response.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        Assert.Equal(SeatStatus.Free, snapshot!.Status);
        Assert.Equal(0, snapshot.TicketCount);
    }

    [Fact]
    public async Task GetSeatSnapshot_ForMissingSeat_ReturnsNotFound()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = 999_999 });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.NotFound, response.Error?.Code);
    }

    [Fact]
    public async Task PartialFailureRun_SeatStaysReservableExactlyOnce()
    {
        const int seatId = 5;
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var first = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Orphan Ticket Client",
            CustomerEmail = "orphan@example.com",
            UseTransaction = false,
            SimulateErrorAfterTicketInsert = true
        });
        Assert.False(first.Success);

        var second = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Real Client",
            CustomerEmail = "real-client@example.com"
        });
        Assert.True(second.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        Assert.Equal(SeatStatus.Reserved, snapshot!.Status);
        Assert.Equal(2, snapshot.TicketCount);
    }
}
