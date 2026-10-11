using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

/// <summary>
/// ЛР №5: репликация, Failover, Recovery. Каждый тест поднимает собственный сервер
/// со сброшенными Primary и Replica, поэтому тесты не зависят друг от друга.
/// </summary>
public class ReplicationTests
{
    [Fact]
    public async Task InitialState_PrimaryEqualsReplica()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var status = await GetStatus(client, seatId: 1);

        Assert.Equal(InstanceState.Available, status.Primary.State);
        Assert.Equal(InstanceState.Available, status.Replica.State);
        Assert.True(status.Consistent);
        Assert.Empty(status.Differences);
        Assert.Equal(status.Primary.FreeSeats, status.Replica.FreeSeats);
        Assert.Equal(SeatStatus.Free, status.Primary.SeatStatus);
        Assert.Equal(SeatStatus.Free, status.Replica.SeatStatus);
        Assert.Equal("Primary", status.ActiveInstance);
    }

    [Fact]
    public async Task ReserveSeat_IsExecutedOnPrimary_AndReplicatedToReplica()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var response = await Reserve(client, seatId: 1);

        Assert.True(response.Success);
        Assert.Equal("Primary", response.DataInstance);

        var status = await GetStatus(client, seatId: 1);
        Assert.Equal(SeatStatus.Reserved, status.Primary.SeatStatus);
        Assert.Equal(SeatStatus.Reserved, status.Replica.SeatStatus);
        Assert.True(status.Consistent);
    }

    [Fact]
    public async Task ThreeSequentialChanges_CopiesStayEqualAfterEachOne()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var reserve = await Reserve(client, seatId: 2);
        Assert.True(reserve.Success);
        var ticketId = Read<ReserveSeatResponseDto>(reserve).TicketId;
        await AssertSeatOnBoth(client, 2, SeatStatus.Reserved);

        var buy = await client.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId });
        Assert.True(buy.Success);
        await AssertSeatOnBoth(client, 2, SeatStatus.Sold);

        var cancel = await client.SendAsync("CancelTicket", new CancelTicketRequestDto { TicketId = ticketId });
        Assert.True(cancel.Success);
        await AssertSeatOnBoth(client, 2, SeatStatus.Free);
    }

    [Fact]
    public async Task PrimaryFailure_IsDetected_AndReplicaIsSelected()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var response = await SetAvailability(client, "Primary", available: false);

        Assert.True(response.Success);
        var status = Read<ReplicationStatusDto>(response);
        Assert.Equal(InstanceState.Unavailable, status.Primary.State);
        Assert.Equal(InstanceState.Available, status.Replica.State);
        Assert.Equal("Replica", status.ActiveInstance);
    }

    [Fact]
    public async Task AfterFailover_ReadAndUpdateAreServedByReplica()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client1 = await server.ConnectAsync();
        using var client2 = await server.ConnectAsync();

        // Билет, созданный на Primary до отказа, сдвигает счётчик id на обеих копиях:
        // следующий INSERT уже на Replica не должен конфликтовать по первичному ключу.
        Assert.True((await Reserve(client1, seatId: 1)).Success);
        await SetAvailability(client1, "Primary", available: false);

        var read = await client1.SendAsync("GetEvents", new GetEventsRequestDto());
        Assert.True(read.Success);
        Assert.Equal("Replica", read.DataInstance);

        var update = await Reserve(client2, seatId: 3);
        Assert.True(update.Success);
        Assert.Equal("Replica", update.DataInstance);

        var status = await GetStatus(client1, seatId: 3);
        Assert.Equal(SeatStatus.Reserved, status.Replica.SeatStatus);
        Assert.Null(status.Primary.SeatStatus);
        Assert.True(status.Primary.NeedsSync);
    }

    [Fact]
    public async Task PrimaryRecovery_DivergenceIsDetected_AndReplicaKeepsServingUntilSync()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        await SetAvailability(client, "Primary", available: false);
        Assert.True((await Reserve(client, seatId: 3)).Success);

        var recovered = Read<ReplicationStatusDto>(await SetAvailability(client, "Primary", available: true));

        Assert.Equal(InstanceState.Available, recovered.Primary.State);
        Assert.Equal(InstanceState.Available, recovered.Replica.State);
        Assert.False(recovered.Consistent);
        Assert.Contains(recovered.Differences, d => d.Contains("место 3"));
        Assert.Equal("Replica", recovered.ActiveInstance);

        var read = await client.SendAsync("GetEvents", new GetEventsRequestDto());
        Assert.Equal("Replica", read.DataInstance);
    }

    [Fact]
    public async Task Synchronize_AfterRecovery_CopiesReplicaToPrimary_AndControlUpdateIsReplicated()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        Assert.True((await Reserve(client, seatId: 1)).Success);
        await SetAvailability(client, "Primary", available: false);
        Assert.True((await Reserve(client, seatId: 3)).Success);
        await SetAvailability(client, "Primary", available: true);

        var syncResponse = await client.SendAsync("SynchronizeInstances", new SynchronizeInstancesRequestDto());

        Assert.True(syncResponse.Success);
        var sync = Read<SynchronizeInstancesResponseDto>(syncResponse);
        Assert.Equal("Replica", sync.Source);
        Assert.Equal("Primary", sync.Target);
        Assert.NotEmpty(sync.DifferencesBefore);
        Assert.True(sync.ConsistentAfter);

        var status = await GetStatus(client, seatId: 3);
        Assert.True(status.Consistent);
        Assert.Equal(SeatStatus.Reserved, status.Primary.SeatStatus);
        Assert.Equal("Primary", status.ActiveInstance);

        var control = await Reserve(client, seatId: 4);
        Assert.True(control.Success);
        Assert.Equal("Primary", control.DataInstance);
        await AssertSeatOnBoth(client, 4, SeatStatus.Reserved);
    }

    [Fact]
    public async Task BothInstancesUnavailable_ClientGetsClearError_AndServerKeepsWorking()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        await SetAvailability(client, "Primary", available: false);
        await SetAvailability(client, "Replica", available: false);

        var response = await client.SendAsync("GetEvents", new GetEventsRequestDto());

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DataUnavailable, response.Error?.Code);
        Assert.Contains("Оба экземпляра", response.Error?.Message);

        await SetAvailability(client, "Replica", available: true);
        var afterRecovery = await client.SendAsync("GetEvents", new GetEventsRequestDto());
        Assert.True(afterRecovery.Success);
    }

    [Fact]
    public async Task ReplicaUnavailable_UpdateSucceedsOnPrimary_ReplicationFails_ThenSyncRestoresReplica()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        await SetAvailability(client, "Replica", available: false);

        var update = await Reserve(client, seatId: 5);
        Assert.True(update.Success);
        Assert.Equal("Primary", update.DataInstance);
        Assert.True(server.Replication.ReplicaNeedsSync);

        var recovered = Read<ReplicationStatusDto>(await SetAvailability(client, "Replica", available: true));
        Assert.False(recovered.Consistent);

        var sync = Read<SynchronizeInstancesResponseDto>(
            await client.SendAsync("SynchronizeInstances", new SynchronizeInstancesRequestDto()));
        Assert.Equal("Primary", sync.Source);
        Assert.Equal("Replica", sync.Target);
        await AssertSeatOnBoth(client, 5, SeatStatus.Reserved);
    }

    [Fact]
    public async Task Failover_ToStaleReplica_IsRefused()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        await SetAvailability(client, "Replica", available: false);
        Assert.True((await Reserve(client, seatId: 6)).Success);
        await SetAvailability(client, "Replica", available: true);
        await SetAvailability(client, "Primary", available: false);

        var response = await client.SendAsync("GetEvents", new GetEventsRequestDto());

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.DataUnavailable, response.Error?.Code);
        Assert.Contains("не синхронизирована", response.Error?.Message);
    }

    [Fact]
    public async Task Synchronize_WhenPrimaryUnavailable_ReturnsSynchronizationError()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        await SetAvailability(client, "Primary", available: false);

        var response = await client.SendAsync("SynchronizeInstances", new SynchronizeInstancesRequestDto());

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.SynchronizationError, response.Error?.Code);
    }

    [Fact]
    public async Task SetInstanceAvailability_UnknownInstance_ReturnsValidationError()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var response = await SetAvailability(client, "Backup", available: false);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.ValidationError, response.Error?.Code);
    }

    private static Task<Response> Reserve(NetworkClient client, int seatId) =>
        client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = $"Покупатель места {seatId}",
            CustomerEmail = $"seat{seatId}@example.com"
        });

    private static Task<Response> SetAvailability(NetworkClient client, string instance, bool available) =>
        client.SendAsync("SetInstanceAvailability", new SetInstanceAvailabilityRequestDto
        {
            Instance = instance,
            Available = available
        });

    private static async Task<ReplicationStatusDto> GetStatus(NetworkClient client, int seatId)
    {
        var response = await client.SendAsync("GetReplicationStatus", new GetReplicationStatusRequestDto { SeatId = seatId });
        Assert.True(response.Success);
        return Read<ReplicationStatusDto>(response);
    }

    private static async Task AssertSeatOnBoth(NetworkClient client, int seatId, SeatStatus expected)
    {
        var status = await GetStatus(client, seatId);
        Assert.Equal(expected, status.Primary.SeatStatus);
        Assert.Equal(expected, status.Replica.SeatStatus);
        Assert.True(status.Consistent);
    }

    private static T Read<T>(Response response) =>
        response.Data!.Value.Deserialize<T>(JsonDefaults.Options)!;
}
