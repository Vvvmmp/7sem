using TicketSales.Shared.Models;

namespace TicketSales.Shared.Dto;

public class BuyTicketResponseDto
{
    public int TicketId { get; set; }
    public int PaymentId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
}
