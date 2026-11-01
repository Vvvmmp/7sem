namespace TicketSales.Shared.Dto;

public class SetInstanceAvailabilityRequestDto
{
    public string Instance { get; set; } = string.Empty;

    public bool Available { get; set; }
}
