using TicketSales.Shared.Models;

namespace TicketSales.Shared.Dto;

public class SeatDto
{
    public int Id { get; set; }
    public string Row { get; set; } = string.Empty;
    public int Number { get; set; }
    public SeatStatus Status { get; set; }
}
