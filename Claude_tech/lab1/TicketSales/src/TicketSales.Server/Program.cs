using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.InMemory;

var port = 5050;
if (args.Length > 0 && int.TryParse(args[0], out var parsedPort))
{
    port = parsedPort;
}

var eventRepository = new InMemoryEventRepository();
var seatRepository = new InMemorySeatRepository();
var ticketRepository = new InMemoryTicketRepository();
var customerRepository = new InMemoryCustomerRepository();
var paymentRepository = new InMemoryPaymentRepository();

SeedData.Populate(eventRepository, seatRepository);

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
var server = new TcpServer(IPAddress.Any, port, dispatcher);

Console.WriteLine($"Сервер продажи билетов запущен на порту {server.Port}");
Console.WriteLine("Нажмите Ctrl+C для остановки");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunAsync(cts.Token);

Console.WriteLine("Сервер остановлен");
