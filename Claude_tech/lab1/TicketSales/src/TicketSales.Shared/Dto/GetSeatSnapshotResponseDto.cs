using TicketSales.Shared.Models;

namespace TicketSales.Shared.Dto;

public class GetSeatSnapshotResponseDto
{
    public SeatStatus Status { get; set; }
    public int TicketCount { get; set; }
}
