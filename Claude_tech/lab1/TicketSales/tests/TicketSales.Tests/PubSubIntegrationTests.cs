using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.PubSub;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class PubSubIntegrationTests
{
    [Fact]
    public async Task ReserveSeat_PublishesSeatReserved_ToSubscribers()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var response = await Reserve(client, seatId: 1);

        Assert.True(response.Success);
        var status = await WaitForStatus(client, s => s.Subscribers.Single(x => x.Name == "SeatService").Processed == 1);
        Assert.Equal(1, status.Subscribers.Single(x => x.Name == "Logger").Processed);
        Assert.Equal(0, status.Subscribers.Single(x => x.Name == "NotificationService").Processed);
    }

    [Fact]
    public async Task SeveralClients_EventsAreDeliveredPerClient()
    {
        await using var server = ReplicatedTestServer.Start();
        var clients = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => server.ConnectAsync()));
        var subscribe = await clients[0].SendAsync("Subscribe", new SubscribeRequestDto { Subscriber = "AnalyticsService", EventType = EventTypes.SeatReserved });
        Assert.True(subscribe.Success);

        var responses = await Task.WhenAll(clients.Select((c, i) => Reserve(c, seatId: i + 1)));

        Assert.All(responses, r => Assert.True(r.Success));
        var status = await WaitForStatus(clients[0], s => s.EventsByType.GetValueOrDefault(EventTypes.SeatReserved) == 3);
        Assert.Equal(3, status.EventsByClient.Count);
        Assert.All(status.EventsByClient.Values, count => Assert.Equal(1, count));
        foreach (var c in clients)
        {
            c.Dispose();
        }
    }

    [Fact]
    public async Task UnknownSubscriber_ReturnsError()
    {
        await using var server = ReplicatedTestServer.Start();
        using var client = await server.ConnectAsync();

        var response = await client.SendAsync("Subscribe", new SubscribeRequestDto { Subscriber = "StockService", EventType = EventTypes.SeatReserved });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.NotFound, response.Error!.Code);
    }

    private static Task<Response> Reserve(NetworkClient client, int seatId) =>
        client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = $"Покупатель места {seatId}",
            CustomerEmail = $"pubsub{seatId}@example.com"
        });

    private static async Task<PubSubStatusDto> WaitForStatus(NetworkClient client, Func<PubSubStatusDto, bool> condition)
    {
        PubSubStatusDto status;
        var attempts = 0;
        do
        {
            await Task.Delay(100);
            var response = await client.SendAsync("GetPubSubStatus", new GetPubSubStatusRequestDto());
            status = response.Data!.Value.Deserialize<PubSubStatusDto>(JsonDefaults.Options)!;
        }
        while (!condition(status) && ++attempts < 50);

        Assert.True(condition(status));
        return status;
    }
}
