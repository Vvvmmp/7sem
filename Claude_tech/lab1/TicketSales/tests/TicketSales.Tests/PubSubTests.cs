using System.Linq;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.PubSub;
using Xunit;

namespace TicketSales.Tests;

public class PubSubTests
{
    private readonly PubSubSystem _pubSub = PubSubSystem.Create(0);

    private Subscriber Get(string name) => _pubSub.Subscribers[name];

    private async Task<PublishResult> PublishAndWait(string type, int entityId = 1)
    {
        var result = _pubSub.Publisher.Publish(new PubSubEvent(type, entityId));
        await result.Delivery;
        return result;
    }

    [Fact]
    public void Event_IsCreatedWithTypeEntityTimestampAndData()
    {
        var e = new PubSubEvent(EventTypes.SeatReserved, 7, new() { ["TicketId"] = "3" });

        Assert.Equal(EventTypes.SeatReserved, e.Type);
        Assert.Equal(7, e.EntityId);
        Assert.Equal("3", e.Data["TicketId"]);
        Assert.Equal("Server", e.ClientId);
        Assert.True((System.DateTime.Now - e.Timestamp).TotalSeconds < 5);
        e.Validate();
    }

    [Theory]
    [InlineData("OrderCreated", 1)]
    [InlineData(EventTypes.SeatReserved, 0)]
    public async Task InvalidOrUnknownEvent_IsRejected(string type, int entityId)
    {
        Assert.Throws<ValidationException>(() => new PubSubEvent(type, entityId).Validate());

        var result = await PublishAndWait(type, entityId);
        Assert.False(result.Success);
        Assert.Empty(Get("Logger").Received);
    }

    [Fact]
    public async Task Publish_IsAcceptedByBroker_AndDeliveredToSubscriber()
    {
        var result = await PublishAndWait(EventTypes.SeatReserved);

        Assert.True(result.Success);
        Assert.Equal("Broker-1", result.Broker);
        Assert.Single(Get("Logger").Received);
    }

    [Fact]
    public void Subscribe_RegistersSubscriptionInEveryBroker()
    {
        _pubSub.Subscribe("AnalyticsService", EventTypes.TicketCancelled);

        Assert.All(_pubSub.Brokers, b => Assert.Equal(new[] { EventTypes.TicketCancelled }, b.SubscriptionsOf(Get("AnalyticsService"))));
    }

    [Fact]
    public void Subscribe_UnknownSubscriberOrEventType_Fails()
    {
        Assert.Throws<NotFoundException>(() => _pubSub.Subscribe("StockService", EventTypes.SeatReserved));
        Assert.Throws<ValidationException>(() => _pubSub.Subscribe("Logger", "OrderCreated"));
    }

    [Fact]
    public async Task NotSubscribedSubscriber_DoesNotReceiveEvent()
    {
        await PublishAndWait(EventTypes.SeatReserved);

        Assert.Empty(Get("NotificationService").Received);
        Assert.Empty(Get("AnalyticsService").Received);
    }

    [Fact]
    public async Task OneEvent_IsDeliveredToAllSubscribers()
    {
        var results = await _pubSub.Publisher.Publish(new PubSubEvent(EventTypes.TicketPurchased, 5)).Delivery;

        Assert.Equal(new[] { "Logger", "NotificationService", "SeatService" }, results.Select(r => r.Subscriber).OrderBy(n => n));
        Assert.All(results, r => Assert.Equal("SUCCESS", r.Status));
    }

    [Fact]
    public async Task SeveralEventTypes_AreRoutedBySubscription()
    {
        foreach (var type in EventTypes.All)
        {
            await PublishAndWait(type);
        }

        Assert.Equal(3, Get("Logger").Received.Count);
        Assert.Equal(3, Get("SeatService").Received.Count);
        Assert.Equal(new[] { EventTypes.TicketPurchased, EventTypes.TicketCancelled }, Get("NotificationService").Received.Select(e => e.Type));
    }

    [Fact]
    public async Task NoSubscribers_EventIsAcceptedWithoutDeliveries()
    {
        _pubSub.Brokers[0].Unsubscribe(Get("Logger"), EventTypes.SeatReserved);
        _pubSub.Brokers[0].Unsubscribe(Get("SeatService"), EventTypes.SeatReserved);

        var result = _pubSub.Publisher.Publish(new PubSubEvent(EventTypes.SeatReserved, 1));

        Assert.True(result.Success);
        Assert.Empty(await result.Delivery);
    }

    [Fact]
    public async Task UnavailableSubscriber_IsQueued_OthersContinue()
    {
        Get("SeatService").Available = false;

        var results = await _pubSub.Publisher.Publish(new PubSubEvent(EventTypes.TicketPurchased, 1)).Delivery;

        var failed = results.Single(r => r.Subscriber == "SeatService");
        Assert.Equal("QUEUED", failed.Status);
        Assert.Equal(3, failed.Attempts);
        Assert.Contains("недоступен", failed.Error);
        Assert.All(results.Where(r => r.Subscriber != "SeatService"), r => Assert.Equal("SUCCESS", r.Status));
        Assert.Single(_pubSub.Brokers[0].Pending);

        Assert.True((await PublishAndWait(EventTypes.TicketCancelled)).Success);
        Assert.Equal(2, Get("Logger").Received.Count);
    }

    [Fact]
    public async Task QueuedEvent_IsNotLost_AndRedeliveredAfterRecovery()
    {
        Get("SeatService").Available = false;
        await PublishAndWait(EventTypes.SeatReserved, 9);

        await _pubSub.SetAvailabilityAsync("SeatService", true, 0);

        Assert.Empty(_pubSub.Brokers[0].Pending);
        Assert.Equal(9, Get("SeatService").Received.Single().EntityId);
    }

    [Fact]
    public async Task TransientFailures_AreRetried_UntilSuccess()
    {
        await _pubSub.SetAvailabilityAsync("Logger", true, 2);

        var results = await _pubSub.Publisher.Publish(new PubSubEvent(EventTypes.SeatReserved, 1)).Delivery;

        var logger = results.Single(r => r.Subscriber == "Logger");
        Assert.Equal("SUCCESS", logger.Status);
        Assert.Equal(3, logger.Attempts);
    }

    [Fact]
    public async Task NewSubscriber_ReceivesEvent_WithSamePublisher()
    {
        var publisher = _pubSub.Publisher;
        await PublishAndWait(EventTypes.TicketPurchased);
        Assert.Empty(Get("AnalyticsService").Received);

        _pubSub.Subscribe("AnalyticsService", EventTypes.TicketPurchased);
        await PublishAndWait(EventTypes.TicketPurchased);

        Assert.Same(publisher, _pubSub.Publisher);
        Assert.Single(Get("AnalyticsService").Received);
        Assert.Equal(1, _pubSub.Analytics.ByType[EventTypes.TicketPurchased]);
    }

    [Fact]
    public async Task FirstBrokerUnavailable_PublisherUsesSecondBroker()
    {
        _pubSub.Brokers[0].Available = false;

        var result = await PublishAndWait(EventTypes.SeatReserved);

        Assert.True(result.Success);
        Assert.Equal("Broker-2", result.Broker);
        Assert.Single(Get("Logger").Received);
    }

    [Fact]
    public async Task AllBrokersUnavailable_PublisherReturnsClearError()
    {
        foreach (var broker in _pubSub.Brokers)
        {
            broker.Available = false;
        }

        var result = await PublishAndWait(EventTypes.SeatReserved);

        Assert.False(result.Success);
        Assert.Contains("Broker недоступен", result.Error);
    }

    [Fact]
    public async Task Status_ShowsSubscriptionsQueueAndStatistics()
    {
        _pubSub.Subscribe("AnalyticsService", EventTypes.SeatReserved);
        await PublishAndWait(EventTypes.SeatReserved);

        var status = _pubSub.GetStatus();

        Assert.Equal(1, status.EventsByType[EventTypes.SeatReserved]);
        Assert.Equal(1, status.EventsByClient["Server"]);
        Assert.Equal(3, status.LastDeliveries.Count);
        Assert.Equal(EventTypes.All, status.Subscribers.Single(s => s.Name == "Logger").EventTypes.OrderBy(t => System.Array.IndexOf(EventTypes.All, t)));
    }
}
