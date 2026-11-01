using System;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Networking;
using TicketSales.Server.PubSub;
using TicketSales.Server.Replication;
using TicketSales.Shared.Dto;

namespace TicketSales.Server.Domain.Services;

public class ReplicatedTicketSalesService : ITicketSalesService
{
    private readonly ReplicationManager _replication;
    private readonly Publisher? _publisher;

    public ReplicatedTicketSalesService(ReplicationManager replication, Publisher? publisher = null)
    {
        _replication = replication;
        _publisher = publisher;
    }

    public Task<GetEventsResponseDto> GetEvents(GetEventsRequestDto request) =>
        ReadAsync("READ events", instance => instance.Service.GetEvents(request));

    public Task<GetSeatSnapshotResponseDto> GetSeatSnapshot(GetSeatSnapshotRequestDto request) =>
        ReadAsync($"READ seat {request.SeatId}", instance => instance.Service.GetSeatSnapshot(request));

    public Task<ReserveSeatResponseDto> ReserveSeat(ReserveSeatRequestDto request) =>
        PublishAsync(
            WriteAsync($"UPDATE ReserveSeat seat={request.SeatId}", instance => instance.Service.ReserveSeat(request), result => result.TicketId),
            result => new PubSubEvent(EventTypes.SeatReserved, request.SeatId, new() { ["TicketId"] = result.TicketId.ToString(), ["EventId"] = request.EventId.ToString() }));

    public Task<BuyTicketResponseDto> BuyTicket(BuyTicketRequestDto request) =>
        PublishAsync(
            WriteAsync($"UPDATE BuyTicket ticket={request.TicketId}", instance => instance.Service.BuyTicket(request), _ => request.TicketId),
            result => new PubSubEvent(EventTypes.TicketPurchased, request.TicketId, new() { ["Amount"] = result.Amount.ToString() }));

    public Task<CancelTicketResponseDto> CancelTicket(CancelTicketRequestDto request) =>
        PublishAsync(
            WriteAsync($"UPDATE CancelTicket ticket={request.TicketId}", instance => instance.Service.CancelTicket(request), _ => request.TicketId),
            _ => new PubSubEvent(EventTypes.TicketCancelled, request.TicketId));

    public Task<GetServerTimeResponseDto> GetServerTime() => _replication.Primary.Service.GetServerTime();

    private async Task<T> PublishAsync<T>(Task<T> operation, Func<T, PubSubEvent> createEvent)
    {
        var result = await operation;
        if (_publisher is not null)
        {
            var e = createEvent(result);
            ReplicationLog.Write($"Server → {e.Type}");
            _publisher.Publish(e);
        }

        return result;
    }

    private async Task<T> ReadAsync<T>(string operation, Func<DataInstance, Task<T>> action)
    {
        var instance = _replication.ResolveForRequest();
        return await ExecuteAsync(operation, instance, action);
    }

    private async Task<T> WriteAsync<T>(string operation, Func<DataInstance, Task<T>> action, Func<T, int> changedTicketId)
    {
        await _replication.WriteGate.WaitAsync();
        try
        {
            var instance = _replication.ResolveForRequest();
            var result = await ExecuteAsync(operation, instance, action);
            _replication.ReplicateTicket(instance, changedTicketId(result));
            return result;
        }
        finally
        {
            _replication.WriteGate.Release();
        }
    }

    private static async Task<T> ExecuteAsync<T>(string operation, DataInstance instance, Func<DataInstance, Task<T>> action)
    {
        if (RequestContext.Current is { } context)
        {
            context.DataInstance = instance.Name;
        }

        ReplicationLog.Write($"{instance.Name} → {operation}");
        try
        {
            var result = await action(instance);
            ReplicationLog.Write($"{instance.Name} → SUCCESS");
            return result;
        }
        catch (DomainException ex)
        {
            ReplicationLog.Write($"{instance.Name} → FAIL ({ex.Message})");
            throw;
        }
        catch (Exception ex)
        {
            ReplicationLog.Write($"{instance.Name} → ERROR ({ex.Message})");
            throw new DataUnavailableException($"Ошибка доступа к данным на экземпляре {instance.Name}: {ex.Message}");
        }
    }
}
