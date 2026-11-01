using System;
using TicketSales.Server.Cluster;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.PubSub;
using TicketSales.Server.Replication;

namespace TicketSales.Server.Dispatching;

public static class ServerComposition
{
    public static OperationDispatcher CreateDispatcher(ReplicationManager replication, PubSubSystem? pubSub = null, NodeCluster? cluster = null)
    {
        pubSub ??= PubSubSystem.Create(0);
        cluster ??= NodeCluster.Create(heartbeatTimeout: TimeSpan.FromMilliseconds(50), writeToConsole: false);
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
            new SubscribeHandler(pubSub),
            new GetClusterStatusHandler(cluster),
            new SetNodeStateHandler(cluster),
            new CheckHeartbeatHandler(cluster),
            new StartElectionHandler(cluster),
            new ProposeReservationStatusHandler(cluster, service)
        };

        return new OperationDispatcher(handlers);
    }
}
