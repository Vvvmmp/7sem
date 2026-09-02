using System;

namespace TicketSales.Shared.Dto;

public class EventDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public string Venue { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public int FreeSeats { get; set; }
}
