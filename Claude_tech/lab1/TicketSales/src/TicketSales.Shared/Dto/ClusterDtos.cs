using System.Collections.Generic;
using TicketSales.Shared.Models;

namespace TicketSales.Shared.Dto;

public class GetClusterStatusRequestDto
{
}

public class SetNodeStateRequestDto
{
    public int NodeId { get; set; }
    public bool Available { get; set; } = true;
    public bool VotesNo { get; set; }
}

public class ProposeReservationStatusRequestDto
{
    public int TicketId { get; set; }
    public TicketStatus Status { get; set; }
}

public class ClusterStatusDto
{
    public List<NodeStatusDto> Nodes { get; set; } = new();
    public int? LeaderId { get; set; }
    public int Term { get; set; }
    public int Available { get; set; }
    public int Majority { get; set; }
    public bool ConsensusPossible { get; set; }
    public List<string> LastLog { get; set; } = new();
}

public class NodeStatusDto
{
    public int Id { get; set; }
    public NodeStatus Status { get; set; }
    public NodeRole Role { get; set; }
    public int? LeaderId { get; set; }
    public bool VotesNo { get; set; }
    public int LastAcceptedVersion { get; set; }
    public string? LastCommitted { get; set; }
}

public class ConsensusResultDto
{
    public int LeaderId { get; set; }
    public string Proposal { get; set; } = string.Empty;
    public Dictionary<int, string> Votes { get; set; } = new();
    public int Yes { get; set; }
    public int No { get; set; }
    public int NoResponse { get; set; }
    public int Majority { get; set; }
    public bool Achieved { get; set; }
    public string? Applied { get; set; }
}
