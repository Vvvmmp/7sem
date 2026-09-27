using System;
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
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

Console.OutputEncoding = Encoding.UTF8;

const string connectionString = "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales";
var connectionFactory = new PostgresConnectionFactory(connectionString);

Console.WriteLine("Сброс и повторное заполнение базы данных для чистого эксперимента...");
PostgresSchemaInitializer.ResetAndSeed(connectionFactory);

var eventRepository = new PostgresEventRepository(connectionFactory);
var seatRepository = new PostgresSeatRepository(connectionFactory);
var ticketRepository = new PostgresTicketRepository(connectionFactory);
var customerRepository = new PostgresCustomerRepository(connectionFactory);
var paymentRepository = new PostgresPaymentRepository(connectionFactory);

var service = new TicketSalesServicePostgres(
    eventRepository, seatRepository, ticketRepository, customerRepository, paymentRepository, connectionFactory);

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
var server = new TcpServer(IPAddress.Loopback, 0, dispatcher);
using var cts = new CancellationTokenSource();
var serverTask = server.RunAsync(cts.Token);

Console.WriteLine($"Сервер запущен на 127.0.0.1:{server.Port}");
Console.WriteLine();

await RunExperiment("Эксперимент 1: без транзакции, без ошибки", seatId: 1, useTransaction: false, simulateError: false);
await RunExperiment("Эксперимент 2: без транзакции, с искусственной ошибкой", seatId: 2, useTransaction: false, simulateError: true);
await RunExperiment("Эксперимент 3: с транзакцией, без ошибки (COMMIT)", seatId: 3, useTransaction: true, simulateError: false);
await RunExperiment("Эксперимент 4: с транзакцией, с искусственной ошибкой (ROLLBACK)", seatId: 4, useTransaction: true, simulateError: true);

cts.Cancel();
try { await serverTask; } catch { }

async Task RunExperiment(string title, int seatId, bool useTransaction, bool simulateError)
{
    Console.WriteLine($"=== {title} ===");

    using var client = new NetworkClient();
    await client.ConnectAsync("127.0.0.1", server.Port);

    var reserveResponse = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
    {
        EventId = 1,
        SeatId = seatId,
        CustomerFullName = $"Demo Client (seat {seatId})",
        CustomerEmail = $"demo-seat-{seatId}@example.com",
        UseTransaction = useTransaction,
        SimulateErrorAfterTicketInsert = simulateError
    });

    Console.WriteLine(reserveResponse.Success
        ? $"ReserveSeat: SUCCESS, ticketId={reserveResponse.Data!.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options)?.TicketId}"
        : $"ReserveSeat: FAIL ({reserveResponse.Error?.Message})");

    var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
    var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);
    Console.WriteLine($"Состояние БД после эксперимента: seat.Status={snapshot?.Status}, ticketCount={snapshot?.TicketCount}");
    Console.WriteLine();
}
