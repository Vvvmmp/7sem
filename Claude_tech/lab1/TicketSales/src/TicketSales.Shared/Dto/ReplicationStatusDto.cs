using System.Collections.Generic;

namespace TicketSales.Shared.Dto;

public class ReplicationStatusDto
{
    public InstanceStatusDto Primary { get; set; } = new();
    public InstanceStatusDto Replica { get; set; } = new();

    public string? ActiveInstance { get; set; }

    public bool? Consistent { get; set; }

    public List<string> Differences { get; set; } = new();
}
