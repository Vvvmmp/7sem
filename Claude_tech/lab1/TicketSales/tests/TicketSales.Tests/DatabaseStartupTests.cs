using System;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Configuration;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Replication;
using Xunit;

namespace TicketSales.Tests;

public class DatabaseStartupTests
{
    [Fact]
    public async Task InitializeWithRetry_DatabaseNeverAvailable_ThrowsClearErrorAfterAllAttempts()
    {
        var replication = CreateReplication(failed: true);

        var ex = await Assert.ThrowsAsync<DataUnavailableException>(() =>
            DatabaseStartup.InitializeWithRetryAsync(replication, 3, TimeSpan.FromMilliseconds(10), CancellationToken.None));

        Assert.Contains("после 3 попыток", ex.Message);
    }

    [Fact]
    public async Task InitializeWithRetry_UnknownHost_ThrowsDataUnavailable()
    {
        var replication = new ReplicationManager(
            new DataInstance("Primary", "Host=no-such-service;Port=5432;Database=x;Username=x;Password=x;Timeout=1"),
            new DataInstance("Replica", "Host=no-such-service;Port=5432;Database=x;Username=x;Password=x;Timeout=1"));

        await Assert.ThrowsAsync<DataUnavailableException>(() =>
            DatabaseStartup.InitializeWithRetryAsync(replication, 2, TimeSpan.FromMilliseconds(10), CancellationToken.None));
    }

    /// <summary>Сервис ещё не готов при старте (как PostgreSQL сразу после docker compose up) и становится доступен позже.</summary>
    [Fact]
    public async Task InitializeWithRetry_DatabaseBecomesReady_Succeeds()
    {
        var replication = CreateReplication(failed: true);

        var startup = DatabaseStartup.InitializeWithRetryAsync(replication, 20, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        await Task.Delay(300);
        Assert.False(startup.IsCompleted);

        replication.Primary.SimulatedFailure = false;
        replication.Replica.SimulatedFailure = false;

        await startup;
        Assert.Empty(replication.FindDifferences());
    }

    private static ReplicationManager CreateReplication(bool failed)
    {
        var primary = new DataInstance("Primary", ReplicatedTestServer.PrimaryConnectionString) { SimulatedFailure = failed };
        var replica = new DataInstance("Replica", ReplicatedTestServer.ReplicaConnectionString) { SimulatedFailure = failed };
        return new ReplicationManager(primary, replica);
    }
}
