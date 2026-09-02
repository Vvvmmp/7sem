namespace TicketSales.Shared.Dto;

public class ReserveSeatResponseDto
{
    public int TicketId { get; set; }
    public SeatDto Seat { get; set; } = new();
}
