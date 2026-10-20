using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TicketSales.Server.Replication;

namespace TicketSales.Server.PubSub;

public record PublishResult(bool Success, string? Broker, string? Error, Task<List<DeliveryResult>> Delivery);

public class Publisher
{
    private readonly IReadOnlyList<Broker> _brokers;

    public Publisher(IReadOnlyList<Broker> brokers)
    {
        _brokers = brokers;
    }

    public PublishResult Publish(PubSubEvent e)
    {
        ReplicationLog.Write($"Publisher → PUBLISH {e}");
        var errors = new List<string>();

        foreach (var broker in _brokers)
        {
            try
            {
                var delivery = broker.Publish(e);
                ReplicationLog.Write($"Publisher → ACCEPTED by {broker.Name}");
                return new PublishResult(true, broker.Name, null, delivery);
            }
            catch (BrokerUnavailableException ex)
            {
                errors.Add(ex.Message);
                ReplicationLog.Write($"Publisher → {broker.Name} UNAVAILABLE, переключение на следующий Broker");
            }
            catch (Exception ex)
            {
                return Failed(ex.Message);
            }
        }

        return Failed(errors.Count == 0 ? "Broker не настроен" : $"Broker недоступен: {string.Join("; ", errors)}");
    }

    private static PublishResult Failed(string error)
    {
        ReplicationLog.Write($"Publisher → FAIL: {error}");
        return new PublishResult(false, null, error, Task.FromResult(new List<DeliveryResult>()));
    }
}
