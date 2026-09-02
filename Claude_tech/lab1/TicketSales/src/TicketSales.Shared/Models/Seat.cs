namespace TicketSales.Shared.Models;

public class Seat
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public string Row { get; set; } = string.Empty;
    public int Number { get; set; }
    public SeatStatus Status { get; set; }
}
