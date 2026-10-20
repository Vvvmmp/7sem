using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Networking;
using TicketSales.Server.PubSub;
using TicketSales.Server.Replication;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

Console.OutputEncoding = Encoding.UTF8;

var primary = new DataInstance("Primary", "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2");
var replica = new DataInstance("Replica", "Host=localhost;Port=5434;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2");
try
{
    PostgresSchemaInitializer.ResetAndSeed(primary.ConnectionFactory);
    PostgresSchemaInitializer.ResetAndSeed(replica.ConnectionFactory);
}
catch (Exception ex)
{
    Console.WriteLine($"Не удалось подключиться к PostgreSQL: {ex.Message}. Запустите docker compose up -d");
    return;
}

var replication = new ReplicationManager(primary, replica);
replication.Initialize();

Header("Запуск: Broker-1, Broker-2, Subscriber и подписки");
var pubSub = PubSubSystem.Create();
using var cts = new CancellationTokenSource();
var server = new TcpServer(IPAddress.Loopback, 0, ServerComposition.CreateDispatcher(replication, pubSub));
var serverTask = server.RunAsync(cts.Token);

var clients = new NetworkClient[3];
for (var i = 0; i < clients.Length; i++)
{
    clients[i] = new NetworkClient();
    await clients[i].ConnectAsync("127.0.0.1", server.Port);
}

Header("Шаги 1–6. Client 1 → ReserveSeat: SeatReserved → Logger, SeatService");
var ticket1 = await Reserve(clients[0], 1);
await Task.Delay(3500);

Header("Шаг 6. Client 1 → BuyTicket: TicketPurchased → 3 Subscriber, анализ задержек");
await Send(clients[0], "BuyTicket", new BuyTicketRequestDto { TicketId = ticket1 });
await Task.Delay(3500);
Console.WriteLine("| Subscriber | Задержка | Начало доставки, мс от публикации | Обработка, мс | Завершение, мс от публикации |");
Console.WriteLine("| --- | --- | --- | --- | --- |");
var purchased = pubSub.Brokers[0].History.Where(r => r.EventType == EventTypes.TicketPurchased).ToList();
var publishedAt = purchased.Min(r => r.Started);
foreach (var r in purchased.OrderBy(r => r.Finished))
{
    Console.WriteLine($"| {r.Subscriber} | {pubSub.Subscribers[r.Subscriber].Delay.TotalSeconds} с | {(r.Started - publishedAt).TotalMilliseconds:F0} | {r.DurationMs:F0} | {(r.Finished - publishedAt).TotalMilliseconds:F0} |");
}

Header("Шаги 7–8. SeatService → UNAVAILABLE, Client 2 → ReserveSeat");
await Send(clients[1], "SetPubSubAvailability", new SetPubSubAvailabilityRequestDto { Component = "SeatService", Available = false });
var ticket2 = await Reserve(clients[1], 2);
await Task.Delay(2500);
await PrintStatus();

Header("Очередь: SeatService → AVAILABLE, повторная доставка из очереди");
await Send(clients[1], "SetPubSubAvailability", new SetPubSubAvailabilityRequestDto { Component = "SeatService", Available = true });

Header("Шаги 9–10. Новый Subscriber AnalyticsService подписывается, Client 2 → CancelTicket");
foreach (var type in EventTypes.All)
{
    await Send(clients[1], "Subscribe", new SubscribeRequestDto { Subscriber = "AnalyticsService", EventType = type });
}
await Send(clients[1], "CancelTicket", new CancelTicketRequestDto { TicketId = ticket2 });
await Task.Delay(3500);

Header("Повторная доставка: Logger завершит 2 попытки ошибкой");
await Send(clients[0], "SetPubSubAvailability", new SetPubSubAvailabilityRequestDto { Component = "Logger", Available = true, TransientFailures = 2 });
await Reserve(clients[0], 3);
await Task.Delay(4000);

Header("Несколько Broker: Broker-1 → UNAVAILABLE, публикация через Broker-2");
await Send(clients[2], "SetPubSubAvailability", new SetPubSubAvailabilityRequestDto { Component = "Broker-1", Available = false });
await Reserve(clients[2], 4);
await Task.Delay(3500);

Header("Оба Broker недоступны: Publisher → FAIL, операция сервера выполнена");
await Send(clients[2], "SetPubSubAvailability", new SetPubSubAvailabilityRequestDto { Component = "Broker-2", Available = false });
await Reserve(clients[2], 5);
await Send(clients[2], "SetPubSubAvailability", new SetPubSubAvailabilityRequestDto { Component = "Broker-1", Available = true });
await Send(clients[2], "SetPubSubAvailability", new SetPubSubAvailabilityRequestDto { Component = "Broker-2", Available = true });

Header("Ошибки: неизвестный Subscriber, неизвестный тип события");
await Send(clients[0], "Subscribe", new SubscribeRequestDto { Subscriber = "StockService", EventType = EventTypes.SeatReserved });
await Send(clients[0], "Subscribe", new SubscribeRequestDto { Subscriber = "Logger", EventType = "OrderCreated" });

Header("Несколько клиентов одновременно: Client 1, 2, 3 → ReserveSeat");
await Task.WhenAll(clients.Select((c, i) => Reserve(c, 6 + i)));
await Task.Delay(4000);
await PrintStatus();

cts.Cancel();
await serverTask;
foreach (var c in clients)
{
    c.Dispose();
}

async Task<int> Reserve(NetworkClient client, int seatId)
{
    var response = await Send(client, "ReserveSeat", new ReserveSeatRequestDto
    {
        EventId = 1,
        SeatId = seatId,
        CustomerFullName = $"Покупатель {seatId}",
        CustomerEmail = $"demo{seatId}@example.com"
    });
    return response.Data?.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options)?.TicketId ?? 0;
}

async Task<Response> Send(NetworkClient client, string operation, object payload)
{
    var response = await client.SendAsync(operation, payload);
    Console.WriteLine(response.Success ? $">>> Клиент: {operation} → OK" : $">>> Клиент: {operation} → ОШИБКА {response.Error?.Code}: {response.Error?.Message}");
    return response;
}

async Task PrintStatus()
{
    var status = (await clients[0].SendAsync("GetPubSubStatus", new GetPubSubStatusRequestDto())).Data!.Value.Deserialize<PubSubStatusDto>(JsonDefaults.Options)!;
    Console.WriteLine(">>> Состояние Pub/Sub:");
    foreach (var b in status.Brokers)
    {
        Console.WriteLine($"    {b.Name}: {(b.Available ? "AVAILABLE" : "UNAVAILABLE")}, в очереди {b.Pending}");
    }

    foreach (var s in status.Subscribers)
    {
        Console.WriteLine($"    {s.Name}: {(s.Available ? "AVAILABLE" : "UNAVAILABLE")}, подписки [{string.Join(", ", s.EventTypes)}], обработано {s.Processed}");
    }

    Console.WriteLine($"    AnalyticsService по типам: {string.Join(", ", status.EventsByType.Select(p => $"{p.Key}={p.Value}"))}");
    Console.WriteLine($"    AnalyticsService по клиентам: {string.Join(", ", status.EventsByClient.Select(p => $"{p.Key}={p.Value}"))}");
}

static void Header(string title) => Console.WriteLine($"\n===== {title} =====");
