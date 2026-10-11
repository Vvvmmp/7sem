using System;
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
                await HandleReplicationStatus(client);
                break;
            case "8":
                await HandleSetInstanceAvailability(client);
                break;
            case "9":
                await HandleSynchronizeInstances(client);
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
    Console.WriteLine("7. Состояние Primary / Replica");
    Console.WriteLine("8. Смоделировать отказ / восстановление экземпляра");
    Console.WriteLine("9. Синхронизировать Primary и Replica");
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

static async Task HandleReplicationStatus(NetworkClient client)
{
    Console.Write("Id места для сравнения на Primary и Replica (Enter — пропустить): ");
    var seatInput = Console.ReadLine();
    int? seatId = int.TryParse(seatInput, out var parsedSeatId) ? parsedSeatId : null;

    var response = await client.SendAsync("GetReplicationStatus", new GetReplicationStatusRequestDto { SeatId = seatId });
    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    PrintReplicationStatus(response.Data.Value.Deserialize<ReplicationStatusDto>(JsonDefaults.Options)!);
}

static async Task HandleSetInstanceAvailability(NetworkClient client)
{
    Console.Write("Экземпляр (Primary / Replica): ");
    var instance = (Console.ReadLine() ?? string.Empty).Trim();

    Console.Write("1 — смоделировать отказ, 2 — восстановить: ");
    var available = (Console.ReadLine() ?? string.Empty).Trim() == "2";

    var response = await client.SendAsync("SetInstanceAvailability", new SetInstanceAvailabilityRequestDto
    {
        Instance = instance,
        Available = available
    });

    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    PrintReplicationStatus(response.Data.Value.Deserialize<ReplicationStatusDto>(JsonDefaults.Options)!);
}

static async Task HandleSynchronizeInstances(NetworkClient client)
{
    var response = await client.SendAsync("SynchronizeInstances", new SynchronizeInstancesRequestDto());
    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    var result = response.Data.Value.Deserialize<SynchronizeInstancesResponseDto>(JsonDefaults.Options)!;
    if (result.Source is null)
    {
        Console.WriteLine("Синхронизация не потребовалась: Primary = Replica");
        return;
    }

    Console.WriteLine($"Синхронизация {result.Source} → {result.Target} выполнена");
    Console.WriteLine("Расхождения до синхронизации:");
    foreach (var difference in result.DifferencesBefore)
    {
        Console.WriteLine($"  - {difference}");
    }

    Console.WriteLine(result.ConsistentAfter ? "После синхронизации: Primary = Replica" : "После синхронизации копии отличаются!");
}

static void PrintReplicationStatus(ReplicationStatusDto status)
{
    foreach (var instance in new[] { status.Primary, status.Replica })
    {
        var line = $"{instance.Name}: {instance.State.ToString().ToUpperInvariant()}";
        if (instance.SeatStatus is not null)
        {
            line += $" | место: {instance.SeatStatus}";
        }

        if (instance.FreeSeats is not null)
        {
            line += $" | свободно мест: {instance.FreeSeats} | билетов: {instance.TicketCount}";
        }

        if (instance.NeedsSync)
        {
            line += " | требует синхронизации";
        }

        Console.WriteLine(line);
    }

    Console.WriteLine($"Активный экземпляр: {status.ActiveInstance ?? "нет (оба недоступны)"}");
    Console.WriteLine(status.Consistent switch
    {
        true => "Согласованность: Primary = Replica",
        false => "Согласованность: ЕСТЬ РАСХОЖДЕНИЯ",
        null => "Согласованность: не проверить (экземпляр недоступен)"
    });

    foreach (var difference in status.Differences)
    {
        Console.WriteLine($"  - {difference}");
    }
}
