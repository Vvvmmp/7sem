using System.Collections.Generic;

namespace TicketSales.Shared.Dto;

public class SynchronizeInstancesResponseDto
{
    public string? Source { get; set; }
    public string? Target { get; set; }
    public List<string> DifferencesBefore { get; set; } = new();
    public bool ConsistentAfter { get; set; }
}
