using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Replication;

public class ReplicationManager
{
    private readonly object _stateLock = new();
    private bool _primaryNeedsSync;
    private bool _replicaNeedsSync;

    public ReplicationManager(DataInstance primary, DataInstance replica)
    {
        Primary = primary;
        Replica = replica;
    }

    public DataInstance Primary { get; }
    public DataInstance Replica { get; }

    public SemaphoreSlim WriteGate { get; } = new(1, 1);

    public bool PrimaryNeedsSync
    {
        get { lock (_stateLock) return _primaryNeedsSync; }
    }

    public bool ReplicaNeedsSync
    {
        get { lock (_stateLock) return _replicaNeedsSync; }
    }

    public void Initialize()
    {
        ReplicationLog.Write("Startup → проверка экземпляров");
        var primaryState = Primary.CheckState();
        var replicaState = Replica.CheckState();
        ReplicationLog.Write($"Primary → {Format(primaryState)}");
        ReplicationLog.Write($"Replica → {Format(replicaState)}");

        if (primaryState == InstanceState.Unavailable && replicaState == InstanceState.Unavailable)
        {
            throw new DataUnavailableException("Оба экземпляра данных (Primary и Replica) недоступны");
        }

        if (primaryState == InstanceState.Available)
        {
            PostgresSchemaInitializer.EnsureCreatedAndSeeded(Primary.ConnectionFactory);
        }

        if (replicaState == InstanceState.Available)
        {
            PostgresSchemaInitializer.EnsureCreated(Replica.ConnectionFactory);
        }

        if (primaryState == InstanceState.Unavailable)
        {
            MarkNeedsSync(Primary);
            return;
        }

        if (replicaState == InstanceState.Unavailable)
        {
            MarkNeedsSync(Replica);
            return;
        }

        var differences = FindDifferences();
        if (differences.Count == 0)
        {
            ReplicationLog.Write("Начальное состояние → Primary = Replica");
            return;
        }

        ReplicationLog.Write($"Начальное состояние → расхождений: {differences.Count}, копирование Primary → Replica");
        CopyAll(Primary, Replica);
        ReplicationLog.Write(FindDifferences().Count == 0
            ? "Начальное состояние → Primary = Replica"
            : "Начальное состояние → не согласованно");
    }

    public DataInstance ResolveForRequest()
    {
        ReplicationLog.Write("Primary → CHECK");
        var primaryState = Primary.CheckState();
        ReplicationLog.Write($"Primary → {Format(primaryState)}");

        var primaryStale = PrimaryNeedsSync;
        if (primaryState == InstanceState.Available && !primaryStale)
        {
            return Primary;
        }

        if (primaryState == InstanceState.Available)
        {
            ReplicationLog.Write("Primary → данные устарели после отказа, требуется синхронизация");
        }

        ReplicationLog.Write("Failover → START");
        ReplicationLog.Write("Replica → CHECK");
        var replicaState = Replica.CheckState();
        ReplicationLog.Write($"Replica → {Format(replicaState)}");

        if (replicaState == InstanceState.Available && !ReplicaNeedsSync)
        {
            ReplicationLog.Write("Request redirected to Replica");
            return Replica;
        }

        ReplicationLog.Write("Failover → FAIL");

        if (replicaState == InstanceState.Available)
        {
            throw new DataUnavailableException(
                "Невозможно выполнить Failover: Primary недоступен, а Replica не синхронизирована (содержит устаревшие данные)");
        }

        if (primaryState == InstanceState.Available)
        {
            throw new DataUnavailableException(
                "Primary требует синхронизации, а Replica недоступна. Восстановите Replica и выполните синхронизацию");
        }

        throw new DataUnavailableException("Оба экземпляра данных (Primary и Replica) недоступны. Повторите запрос позже");
    }

    public void ReplicateTicket(DataInstance source, int ticketId)
    {
        var target = Other(source);
        ReplicationLog.Write($"Replication → START ({source.Name} → {target.Name}, ticketId={ticketId})");

        if (target.CheckState() == InstanceState.Unavailable)
        {
            ReplicationLog.Write($"{target.Name} → UNAVAILABLE");
            ReplicationLog.Write($"Replication → FAIL, {target.Name} помечен как требующий синхронизации");
            MarkNeedsSync(target);
            return;
        }

        if (NeedsSync(target))
        {
            ReplicationLog.Write($"Replication → SKIPPED, {target.Name} ожидает полной синхронизации");
            return;
        }

        try
        {
            using var sourceConnection = source.ConnectionFactory.Open();
            using var targetConnection = target.ConnectionFactory.Open();
            var copied = DataCopier.CopyTicketRows(sourceConnection, targetConnection, ticketId);
            ReplicationLog.Write($"{target.Name} → UPSERT ({string.Join(", ", copied)})");
            ReplicationLog.Write("Replication → SUCCESS");
        }
        catch (Exception ex)
        {
            ReplicationLog.Write($"Replication → FAIL ({ex.Message}), {target.Name} помечен как требующий синхронизации");
            MarkNeedsSync(target);
        }
    }

    public async Task<SynchronizeInstancesResponseDto> SynchronizeAsync()
    {
        await WriteGate.WaitAsync();
        try
        {
            ReplicationLog.Write("Synchronization → START");

            foreach (var instance in new[] { Primary, Replica })
            {
                var state = instance.CheckState();
                ReplicationLog.Write($"{instance.Name} → {Format(state)}");
                if (state == InstanceState.Unavailable)
                {
                    ReplicationLog.Write("Synchronization → FAIL");
                    throw new SynchronizationException($"Невозможно выполнить синхронизацию: {instance.Name} недоступен");
                }
            }

            var differences = FindDifferences();
            foreach (var difference in differences)
            {
                ReplicationLog.Write($"Расхождение → {difference}");
            }

            if (differences.Count == 0 && !PrimaryNeedsSync && !ReplicaNeedsSync)
            {
                ReplicationLog.Write("Synchronization → не требуется, Primary = Replica");
                return new SynchronizeInstancesResponseDto { ConsistentAfter = true };
            }

            var (source, target) = PrimaryNeedsSync ? (Replica, Primary) : (Primary, Replica);
            ReplicationLog.Write($"Synchronization → копирование {source.Name} → {target.Name}");

            try
            {
                CopyAll(source, target);
            }
            catch (Exception ex)
            {
                ReplicationLog.Write($"Synchronization → FAIL ({ex.Message})");
                throw new SynchronizationException($"Ошибка синхронизации {source.Name} → {target.Name}: {ex.Message}");
            }

            var consistentAfter = FindDifferences().Count == 0;
            if (!consistentAfter)
            {
                ReplicationLog.Write("Synchronization → FAIL, Не согласованно после копирования");
                throw new SynchronizationException("Нарушение согласованности: после синхронизации копии всё ещё отличаются");
            }

            lock (_stateLock)
            {
                _primaryNeedsSync = false;
                _replicaNeedsSync = false;
            }

            ReplicationLog.Write("Synchronization → SUCCESS, Primary = Replica");
            return new SynchronizeInstancesResponseDto
            {
                Source = source.Name,
                Target = target.Name,
                DifferencesBefore = differences,
                ConsistentAfter = true
            };
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public ReplicationStatusDto SetAvailability(string instanceName, bool available)
    {
        var instance = GetByName(instanceName);
        instance.SimulatedFailure = !available;
        ReplicationLog.Write(available
            ? $"{instance.Name} → RECOVERY (имитация отказа снята)"
            : $"{instance.Name} → FAILURE (смоделирован отказ, экземпляр UNAVAILABLE)");

        return GetStatus(null);
    }

    public ReplicationStatusDto GetStatus(int? seatId)
    {
        var primaryState = Primary.CheckState();
        var replicaState = Replica.CheckState();

        var status = new ReplicationStatusDto
        {
            Primary = DescribeInstance(Primary, primaryState, seatId),
            Replica = DescribeInstance(Replica, replicaState, seatId)
        };

        if (primaryState == InstanceState.Available && !PrimaryNeedsSync)
        {
            status.ActiveInstance = Primary.Name;
        }
        else if (replicaState == InstanceState.Available && !ReplicaNeedsSync)
        {
            status.ActiveInstance = Replica.Name;
        }

        if (primaryState == InstanceState.Available && replicaState == InstanceState.Available)
        {
            status.Differences = FindDifferences();
            status.Consistent = status.Differences.Count == 0;
        }

        ReplicationLog.Write(
            $"Status → Primary {Format(primaryState)}, Replica {Format(replicaState)}, активный: {status.ActiveInstance ?? "нет"}, " +
            $"согласованность: {(status.Consistent is null ? "не проверить" : status.Consistent.Value ? "Primary = Replica" : "ЕСТЬ РАСХОЖДЕНИЯ")}");

        return status;
    }

    public List<string> FindDifferences()
    {
        using var primaryConnection = Primary.ConnectionFactory.Open();
        using var replicaConnection = Replica.ConnectionFactory.Open();
        return DataCopier.FindDifferences(primaryConnection, replicaConnection);
    }

    private InstanceStatusDto DescribeInstance(DataInstance instance, InstanceState state, int? seatId)
    {
        var dto = new InstanceStatusDto
        {
            Name = instance.Name,
            State = state,
            SimulatedFailure = instance.SimulatedFailure,
            NeedsSync = NeedsSync(instance)
        };

        if (state == InstanceState.Unavailable)
        {
            return dto;
        }

        using var connection = instance.ConnectionFactory.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT (SELECT COUNT(*) FROM seats WHERE status = 'Free'), (SELECT COUNT(*) FROM tickets)
                """;
            using var reader = command.ExecuteReader();
            reader.Read();
            dto.FreeSeats = (int)reader.GetInt64(0);
            dto.TicketCount = (int)reader.GetInt64(1);
        }

        if (seatId is not null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM seats WHERE id = @id";
            command.Parameters.AddWithValue("id", seatId.Value);
            if (command.ExecuteScalar() is string seatStatus)
            {
                dto.SeatStatus = Enum.Parse<SeatStatus>(seatStatus);
            }
        }

        return dto;
    }

    private static void CopyAll(DataInstance source, DataInstance target)
    {
        using var sourceConnection = source.ConnectionFactory.Open();
        using var targetConnection = target.ConnectionFactory.Open();
        DataCopier.CopyAll(sourceConnection, targetConnection);
    }

    private DataInstance GetByName(string name)
    {
        if (string.Equals(name, Primary.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Primary;
        }

        if (string.Equals(name, Replica.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Replica;
        }

        throw new ValidationException($"Неизвестный экземпляр данных: '{name}'. Допустимо: Primary, Replica");
    }

    private DataInstance Other(DataInstance instance) => instance == Primary ? Replica : Primary;

    private bool NeedsSync(DataInstance instance) => instance == Primary ? PrimaryNeedsSync : ReplicaNeedsSync;

    private void MarkNeedsSync(DataInstance instance)
    {
        lock (_stateLock)
        {
            if (instance == Primary)
            {
                _primaryNeedsSync = true;
            }
            else
            {
                _replicaNeedsSync = true;
            }
        }
    }

    private static string Format(InstanceState state) => state.ToString().ToUpperInvariant();
}
