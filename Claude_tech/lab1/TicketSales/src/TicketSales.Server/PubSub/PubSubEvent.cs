using System;
using System.Collections.Generic;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Networking;

namespace TicketSales.Server.PubSub;

public static class EventTypes
{
    public const string TicketPurchased = "TicketPurchased";
    public const string SeatReserved = "SeatReserved";
    public const string TicketCancelled = "TicketCancelled";

    public static readonly string[] All = { TicketPurchased, SeatReserved, TicketCancelled };

    public static bool IsKnown(string? type) => Array.IndexOf(All, type) >= 0;
}

public class PubSubEvent
{
    public PubSubEvent(string type, int entityId, Dictionary<string, string>? data = null)
    {
        Type = type;
        EntityId = entityId;
        Data = data ?? new();
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string Type { get; }
    public int EntityId { get; }
    public DateTime Timestamp { get; } = DateTime.Now;
    public string ClientId { get; init; } = RequestContext.Current?.ClientId ?? "Server";
    public Dictionary<string, string> Data { get; }

    public void Validate()
    {
        if (!EventTypes.IsKnown(Type))
        {
            throw new ValidationException($"Неизвестный тип события: '{Type}'");
        }

        if (EntityId <= 0)
        {
            throw new ValidationException($"Некорректное событие {Type}: EntityId = {EntityId}");
        }
    }

    public override string ToString() => $"{Type}(EntityId={EntityId}, {ClientId})";
}
