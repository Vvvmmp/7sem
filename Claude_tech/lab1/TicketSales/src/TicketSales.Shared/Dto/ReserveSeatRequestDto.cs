namespace TicketSales.Shared.Dto;

public class ReserveSeatRequestDto
{
    public int EventId { get; set; }
    public int SeatId { get; set; }
    public string CustomerFullName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public bool UseTransaction { get; set; } = true;
    public bool SimulateErrorAfterTicketInsert { get; set; } = false;
}
