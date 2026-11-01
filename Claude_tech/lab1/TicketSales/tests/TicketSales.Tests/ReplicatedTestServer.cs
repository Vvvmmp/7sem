using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Cluster;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Networking;
using TicketSales.Server.PubSub;
using TicketSales.Server.Replication;
using TicketSales.Server.Storage.Postgres;

namespace TicketSales.Tests;

public sealed class ReplicatedTestServer : IAsyncDisposable
{
    public const string PrimaryConnectionString =
        "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2";

    public const string ReplicaConnectionString =
        "Host=localhost;Port=5434;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2";

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;

    private ReplicatedTestServer(bool resetDatabases)
    {
        var primary = new DataInstance("Primary", PrimaryConnectionString);
        var replica = new DataInstance("Replica", ReplicaConnectionString);
        if (resetDatabases)
        {
            PostgresSchemaInitializer.ResetAndSeed(primary.ConnectionFactory);
            PostgresSchemaInitializer.ResetAndSeed(replica.ConnectionFactory);
        }

        Replication = new ReplicationManager(primary, replica);
        Replication.Initialize();

        var server = new TcpServer(IPAddress.Loopback, 0, ServerComposition.CreateDispatcher(Replication, PubSub, Cluster));
        Port = server.Port;
        _serverTask = server.RunAsync(_cts.Token);
    }

    public ReplicationManager Replication { get; }
    public PubSubSystem PubSub { get; } = PubSubSystem.Create(0);
    public NodeCluster Cluster { get; } = NodeCluster.Create(heartbeatTimeout: TimeSpan.FromMilliseconds(20), writeToConsole: false);
    public int Port { get; }

    public static ReplicatedTestServer Start(bool resetDatabases = true) => new(resetDatabases);

    public async Task<NetworkClient> ConnectAsync()
    {
        var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", Port);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _serverTask;
        }
        catch
        {
        }

        Replication.Primary.SimulatedFailure = false;
        Replication.Replica.SimulatedFailure = false;
    }
}
