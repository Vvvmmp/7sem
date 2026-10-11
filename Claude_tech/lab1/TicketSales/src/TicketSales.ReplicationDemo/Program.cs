using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Networking;
using TicketSales.Server.Replication;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

// ЛР №5: полный цикл Primary → Replication → Failure → Failover → Recovery → Synchronization
// через реальный путь Client → TCP → Server → PostgreSQL (два контейнера docker compose).
// Внимание: обе БД сбрасываются к начальным данным.

Console.OutputEncoding = Encoding.UTF8;

const string primaryConnectionString = "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2";
const string replicaConnectionString = "Host=localhost;Port=5434;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2";

var primary = new DataInstance("Primary", primaryConnectionString);
var replica = new DataInstance("Replica", replicaConnectionString);

try
{
    PostgresSchemaInitializer.ResetAndSeed(primary.ConnectionFactory);
    PostgresSchemaInitializer.ResetAndSeed(replica.ConnectionFactory);
}
catch (Exception ex)
{
    Console.WriteLine($"Не удалось подключиться к PostgreSQL: {ex.Message}");
    Console.WriteLine("Запустите оба экземпляра: docker compose up -d");
    return;
}

var replication = new ReplicationManager(primary, replica);
Header("ЧАСТЬ 1. Запуск сервера: начальное состояние Primary = Replica");
replication.Initialize();

using var cts = new CancellationTokenSource();
var server = new TcpServer(IPAddress.Loopback, 0, ServerComposition.CreateDispatcher(replication));
var serverTask = server.RunAsync(cts.Token);

using var client1 = new NetworkClient();
await client1.ConnectAsync("127.0.0.1", server.Port);
using var client2 = new NetworkClient();
await client2.ConnectAsync("127.0.0.1", server.Port);

var initial = await Status(client1, null);
PrintStatus(initial);
var initialRows = new List<string>();
for (var seatId = 1; seatId <= 3; seatId++)
{
    var s = await Status(client1, seatId);
    initialRows.Add($"| Место {seatId} | {s.Primary.SeatStatus} | {s.Replica.SeatStatus} | {(s.Primary.SeatStatus == s.Replica.SeatStatus ? "да" : "нет")} |");
}
initialRows.Add($"| Свободных мест | {initial.Primary.FreeSeats} | {initial.Replica.FreeSeats} | {(initial.Primary.FreeSeats == initial.Replica.FreeSeats ? "да" : "нет")} |");
initialRows.Add($"| Билетов | {initial.Primary.TicketCount} | {initial.Replica.TicketCount} | {(initial.Primary.TicketCount == initial.Replica.TicketCount ? "да" : "нет")} |");

Header("ЧАСТЬ 2. Репликация: три последовательных изменения");
var replicationRows = new List<string>();

var change1 = await Change(1, "Client 1: ReserveSeat (место 1)", 1,
    () => client1.SendAsync("ReserveSeat", Reserve(1, "Иван Петров", "ivan@example.com")));
var ticket1 = Data<ReserveSeatResponseDto>(change1).TicketId;

await Change(2, $"Client 2: BuyTicket (билет {ticket1}, место 1)", 1,
    () => client2.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticket1 }));

await Change(3, "Client 1: ReserveSeat (место 2)", 2,
    () => client1.SendAsync("ReserveSeat", Reserve(2, "Анна Смирнова", "anna@example.com")));

Header("ЧАСТЬ 3. Отказ Primary и Failover");
var failoverRows = new List<string>();

PrintStatus(await Status(client1, null));
await Request(1, "Client 1", "Read (GetEvents)", () => client1.SendAsync("GetEvents", new GetEventsRequestDto()));

Console.WriteLine();
Console.WriteLine(">>> Моделирование отказа: Primary → UNAVAILABLE");
PrintStatus(Data<ReplicationStatusDto>(await client1.SendAsync("SetInstanceAvailability",
    new SetInstanceAvailabilityRequestDto { Instance = "Primary", Available = false })));

await Request(2, "Client 2", "Read (GetSeatSnapshot, место 1)",
    () => client2.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = 1 }));
var failoverUpdate = await Request(3, "Client 1", "Update (ReserveSeat, место 3)",
    () => client1.SendAsync("ReserveSeat", Reserve(3, "Олег Ким", "oleg@example.com")));
await Request(4, "Client 2", "Read (GetEvents)", () => client2.SendAsync("GetEvents", new GetEventsRequestDto()));
var ticket3 = Data<ReserveSeatResponseDto>(failoverUpdate).TicketId;
await Request(5, "Client 2", $"Update (BuyTicket, билет {ticket3})",
    () => client2.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticket3 }));

Header("ЧАСТЬ 4. Восстановление Primary и синхронизация");
Console.WriteLine(">>> Восстановление: Primary → AVAILABLE");
var recovered = Data<ReplicationStatusDto>(await client1.SendAsync("SetInstanceAvailability",
    new SetInstanceAvailabilityRequestDto { Instance = "Primary", Available = true }));
PrintStatus(recovered);
var seat3BeforeSync = await Status(client1, 3);

Console.WriteLine();
Console.WriteLine(">>> Синхронизация");
var sync = Data<SynchronizeInstancesResponseDto>(await client1.SendAsync("SynchronizeInstances", new SynchronizeInstancesRequestDto()));
Console.WriteLine($"Синхронизация {sync.Source} → {sync.Target}, после: {(sync.ConsistentAfter ? "Primary = Replica" : "РАСХОЖДЕНИЯ")}");
var afterSync = await Status(client1, 3);
PrintStatus(afterSync);

Console.WriteLine();
Console.WriteLine(">>> Контрольная операция после восстановления");
await Change(4, "Client 1: ReserveSeat (место 4), после восстановления", 4,
    () => client1.SendAsync("ReserveSeat", Reserve(4, "Мария Орлова", "maria@example.com")));
var afterControl = await Status(client1, 3);
PrintStatus(afterControl);

Header("ИТОГОВЫЕ ТАБЛИЦЫ");

Console.WriteLine("Начальное состояние (Часть 1, Требование 7):");
Console.WriteLine("| Значение | Primary | Replica | Совпадает |");
Console.WriteLine("| --- | --- | --- | --- |");
initialRows.ForEach(Console.WriteLine);

Console.WriteLine();
Console.WriteLine("Таблица результатов репликации (Часть 2, Требование 2):");
Console.WriteLine("| № | Операция | Primary до | Primary после | Replica до | Replica после | Репликация | Результат |");
Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
replicationRows.ForEach(Console.WriteLine);

Console.WriteLine();
Console.WriteLine("Таблица результатов отказа (Часть 3, Требование 5):");
Console.WriteLine("| № | Клиент | Операция | Primary | Replica | Результат | Использованный экземпляр |");
Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
failoverRows.ForEach(Console.WriteLine);

Console.WriteLine();
Console.WriteLine("Восстановление (Часть 4):");
Console.WriteLine("| Этап | Primary | Replica | Место 3 Primary | Место 3 Replica | Билетов Primary / Replica | Согласованность |");
Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
Console.WriteLine(RecoveryRow("После восстановления Primary", seat3BeforeSync));
Console.WriteLine(RecoveryRow($"После синхронизации {sync.Source} → {sync.Target}", afterSync));
Console.WriteLine(RecoveryRow("После контрольной операции", afterControl));
Console.WriteLine();
Console.WriteLine("Расхождения, обнаруженные после восстановления:");
recovered.Differences.ForEach(d => Console.WriteLine($"  - {d}"));

cts.Cancel();
try { await serverTask; } catch { }
return;

async Task<Response> Change(int number, string operation, int seatId, Func<Task<Response>> action)
{
    var before = await Status(client1, seatId);
    Console.WriteLine();
    Console.WriteLine($">>> Изменение {number}: {operation}");
    var response = await action();
    var after = await Status(client1, seatId);
    var replicated = after.Primary.SeatStatus == after.Replica.SeatStatus && after.Consistent == true;
    Console.WriteLine($"    Ответ: {(response.Success ? "SUCCESS" : $"FAIL {response.Error?.Message}")}, экземпляр: {response.DataInstance}");
    Console.WriteLine($"    Место {seatId}: Primary {before.Primary.SeatStatus} → {after.Primary.SeatStatus}, Replica {before.Replica.SeatStatus} → {after.Replica.SeatStatus}");
    replicationRows.Add(
        $"| {number} | {operation} | {before.Primary.SeatStatus} | {after.Primary.SeatStatus} | {before.Replica.SeatStatus} | {after.Replica.SeatStatus} | " +
        $"{(replicated ? "SUCCESS" : "FAIL")} | {(replicated ? "Primary = Replica" : "расхождение")} |");
    return response;
}

async Task<Response> Request(int number, string clientName, string operation, Func<Task<Response>> action)
{
    var before = await Status(client1, null);
    Console.WriteLine();
    Console.WriteLine($">>> {clientName} → {operation}");
    var response = await action();
    Console.WriteLine($"    Ответ: {(response.Success ? "SUCCESS" : $"FAIL {response.Error?.Message}")}, экземпляр: {response.DataInstance}");
    failoverRows.Add(
        $"| {number} | {clientName} | {operation} | {Format(before.Primary)} | {Format(before.Replica)} | " +
        $"{(response.Success ? "SUCCESS" : "FAIL")} | {response.DataInstance ?? "—"} |");
    return response;
}

static ReserveSeatRequestDto Reserve(int seatId, string name, string email) => new()
{
    EventId = 1,
    SeatId = seatId,
    CustomerFullName = name,
    CustomerEmail = email
};

static async Task<ReplicationStatusDto> Status(NetworkClient client, int? seatId) =>
    Data<ReplicationStatusDto>(await client.SendAsync("GetReplicationStatus", new GetReplicationStatusRequestDto { SeatId = seatId }));

static T Data<T>(Response response) =>
    response.Data!.Value.Deserialize<T>(JsonDefaults.Options)
    ?? throw new InvalidOperationException($"Пустой ответ: {response.Error?.Message}");

static string Format(InstanceStatusDto instance) => instance.State.ToString().ToUpperInvariant();

static string RecoveryRow(string stage, ReplicationStatusDto s) =>
    $"| {stage} | {Format(s.Primary)} | {Format(s.Replica)} | {s.Primary.SeatStatus} | {s.Replica.SeatStatus} | " +
    $"{s.Primary.TicketCount} / {s.Replica.TicketCount} | {(s.Consistent == true ? "Primary = Replica" : "ЕСТЬ РАСХОЖДЕНИЯ")} |";

static void PrintStatus(ReplicationStatusDto s)
{
    Console.WriteLine($"    Primary: {Format(s.Primary)}{(s.Primary.NeedsSync ? " (требует синхронизации)" : "")}, " +
                      $"Replica: {Format(s.Replica)}{(s.Replica.NeedsSync ? " (требует синхронизации)" : "")}, " +
                      $"активный: {s.ActiveInstance ?? "нет"}, " +
                      $"согласованность: {(s.Consistent is null ? "не проверить" : s.Consistent.Value ? "Primary = Replica" : "ЕСТЬ РАСХОЖДЕНИЯ")}");
    foreach (var difference in s.Differences)
    {
        Console.WriteLine($"      - {difference}");
    }
}

static void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 90));
    Console.WriteLine(title);
    Console.WriteLine(new string('=', 90));
}
