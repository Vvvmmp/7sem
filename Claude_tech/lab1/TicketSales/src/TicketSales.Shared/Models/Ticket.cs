using System;

namespace TicketSales.Shared.Models;

public class Ticket
{
    public int Id { get; set; }
    public int EventId { get; set; }
    public int SeatId { get; set; }
    public int CustomerId { get; set; }
    public TicketStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
