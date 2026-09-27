using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.Postgres;
using Xunit;

namespace TicketSales.Tests;

public class PostgresTestServerFixture : IAsyncLifetime
{
    public const string ConnectionString = "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales";

    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public int Port { get; private set; }

    public Task InitializeAsync()
    {
        var connectionFactory = new PostgresConnectionFactory(ConnectionString);
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
        Port = server.Port;

        _cts = new CancellationTokenSource();
        _serverTask = server.RunAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts?.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
        }
    }
}
