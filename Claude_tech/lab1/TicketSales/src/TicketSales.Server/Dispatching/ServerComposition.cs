using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.PubSub;
using TicketSales.Server.Replication;

namespace TicketSales.Server.Dispatching;

/// <summary>Единый набор операций сервера (используется сервером, тестами и демо).</summary>
public static class ServerComposition
{
    public static OperationDispatcher CreateDispatcher(ReplicationManager replication, PubSubSystem? pubSub = null)
    {
        pubSub ??= PubSubSystem.Create(0);
        var service = new ReplicatedTicketSalesService(replication, pubSub.Publisher);

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
            new SynchronizeInstancesHandler(replication),
            new GetPubSubStatusHandler(pubSub),
            new SetPubSubAvailabilityHandler(pubSub),
            new SubscribeHandler(pubSub)
        };

        return new OperationDispatcher(handlers);
    }
}
