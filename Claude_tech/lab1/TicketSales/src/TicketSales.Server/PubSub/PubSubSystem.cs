using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Replication;
using TicketSales.Shared.Dto;

namespace TicketSales.Server.PubSub;

public class PubSubSystem
{
    public PubSubSystem(Broker[] brokers, IEnumerable<Subscriber> subscribers)
    {
        Brokers = brokers;
        Publisher = new Publisher(brokers);
        Subscribers = subscribers.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Broker[] Brokers { get; }
    public Publisher Publisher { get; }
    public Dictionary<string, Subscriber> Subscribers { get; }
    public AnalyticsService Analytics => (AnalyticsService)Subscribers["AnalyticsService"];

    public static PubSubSystem Create(double delayFactor = 1)
    {
        TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value * delayFactor);

        var system = new PubSubSystem(
            new[] { new Broker("Broker-1", Seconds(0.3)), new Broker("Broker-2", Seconds(0.3)) },
            new Subscriber[]
            {
                new LoggerSubscriber(Seconds(1)),
                new SeatService(Seconds(3)),
                new NotificationService(Seconds(2)),
                new AnalyticsService(Seconds(0.5))
            });

        foreach (var type in EventTypes.All)
        {
            system.Subscribe("Logger", type);
            system.Subscribe("SeatService", type);
        }

        system.Subscribe("NotificationService", EventTypes.TicketPurchased);
        system.Subscribe("NotificationService", EventTypes.TicketCancelled);
        return system;
    }

    public void Subscribe(string subscriberName, string eventType)
    {
        var subscriber = FindSubscriber(subscriberName);
        foreach (var broker in Brokers)
        {
            broker.Subscribe(subscriber, eventType);
        }
    }

    public async Task<PubSubStatusDto> SetAvailabilityAsync(string component, bool available, int transientFailures)
    {
        var broker = Brokers.FirstOrDefault(b => string.Equals(b.Name, component, StringComparison.OrdinalIgnoreCase));
        if (broker is not null)
        {
            broker.Available = available;
            ReplicationLog.Write($"{broker.Name} → {(available ? "AVAILABLE" : "UNAVAILABLE")}");
            return GetStatus();
        }

        var subscriber = FindSubscriber(component);
        subscriber.Available = available;
        subscriber.TransientFailures = transientFailures;
        ReplicationLog.Write($"{subscriber.Name} → {(available ? "AVAILABLE" : "UNAVAILABLE")}" +
                             (transientFailures > 0 ? $", временных ошибок: {transientFailures}" : string.Empty));

        if (available)
        {
            foreach (var b in Brokers)
            {
                await b.RedeliverPendingAsync();
            }
        }

        return GetStatus();
    }

    public PubSubStatusDto GetStatus() => new()
    {
        Brokers = Brokers.Select(b => new BrokerStatusDto { Name = b.Name, Available = b.Available, Pending = b.Pending.Count }).ToList(),
        Subscribers = Subscribers.Values.Select(s => new SubscriberStatusDto
        {
            Name = s.Name,
            Available = s.Available,
            EventTypes = Brokers[0].SubscriptionsOf(s).ToList(),
            Processed = s.Received.Count
        }).ToList(),
        EventsByType = new Dictionary<string, int>(Analytics.ByType),
        EventsByClient = new Dictionary<string, int>(Analytics.ByClient),
        LastDeliveries = Brokers.SelectMany(b => b.History).OrderBy(r => r.Finished).TakeLast(10)
            .Select(r => $"[{r.Finished:HH:mm:ss.fff}] {r.Broker}: {r.EventType} → {r.Subscriber} {r.Status}, попыток {r.Attempts}, {r.DurationMs:F0} ms{(r.Error is null ? "" : $" ({r.Error})")}")
            .ToList()
    };

    private Subscriber FindSubscriber(string name) =>
        Subscribers.TryGetValue(name ?? string.Empty, out var subscriber)
            ? subscriber
            : throw new NotFoundException($"Subscriber '{name}' не зарегистрирован");
}
