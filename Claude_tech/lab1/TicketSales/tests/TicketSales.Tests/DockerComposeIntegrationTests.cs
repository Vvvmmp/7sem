using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;
using TicketSales.Client;
using TicketSales.Server.Replication;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

[Trait("Category", "Docker")]
public class DockerComposeIntegrationTests : IAsyncLifetime
{
    private const string Host = "127.0.0.1";
    private const int Port = 5050;

    public async Task InitializeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        PostgresSchemaInitializer.ResetAndSeed(new DataInstance("Primary", ReplicatedTestServer.PrimaryConnectionString).ConnectionFactory);
        PostgresSchemaInitializer.ResetAndSeed(new DataInstance("Replica", ReplicatedTestServer.ReplicaConnectionString).ConnectionFactory);
        await WaitUntilServerHandlesRequestsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MainOperations_ReserveBuyCancel_ThroughContainer()
    {
        using var client = await ConnectAsync();

        var events = await client.SendAsync("GetEvents", new GetEventsRequestDto());
        Assert.True(events.Success);
        Assert.NotEmpty(events.Data!.Value.Deserialize<GetEventsResponseDto>(JsonDefaults.Options)!.Events);

        var ticketId = await ReserveAsync(client, seatId: 3);
        var buy = await client.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId });
        Assert.True(buy.Success);
        Assert.Equal(PaymentStatus.Completed, buy.Data!.Value.Deserialize<BuyTicketResponseDto>(JsonDefaults.Options)!.Status);
        Assert.Equal(SeatStatus.Sold, await GetSeatStatusAsync(client, 3));

        var cancel = await client.SendAsync("CancelTicket", new CancelTicketRequestDto { TicketId = ticketId });
        Assert.True(cancel.Success);
        Assert.Equal(SeatStatus.Free, await GetSeatStatusAsync(client, 3));
    }

    [Fact]
    public async Task InvalidRequests_ReturnErrorCodes()
    {
        using var client = await ConnectAsync();

        var unknown = await client.SendAsync("DropDatabase", new { });
        Assert.Equal(ErrorCodes.UnknownOperation, unknown.Error?.Code);

        var invalid = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto());
        Assert.Equal(ErrorCodes.ValidationError, invalid.Error?.Code);

        var notFound = await client.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = 999_999 });
        Assert.Equal(ErrorCodes.NotFound, notFound.Error?.Code);
    }

    [Fact]
    public async Task DatabaseContainersStopped_ServerReturnsDataUnavailable_ThenRecovers()
    {
        using var client = await ConnectAsync();

        Docker("stop ticketsales-primary ticketsales-replica");
        try
        {
            var response = await client.SendAsync("GetEvents", new GetEventsRequestDto());
            Assert.False(response.Success);
            Assert.Equal(ErrorCodes.DataUnavailable, response.Error?.Code);
            Assert.Contains("недоступны", response.Error!.Message);
        }
        finally
        {
            Docker("start ticketsales-primary ticketsales-replica");
        }

        await WaitUntilServerHandlesRequestsAsync();
        Assert.True((await client.SendAsync("GetEvents", new GetEventsRequestDto())).Success);
    }

    [Fact]
    public async Task ServerContainerRestarted_ClientReconnects_AndDataPersists()
    {
        using var client = await ConnectAsync();
        var ticketId = await ReserveAsync(client, seatId: 4);
        Assert.True((await client.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId })).Success);

        Docker("stop ticketsales-server");
        try
        {
            await Assert.ThrowsAsync<NetworkException>(() => client.SendAsync("GetEvents", new GetEventsRequestDto()));
            await Assert.ThrowsAsync<ServerUnavailableException>(() => client.ReconnectAsync(timeoutMs: 1000));
        }
        finally
        {
            Docker("start ticketsales-server");
        }

        await WaitUntilServerHandlesRequestsAsync();
        await client.ReconnectAsync();

        Assert.Equal(SeatStatus.Sold, await GetSeatStatusAsync(client, 4));
        var buyAgain = await client.SendAsync("BuyTicket", new BuyTicketRequestDto { TicketId = ticketId });
        Assert.Equal(ErrorCodes.ValidationError, buyAgain.Error?.Code);
    }

    [Fact]
    public async Task ConcurrentClients_AllRequestsSucceed_AndOnlyOneReservationWins()
    {
        var reads = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using var client = await ConnectAsync();
            var ok = 0;
            for (var i = 0; i < 25; i++)
            {
                if ((await client.SendAsync("GetEvents", new GetEventsRequestDto())).Success)
                {
                    ok++;
                }
            }

            return ok;
        }));
        Assert.All(reads, ok => Assert.Equal(25, ok));

        var reservations = await Task.WhenAll(Enumerable.Range(0, 10).Select(async i =>
        {
            using var client = await ConnectAsync();
            return await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = 1,
                SeatId = 5,
                CustomerFullName = $"Клиент {i}",
                CustomerEmail = $"race{i}@example.com"
            });
        }));
        Assert.Equal(1, reservations.Count(r => r.Success));
        Assert.All(reservations.Where(r => !r.Success), r => Assert.Equal(ErrorCodes.ValidationError, r.Error?.Code));
    }

    private static async Task<NetworkClient> ConnectAsync()
    {
        var client = new NetworkClient();
        await client.ConnectAsync(Host, Port);
        return client;
    }

    private static async Task<int> ReserveAsync(NetworkClient client, int seatId)
    {
        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Docker Test",
            CustomerEmail = $"docker{seatId}@example.com"
        });
        Assert.True(response.Success, response.Error?.Message);
        return response.Data!.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options)!.TicketId;
    }

    private static async Task<SeatStatus> GetSeatStatusAsync(NetworkClient client, int seatId)
    {
        var response = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        Assert.True(response.Success, response.Error?.Message);
        return response.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options)!.Status;
    }

    private static async Task WaitUntilServerHandlesRequestsAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = await ConnectAsync();
                var response = await client.SendAsync("GetEvents", new GetEventsRequestDto());
                if (response.Success)
                {
                    return;
                }

                last = new Exception(response.Error?.Message);
            }
            catch (Exception ex) when (ex is ServerUnavailableException or NetworkException)
            {
                last = ex;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"Сервер {Host}:{Port} не отвечает. Запустите docker compose up -d --build. Последняя ошибка: {last?.Message}");
    }

    private static void Docker(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker {arguments} завершился с кодом {process.ExitCode}: {process.StandardError.ReadToEnd()}");
        }
    }
}
