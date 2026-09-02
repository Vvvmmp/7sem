using System.Collections.Generic;

namespace TicketSales.Shared.Dto;

public class GetEventsResponseDto
{
    public List<EventDto> Events { get; set; } = new();
}
