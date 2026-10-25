using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class ClusterIntegrationTests
{
    [Fact]
    public async Task ProposeReservationStatus_WithMajority_IsAppliedToTicket()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();
        var ticketId = await Reserve(client, seatId: 1);

        var response = await client.SendAsync("ProposeReservationStatus", new ProposeReservationStatusRequestDto { TicketId = ticketId, Status = TicketStatus.Purchased });

        Assert.True(response.Success);
        var result = response.Data!.Value.Deserialize<ConsensusResultDto>(JsonDefaults.Options)!;
        Assert.True(result.Achieved);
        Assert.Equal(5, result.LeaderId);
        Assert.Equal(SeatStatus.Sold, await SeatStatusOf(client, seatId: 1));
    }

    [Fact]
    public async Task ProposeReservationStatus_WithoutMajority_ReturnsError_AndTicketIsUnchanged()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();
        var ticketId = await Reserve(client, seatId: 2);
        foreach (var nodeId in new[] { 3, 4, 5 })
        {
            Assert.True((await client.SendAsync("SetNodeState", new SetNodeStateRequestDto { NodeId = nodeId, Available = false })).Success);
        }

        var response = await client.SendAsync("ProposeReservationStatus", new ProposeReservationStatusRequestDto { TicketId = ticketId, Status = TicketStatus.Purchased });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ConsensusNotAchieved, response.Error!.Code);
        Assert.Equal(SeatStatus.Reserved, await SeatStatusOf(client, seatId: 2));
        var status = (await client.SendAsync("GetClusterStatus", new GetClusterStatusRequestDto())).Data!.Value.Deserialize<ClusterStatusDto>(JsonDefaults.Options)!;
        Assert.Equal(2, status.LeaderId);
        Assert.False(status.ConsensusPossible);
    }

    [Fact]
    public async Task SetNodeState_UnknownNode_ReturnsNotFound()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var response = await client.SendAsync("SetNodeState", new SetNodeStateRequestDto { NodeId = 9, Available = false });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.NotFound, response.Error!.Code);
    }

    private static async Task<int> Reserve(NetworkClient client, int seatId)
    {
        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = $"Покупатель места {seatId}",
            CustomerEmail = $"consensus{seatId}@example.com"
        });
        Assert.True(response.Success);
        return response.Data!.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options)!.TicketId;
    }

    private static async Task<SeatStatus> SeatStatusOf(NetworkClient client, int seatId)
    {
        var response = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        return response.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options)!.Status;
    }
}
