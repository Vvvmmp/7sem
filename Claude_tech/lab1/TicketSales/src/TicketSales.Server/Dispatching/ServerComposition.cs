using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Replication;

namespace TicketSales.Server.Dispatching;

/// <summary>Единый набор операций сервера (используется сервером, тестами и демо).</summary>
public static class ServerComposition
{
    public static OperationDispatcher CreateDispatcher(ReplicationManager replication)
    {
        var service = new ReplicatedTicketSalesService(replication);

        IOperationHandler[] handlers =
        {
            new GetEventsHandler(service),
            new ReserveSeatHandler(service),
            new BuyTicketHandler(service),
            new CancelTicketHandler(service),
            new GetServerTimeHandler(service),
            new GetSeatSnapshotHandler(service),
            new GetReplicationStatusHandler(replication),
            new SetInstanceAvailabilityHandler(replication),
            new SynchronizeInstancesHandler(replication)
        };

        return new OperationDispatcher(handlers);
    }
}
