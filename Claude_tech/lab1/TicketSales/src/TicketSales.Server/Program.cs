using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.Postgres;

var port = 5050;
if (args.Length > 0 && int.TryParse(args[0], out var parsedPort))
{
    port = parsedPort;
}

var serverTimeDelayMs = 0;
if (args.Length > 1 && int.TryParse(args[1], out var parsedDelay))
{
    serverTimeDelayMs = parsedDelay;
}

var connectionString = args.Length > 2
    ? args[2]
    : "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales";

var connectionFactory = new PostgresConnectionFactory(connectionString);

try
{
    using var probe = connectionFactory.Open();
}
catch (Exception ex)
{
    Console.WriteLine($"Не удалось подключиться к базе данных: {ex.Message}");
    Console.WriteLine("Убедитесь, что запущен docker compose (docker compose up -d) и БД доступна.");
    return;
}

PostgresSchemaInitializer.EnsureCreatedAndSeeded(connectionFactory);

var eventRepository = new PostgresEventRepository(connectionFactory);
var seatRepository = new PostgresSeatRepository(connectionFactory);
var ticketRepository = new PostgresTicketRepository(connectionFactory);
var customerRepository = new PostgresCustomerRepository(connectionFactory);
var paymentRepository = new PostgresPaymentRepository(connectionFactory);

var service = new TicketSalesServicePostgres(
    eventRepository,
    seatRepository,
    ticketRepository,
    customerRepository,
    paymentRepository,
    connectionFactory,
    TimeSpan.FromMilliseconds(serverTimeDelayMs));

IOperationHandler[] handlers =
{
    new GetEventsHandler(service),
    new ReserveSeatHandler(service),
    new BuyTicketHandler(service),
    new CancelTicketHandler(service),
    new GetServerTimeHandler(service),
    new GetSeatSnapshotHandler(service)
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
