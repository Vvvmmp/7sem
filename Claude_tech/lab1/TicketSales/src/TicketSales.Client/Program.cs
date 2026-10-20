using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

Console.OutputEncoding = Encoding.UTF8;

Console.Write("Адрес сервера [127.0.0.1]: ");
var hostInput = Console.ReadLine();
var host = string.IsNullOrWhiteSpace(hostInput) ? "127.0.0.1" : hostInput.Trim();

Console.Write("Порт сервера [5050]: ");
var portInput = Console.ReadLine();
var port = 5050;
if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput.Trim(), out var parsedPort))
{
    port = parsedPort;
}

Console.Write("Смещение локальных часов клиента в секундах (для моделирования рассинхронизации) [0]: ");
var offsetInput = Console.ReadLine();
var clockOffsetSeconds = 0d;
if (!string.IsNullOrWhiteSpace(offsetInput) && double.TryParse(offsetInput.Trim(), out var parsedOffset))
{
    clockOffsetSeconds = parsedOffset;
}
var clockOffset = TimeSpan.FromSeconds(clockOffsetSeconds);

using var client = new NetworkClient();

try
{
    await client.ConnectAsync(host, port);
    Console.WriteLine($"Подключено к серверу {host}:{port}");
}
catch (ServerUnavailableException ex)
{
    Console.WriteLine($"Не удалось подключиться: {ex.Message}");
    return;
}

var running = true;
while (running)
{
    PrintMenu();
    var choice = Console.ReadLine();

    try
    {
        switch (choice)
        {
            case "1":
                await HandleGetEvents(client);
                break;
            case "2":
                await HandleReserveSeat(client);
                break;
            case "3":
                await HandleBuyTicket(client);
                break;
            case "4":
                await HandleCancelTicket(client);
                break;
            case "5":
                HandleShowLocalTime(clockOffset);
                break;
            case "6":
                await HandleSynchronizeTime(client, clockOffset);
                break;
            case "7":
                await HandlePubSubStatus(client);
                break;
            case "8":
                await HandleSetPubSubAvailability(client, "Subscriber (Logger / SeatService / NotificationService / AnalyticsService)", false);
                break;
            case "9":
                await HandleSetPubSubAvailability(client, "Broker (Broker-1 / Broker-2)", false);
                break;
            case "10":
                await HandleSubscribe(client);
                break;
            case "11":
                await HandleSetPubSubAvailability(client, "Subscriber", true);
                break;
            case "0":
                running = false;
                break;
            default:
                Console.WriteLine("Неизвестный пункт меню");
                break;
        }
    }
    catch (NetworkException ex)
    {
        Console.WriteLine($"Ошибка сети: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка ввода данных: {ex.Message}");
    }
}

static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("1. Список событий");
    Console.WriteLine("2. Забронировать место");
    Console.WriteLine("3. Купить билет");
    Console.WriteLine("4. Отменить билет");
    Console.WriteLine("5. Показать локальное время (с учетом смещения)");
    Console.WriteLine("6. Синхронизировать время с сервером");
    Console.WriteLine("7. Состояние Pub/Sub (Broker, Subscriber, очередь, статистика)");
    Console.WriteLine("8. Отказ / восстановление Subscriber");
    Console.WriteLine("9. Отказ / восстановление Broker");
    Console.WriteLine("10. Подписать Subscriber на событие");
    Console.WriteLine("11. Временные ошибки Subscriber (повторная доставка)");
    Console.WriteLine("0. Выход");
    Console.Write("Выберите пункт: ");
}

static void HandleShowLocalTime(TimeSpan clockOffset)
{
    var localTime = TimeSynchronizer.ApplyOffset(DateTime.UtcNow, clockOffset);
    Console.WriteLine($"Локальное время клиента (со смещением {clockOffset.TotalSeconds:+0;-0;0}s): {localTime:HH:mm:ss.fff}");
}

static async Task HandleSynchronizeTime(NetworkClient client, TimeSpan clockOffset)
{
    var requestSentAtClientClock = TimeSynchronizer.ApplyOffset(DateTime.UtcNow, clockOffset);
    var trueSendInstant = DateTime.UtcNow;

    var response = await client.SendAsync("GetServerTime", new GetServerTimeRequestDto());

    var trueReceiveInstant = DateTime.UtcNow;
    var rtt = trueReceiveInstant - trueSendInstant;

    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка синхронизации времени: {response.Error?.Message}");
        return;
    }

    var result = response.Data.Value.Deserialize<GetServerTimeResponseDto>(JsonDefaults.Options);
    if (result is null)
    {
        Console.WriteLine("Ошибка обработки ответа сервера: пустые данные времени");
        return;
    }

    var offset = TimeSynchronizer.CalculateOffset(result.ServerTimeUtc, requestSentAtClientClock, rtt);
    var correctedNow = TimeSynchronizer.ApplyOffset(TimeSynchronizer.ApplyOffset(DateTime.UtcNow, clockOffset), offset);

    Console.WriteLine($"Локальное время клиента (до синхронизации): {requestSentAtClientClock:HH:mm:ss.fff}");
    Console.WriteLine($"Время сервера: {result.ServerTimeUtc:HH:mm:ss.fff}");
    Console.WriteLine($"RTT: {rtt.TotalMilliseconds:F0} ms (оценка задержки RTT/2 = {rtt.TotalMilliseconds / 2:F0} ms)");
    Console.WriteLine($"Вычисленная поправка (offset): {offset.TotalMilliseconds:F0} ms");
    Console.WriteLine($"Скорректированное время клиента: {correctedNow:HH:mm:ss.fff}");
}

static async Task HandleGetEvents(NetworkClient client)
{
    var response = await client.SendAsync("GetEvents", new GetEventsRequestDto());
    PrintDataInstance(response);
    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    var result = response.Data.Value.Deserialize<GetEventsResponseDto>(JsonDefaults.Options);
    if (result is null || result.Events.Count == 0)
    {
        Console.WriteLine("Событий нет");
        return;
    }

    foreach (var e in result.Events)
    {
        Console.WriteLine($"{e.Id} | {e.Title} | {e.StartsAt} | {e.Venue} | цена {e.BasePrice} | свободно мест {e.FreeSeats}");
    }
}

static async Task HandleReserveSeat(NetworkClient client)
{
    Console.Write("Id события: ");
    var eventId = int.Parse(Console.ReadLine() ?? string.Empty);

    Console.Write("Id места: ");
    var seatId = int.Parse(Console.ReadLine() ?? string.Empty);

    Console.Write("ФИО покупателя: ");
    var fullName = Console.ReadLine() ?? string.Empty;

    Console.Write("Email покупателя: ");
    var email = Console.ReadLine() ?? string.Empty;

    var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
    {
        EventId = eventId,
        SeatId = seatId,
        CustomerFullName = fullName,
        CustomerEmail = email
    });
    PrintDataInstance(response);

    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    var result = response.Data.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options);
    Console.WriteLine($"Место забронировано. TicketId = {result?.TicketId}");
}

static async Task HandleBuyTicket(NetworkClient client)
{
    Console.Write("Id билета: ");
    var ticketId = int.Parse(Console.ReadLine() ?? string.Empty);

    var response = await client.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId });
    PrintDataInstance(response);

    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    var result = response.Data.Value.Deserialize<BuyTicketResponseDto>(JsonDefaults.Options);
    Console.WriteLine($"Билет куплен. PaymentId = {result?.PaymentId}, сумма {result?.Amount}, статус {result?.Status}");
}

static async Task HandleCancelTicket(NetworkClient client)
{
    Console.Write("Id билета: ");
    var ticketId = int.Parse(Console.ReadLine() ?? string.Empty);

    var response = await client.SendAsync("CancelTicket", new CancelTicketRequestDto { TicketId = ticketId });
    PrintDataInstance(response);

    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    var result = response.Data.Value.Deserialize<CancelTicketResponseDto>(JsonDefaults.Options);
    Console.WriteLine($"Билет {result?.TicketId} переведен в статус {result?.Status}");
}

static void PrintDataInstance(Response response)
{
    if (response.DataInstance is not null)
    {
        Console.WriteLine($"[Экземпляр данных: {response.DataInstance}]");
    }
}

static async Task HandlePubSubStatus(NetworkClient client)
{
    PrintPubSubStatus(await client.SendAsync("GetPubSubStatus", new GetPubSubStatusRequestDto()));
}

static async Task HandleSetPubSubAvailability(NetworkClient client, string prompt, bool transient)
{
    Console.Write($"{prompt}: ");
    var request = new SetPubSubAvailabilityRequestDto { Component = (Console.ReadLine() ?? string.Empty).Trim(), Available = true };

    if (transient)
    {
        Console.Write("Сколько попыток доставки завершить ошибкой [2]: ");
        request.TransientFailures = int.TryParse(Console.ReadLine(), out var failures) ? failures : 2;
    }
    else
    {
        Console.Write("1 — смоделировать отказ, 2 — восстановить: ");
        request.Available = (Console.ReadLine() ?? string.Empty).Trim() == "2";
    }

    PrintPubSubStatus(await client.SendAsync("SetPubSubAvailability", request));
}

static async Task HandleSubscribe(NetworkClient client)
{
    Console.Write("Subscriber [AnalyticsService]: ");
    var subscriber = Console.ReadLine();
    Console.Write("Тип события (TicketPurchased / SeatReserved / TicketCancelled): ");
    var eventType = (Console.ReadLine() ?? string.Empty).Trim();

    PrintPubSubStatus(await client.SendAsync("Subscribe", new SubscribeRequestDto
    {
        Subscriber = string.IsNullOrWhiteSpace(subscriber) ? "AnalyticsService" : subscriber.Trim(),
        EventType = eventType
    }));
}

static void PrintPubSubStatus(Response response)
{
    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    var status = response.Data.Value.Deserialize<PubSubStatusDto>(JsonDefaults.Options)!;
    foreach (var broker in status.Brokers)
    {
        Console.WriteLine($"{broker.Name}: {(broker.Available ? "AVAILABLE" : "UNAVAILABLE")} | в очереди: {broker.Pending}");
    }

    foreach (var subscriber in status.Subscribers)
    {
        var types = subscriber.EventTypes.Count == 0 ? "нет подписок" : string.Join(", ", subscriber.EventTypes);
        Console.WriteLine($"{subscriber.Name}: {(subscriber.Available ? "AVAILABLE" : "UNAVAILABLE")} | {types} | обработано: {subscriber.Processed}");
    }

    Console.WriteLine($"Статистика AnalyticsService по типам: {string.Join(", ", status.EventsByType.Select(p => $"{p.Key}={p.Value}"))}");
    Console.WriteLine($"Статистика AnalyticsService по клиентам: {string.Join(", ", status.EventsByClient.Select(p => $"{p.Key}={p.Value}"))}");
    Console.WriteLine("Последние доставки:");
    foreach (var delivery in status.LastDeliveries)
    {
        Console.WriteLine($"  {delivery}");
    }
}
