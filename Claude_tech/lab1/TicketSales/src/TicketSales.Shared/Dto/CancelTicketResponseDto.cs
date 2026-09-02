using TicketSales.Shared.Models;

namespace TicketSales.Shared.Dto;

public class CancelTicketResponseDto
{
    public int TicketId { get; set; }
    public TicketStatus Status { get; set; }
}
