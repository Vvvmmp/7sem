using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.InMemory;
using Xunit;

namespace TicketSales.Tests;

public class TestServerFixture : IAsyncLifetime
{
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public int Port { get; private set; }

    public Task InitializeAsync()
    {
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
            try
            {
                await _serverTask;
            }
            catch
            {
            }
        }
    }
}
