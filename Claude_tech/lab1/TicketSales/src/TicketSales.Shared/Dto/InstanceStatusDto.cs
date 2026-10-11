using TicketSales.Shared.Models;

namespace TicketSales.Shared.Dto;

public class InstanceStatusDto
{
    public string Name { get; set; } = string.Empty;
    public InstanceState State { get; set; }
    public bool SimulatedFailure { get; set; }
    public bool NeedsSync { get; set; }
    public SeatStatus? SeatStatus { get; set; }
    public int? FreeSeats { get; set; }
    public int? TicketCount { get; set; }
}
