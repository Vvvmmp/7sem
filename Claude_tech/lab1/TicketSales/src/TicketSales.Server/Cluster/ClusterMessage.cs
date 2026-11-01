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

public record Proposal(int Version, int TicketId, TicketStatus Status)
{
    public override string ToString() => $"Version={Version}, Ticket {TicketId}: ReservationStatus={Status}";
}

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
