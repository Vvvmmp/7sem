using System.Collections.Generic;

namespace TicketSales.Shared.Dto;

public class GetPubSubStatusRequestDto
{
}

public class SetPubSubAvailabilityRequestDto
{
    public string Component { get; set; } = string.Empty;
    public bool Available { get; set; }
    public int TransientFailures { get; set; }
}

public class SubscribeRequestDto
{
    public string Subscriber { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
}

public class PubSubStatusDto
{
    public List<BrokerStatusDto> Brokers { get; set; } = new();
    public List<SubscriberStatusDto> Subscribers { get; set; } = new();
    public Dictionary<string, int> EventsByType { get; set; } = new();
    public Dictionary<string, int> EventsByClient { get; set; } = new();
    public List<string> LastDeliveries { get; set; } = new();
}

public class BrokerStatusDto
{
    public string Name { get; set; } = string.Empty;
    public bool Available { get; set; }
    public int Pending { get; set; }
}

public class SubscriberStatusDto
{
    public string Name { get; set; } = string.Empty;
    public bool Available { get; set; }
    public List<string> EventTypes { get; set; } = new();
    public int Processed { get; set; }
}
