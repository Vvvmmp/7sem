using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Replication;

namespace TicketSales.Server.PubSub;

public class SubscriberUnavailableException : Exception
{
    public SubscriberUnavailableException(string name) : base($"{name} недоступен")
    {
    }
}

public abstract class Subscriber
{
    private int _transientFailures;

    protected Subscriber(string name, TimeSpan delay)
    {
        Name = name;
        Delay = delay;
    }

    public string Name { get; }
    public TimeSpan Delay { get; }
    public volatile bool Available = true;
    public ConcurrentQueue<PubSubEvent> Received { get; } = new();

    public int TransientFailures
    {
        get => _transientFailures;
        set => _transientFailures = value;
    }

    public async Task HandleAsync(PubSubEvent e)
    {
        if (!Available)
        {
            throw new SubscriberUnavailableException(Name);
        }

        await Task.Delay(Delay);

        if (Interlocked.Decrement(ref _transientFailures) >= 0)
        {
            throw new InvalidOperationException($"{Name}: временная ошибка обработки {e.Type}");
        }

        Interlocked.Exchange(ref _transientFailures, 0);
        Process(e);
        Received.Enqueue(e);
    }

    protected abstract void Process(PubSubEvent e);
}

public class LoggerSubscriber : Subscriber
{
    public LoggerSubscriber(TimeSpan delay) : base("Logger", delay)
    {
    }

    protected override void Process(PubSubEvent e) =>
        ReplicationLog.Write($"Logger → журнал: {e} at {e.Timestamp:HH:mm:ss.fff}");
}

public class SeatService : Subscriber
{
    public SeatService(TimeSpan delay) : base("SeatService", delay)
    {
    }

    public ConcurrentDictionary<int, string> TicketStates { get; } = new();

    protected override void Process(PubSubEvent e) => TicketStates[e.Data.TryGetValue("TicketId", out var id) ? int.Parse(id) : e.EntityId] = e.Type switch
    {
        EventTypes.SeatReserved => "Reserved",
        EventTypes.TicketPurchased => "Sold",
        _ => "Free"
    };
}

public class NotificationService : Subscriber
{
    public NotificationService(TimeSpan delay) : base("NotificationService", delay)
    {
    }

    protected override void Process(PubSubEvent e) =>
        ReplicationLog.Write($"NotificationService → уведомление покупателю: {e.Type}, билет {e.EntityId}");
}

public class AnalyticsService : Subscriber
{
    public AnalyticsService(TimeSpan delay) : base("AnalyticsService", delay)
    {
    }

    public ConcurrentDictionary<string, int> ByType { get; } = new();
    public ConcurrentDictionary<string, int> ByClient { get; } = new();

    protected override void Process(PubSubEvent e)
    {
        ByType.AddOrUpdate(e.Type, 1, (_, n) => n + 1);
        ByClient.AddOrUpdate(e.ClientId, 1, (_, n) => n + 1);
    }
}
