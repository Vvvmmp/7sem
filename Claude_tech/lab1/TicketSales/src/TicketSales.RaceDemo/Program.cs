using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.InMemory;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;

Console.OutputEncoding = Encoding.UTF8;


const int ClientsCount = 5;

var eventRepository = new InMemoryEventRepository();
var seatRepository = new InMemorySeatRepository();
var ticketRepository = new InMemoryTicketRepository();
var customerRepository = new InMemoryCustomerRepository();
var paymentRepository = new InMemoryPaymentRepository();

SeedData.Populate(eventRepository, seatRepository);

var eventId = SeedData.FirstEventId;
var seatId = SeedData.FirstEventFirstSeatId;

var service = new TicketSalesService(
    eventRepository,
    seatRepository,
    ticketRepository,
    customerRepository,
    paymentRepository);

IOperationHandler[] handlers =
{
    new GetEventsHandler(service),
    new ReserveSeatHandler(service),
    new BuyTicketHandler(service),
    new CancelTicketHandler(service)
};

var dispatcher = new OperationDispatcher(handlers);
var server = new TcpServer(IPAddress.Loopback, 0, dispatcher);

using var cts = new CancellationTokenSource();
var serverTask = server.RunAsync(cts.Token);

Console.WriteLine($"Сервер запущен на 127.0.0.1:{server.Port}");
Console.WriteLine($"Цель гонки: eventId={eventId}, seatId={seatId} (изначально свободно)");
Console.WriteLine($"Параллельно запускаем {ClientsCount} клиентов на ОДНО и то же место...");
Console.WriteLine();

var results = await Task.WhenAll(Enumerable.Range(1, ClientsCount)
    .Select(i => ReserveAsSeparateClientAsync(server.Port, eventId, seatId, i)));

Console.WriteLine();
Console.WriteLine("=== Итоги ===");
foreach (var r in results)
{
    Console.WriteLine(r);
}

var successCount = results.Count(r => r.Success);
Console.WriteLine();
Console.WriteLine($"Успешных броней одного и того же места: {successCount} из {ClientsCount}");

var seatAfter = seatRepository.GetById(seatId);
var ticketsForSeat = ticketRepository.GetAll().Count(t => t.SeatId == seatId);
Console.WriteLine($"Итоговый статус места в репозитории: {seatAfter?.Status}");
Console.WriteLine($"Количество созданных билетов на это место: {ticketsForSeat}");

Console.WriteLine();
if (successCount > 1)
{
    Console.WriteLine("race cond: одно и то же место забронировано более одного раза.");
}
else
{
    Console.WriteLine("В этом прогоне гонка не проявилась.");
}

cts.Cancel();
try
{
    await serverTask;
}
catch
{
}

static async Task<ClientResult> ReserveAsSeparateClientAsync(int port, int eventId, int seatId, int clientNumber)
{
    using var client = new NetworkClient();
    await client.ConnectAsync("127.0.0.1", port);

    var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
    {
        EventId = eventId,
        SeatId = seatId,
        CustomerFullName = $"Клиент {clientNumber}",
        CustomerEmail = $"client{clientNumber}@example.com"
    });

    if (response.Success && response.Data is not null)
    {
        var result = response.Data.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options);
        return new ClientResult(clientNumber, true, $"клиент {clientNumber}: успех, ticketId={result?.TicketId}");
    }

    return new ClientResult(clientNumber, false, $"клиент {clientNumber}: отказ ({response.Error?.Message})");
}

internal record ClientResult(int ClientNumber, bool Success, string Message)
{
    public override string ToString() => Message;
}
