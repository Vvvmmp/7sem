using System;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Networking;
using TicketSales.Server.Replication;
using TicketSales.Shared.Dto;

namespace TicketSales.Server.Domain.Services;

/// <summary>
/// Сервис предметной области поверх пары Primary / Replica.
/// Чтение: выбор доступного экземпляра (с Failover) и выполнение на нём.
/// Изменение: выполнение на выбранном экземпляре, затем репликация изменения на второй.
/// Сама бизнес-логика (lock + транзакции) остаётся в TicketSalesServicePostgres каждого экземпляра.
/// </summary>
public class ReplicatedTicketSalesService : ITicketSalesService
{
    private readonly ReplicationManager _replication;

    public ReplicatedTicketSalesService(ReplicationManager replication)
    {
        _replication = replication;
    }

    public Task<GetEventsResponseDto> GetEvents(GetEventsRequestDto request) =>
        ReadAsync("READ events", instance => instance.Service.GetEvents(request));

    public Task<GetSeatSnapshotResponseDto> GetSeatSnapshot(GetSeatSnapshotRequestDto request) =>
        ReadAsync($"READ seat {request.SeatId}", instance => instance.Service.GetSeatSnapshot(request));

    public Task<ReserveSeatResponseDto> ReserveSeat(ReserveSeatRequestDto request) =>
        WriteAsync($"UPDATE ReserveSeat seat={request.SeatId}", instance => instance.Service.ReserveSeat(request), result => result.TicketId);

    public Task<BuyTicketResponseDto> BuyTicket(BuyTicketRequestDto request) =>
        WriteAsync($"UPDATE BuyTicket ticket={request.TicketId}", instance => instance.Service.BuyTicket(request), _ => request.TicketId);

    public Task<CancelTicketResponseDto> CancelTicket(CancelTicketRequestDto request) =>
        WriteAsync($"UPDATE CancelTicket ticket={request.TicketId}", instance => instance.Service.CancelTicket(request), _ => request.TicketId);

    // Время сервера не хранится в БД, экземпляр данных для него не нужен.
    public Task<GetServerTimeResponseDto> GetServerTime() => _replication.Primary.Service.GetServerTime();

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
            // Экземпляр отказал уже после проверки (например, контейнер остановлен во время запроса).
            // Следующий запрос обнаружит отказ при проверке и выполнит Failover.
            ReplicationLog.Write($"{instance.Name} → ERROR ({ex.Message})");
            throw new DataUnavailableException($"Ошибка доступа к данным на экземпляре {instance.Name}: {ex.Message}");
        }
    }
}
