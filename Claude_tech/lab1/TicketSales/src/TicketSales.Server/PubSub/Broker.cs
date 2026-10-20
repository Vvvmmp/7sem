using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Replication;

namespace TicketSales.Server.PubSub;

public record DeliveryResult(string Broker, string EventType, string Subscriber, string Status, int Attempts, DateTime Started, DateTime Finished, string? Error)
{
    public double DurationMs => (Finished - Started).TotalMilliseconds;
}

public class BrokerUnavailableException : Exception
{
    public BrokerUnavailableException(string name) : base($"{name} недоступен")
    {
    }
}

public class Broker
{
    private readonly Dictionary<string, List<Subscriber>> _subscriptions = new();

    public Broker(string name, TimeSpan retryDelay, int maxAttempts = 3)
    {
        Name = name;
        RetryDelay = retryDelay;
        MaxAttempts = maxAttempts;
    }

    public string Name { get; }
    public TimeSpan RetryDelay { get; }
    public int MaxAttempts { get; }
    public volatile bool Available = true;
    public ConcurrentQueue<(Subscriber Subscriber, PubSubEvent Event)> Pending { get; } = new();
    public ConcurrentQueue<DeliveryResult> History { get; } = new();

    public void Subscribe(Subscriber subscriber, string eventType)
    {
        if (!EventTypes.IsKnown(eventType))
        {
            throw new ValidationException($"Неизвестный тип события: '{eventType}'");
        }

        lock (_subscriptions)
        {
            if (!_subscriptions.TryGetValue(eventType, out var list))
            {
                _subscriptions[eventType] = list = new List<Subscriber>();
            }

            if (!list.Contains(subscriber))
            {
                list.Add(subscriber);
            }
        }

        ReplicationLog.Write($"{Name} → SUBSCRIBE {subscriber.Name}: {eventType}");
    }

    public void Unsubscribe(Subscriber subscriber, string eventType)
    {
        lock (_subscriptions)
        {
            if (_subscriptions.TryGetValue(eventType, out var list))
            {
                list.Remove(subscriber);
            }
        }
    }

    public string[] SubscriptionsOf(Subscriber subscriber)
    {
        lock (_subscriptions)
        {
            return _subscriptions.Where(p => p.Value.Contains(subscriber)).Select(p => p.Key).ToArray();
        }
    }

    public Task<List<DeliveryResult>> Publish(PubSubEvent e)
    {
        if (!Available)
        {
            throw new BrokerUnavailableException(Name);
        }

        e.Validate();
        ReplicationLog.Write($"{Name} → EVENT RECEIVED {e}");

        Subscriber[] recipients;
        lock (_subscriptions)
        {
            recipients = _subscriptions.TryGetValue(e.Type, out var list) ? list.ToArray() : Array.Empty<Subscriber>();
        }

        if (recipients.Length == 0)
        {
            ReplicationLog.Write($"{Name} → NO SUBSCRIBERS for {e.Type}");
            return Task.FromResult(new List<DeliveryResult>());
        }

        ReplicationLog.Write($"{Name} → получатели {e.Type}: {string.Join(", ", recipients.Select(s => s.Name))}");
        return Task.Run(() => DeliverAllAsync(e, recipients));
    }

    public async Task<List<DeliveryResult>> RedeliverPendingAsync()
    {
        var batch = new List<(Subscriber Subscriber, PubSubEvent Event)>();
        while (Pending.TryDequeue(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
        {
            return new List<DeliveryResult>();
        }

        ReplicationLog.Write($"{Name} → REDELIVERY из очереди: {batch.Count}");
        return (await Task.WhenAll(batch.Select(p => DeliverAsync(p.Subscriber, p.Event)))).ToList();
    }

    private async Task<List<DeliveryResult>> DeliverAllAsync(PubSubEvent e, Subscriber[] recipients)
    {
        var results = await Task.WhenAll(recipients.Select(s => Task.Run(() => DeliverAsync(s, e))));
        var succeeded = results.Count(r => r.Status == "SUCCESS");
        ReplicationLog.Write($"{Name} → ALL DONE {e.Type}: SUCCESS {succeeded}/{results.Length}, {(DateTime.Now - e.Timestamp).TotalMilliseconds:F0} ms от публикации");
        return results.ToList();
    }

    private async Task<DeliveryResult> DeliverAsync(Subscriber subscriber, PubSubEvent e)
    {
        var started = DateTime.Now;
        string? error = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ReplicationLog.Write($"{Name} → {subscriber.Name} {e.Type} (attempt {attempt})");
            try
            {
                await subscriber.HandleAsync(e);
                ReplicationLog.Write($"{subscriber.Name} → SUCCESS {e.Type} (attempt {attempt}, {(DateTime.Now - started).TotalMilliseconds:F0} ms)");
                return Record(new DeliveryResult(Name, e.Type, subscriber.Name, "SUCCESS", attempt, started, DateTime.Now, null));
            }
            catch (SubscriberUnavailableException ex)
            {
                error = ex.Message;
                ReplicationLog.Write($"{subscriber.Name} → UNAVAILABLE {e.Type} (attempt {attempt}): {ex.Message}");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                ReplicationLog.Write($"{subscriber.Name} → FAIL {e.Type} (attempt {attempt}): {ex.Message}");
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay);
            }
        }

        Pending.Enqueue((subscriber, e));
        ReplicationLog.Write($"{Name} → QUEUED {e.Type} для {subscriber.Name} (в очереди: {Pending.Count})");
        return Record(new DeliveryResult(Name, e.Type, subscriber.Name, "QUEUED", MaxAttempts, started, DateTime.Now, error));
    }

    private DeliveryResult Record(DeliveryResult result)
    {
        History.Enqueue(result);
        return result;
    }
}
