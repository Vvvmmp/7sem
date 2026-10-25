using System;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Cluster;

public enum MessageType
{
    Heartbeat,
    Alive,
    Election,
    Coordinator,
    Propose,
    Vote,
    Commit,
    Ack,
    Error
}

/// <summary>Предлагаемое значение предметной области: новый ReservationStatus билета.</summary>
public record Proposal(int Version, int TicketId, TicketStatus Status)
{
    public override string ToString() => $"Version={Version}, Ticket {TicketId}: ReservationStatus={Status}";
}

/// <summary>
/// Сообщение между узлами. Term — номер выборов (эпоха лидера), Version — последняя принятая версия значения.
/// Ответ на Propose — Vote с Agree = YES / NO и причиной в Reason.
/// </summary>
public record ClusterMessage(
    MessageType Type,
    int From,
    int To,
    int Term,
    Proposal? Proposal = null,
    int? LeaderId = null,
    int Version = 0,
    bool Agree = false,
    string? Reason = null);

public class NodeUnreachableException : Exception
{
    public NodeUnreachableException(int nodeId, string reason) : base($"Node {nodeId} → {reason}")
    {
        NodeId = nodeId;
        Reason = reason;
    }

    public int NodeId { get; }
    public string Reason { get; }
}
