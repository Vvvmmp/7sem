using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.InMemory;
using TicketSales.Shared.Dto;
using Xunit;

namespace TicketSales.Tests;

public class ReserveSeatSemaphoreConcurrencyTests
{
    [Fact]
    public async Task ConcurrentReserveSeat_WithSemaphoreService_OnlyOneSucceeds()
    {
        var eventRepository = new InMemoryEventRepository();
        var seatRepository = new InMemorySeatRepository();
        var ticketRepository = new InMemoryTicketRepository();
        var customerRepository = new InMemoryCustomerRepository();
        var paymentRepository = new InMemoryPaymentRepository();
        SeedData.Populate(eventRepository, seatRepository);

        var service = new TicketSalesServiceSemaphore(
            eventRepository, seatRepository, ticketRepository, customerRepository, paymentRepository);

        IOperationHandler[] handlers =
        {
            new GetEventsHandler(service),
            new ReserveSeatHandler(service),
            new BuyTicketHandler(service),
            new CancelTicketHandler(service),
            new GetServerTimeHandler(service)
        };
        var dispatcher = new OperationDispatcher(handlers);
        var server = new TcpServer(IPAddress.Loopback, 0, dispatcher);
        using var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            using var client = new NetworkClient();
            await client.ConnectAsync("127.0.0.1", server.Port);
            return await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = SeedData.FirstEventId,
                SeatId = SeedData.FirstEventFirstSeatId,
                CustomerFullName = $"Semaphore Client {i}",
                CustomerEmail = $"semaphore{i}@example.com"
            });
        });

        var responses = await Task.WhenAll(tasks);

        cts.Cancel();
        try { await serverTask; } catch { }

        Assert.Equal(1, responses.Count(r => r.Success));
    }
}
