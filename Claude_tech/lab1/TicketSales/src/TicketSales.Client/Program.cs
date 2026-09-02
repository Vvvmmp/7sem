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
    Console.WriteLine("5. Выход");
    Console.Write("Выберите пункт: ");
}

static async Task HandleGetEvents(NetworkClient client)
{
    var response = await client.SendAsync("GetEvents", new GetEventsRequestDto());
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

    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка: {response.Error?.Message}");
        return;
    }

    var result = response.Data.Value.Deserialize<CancelTicketResponseDto>(JsonDefaults.Options);
    Console.WriteLine($"Билет {result?.TicketId} переведен в статус {result?.Status}");
}
