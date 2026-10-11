namespace TicketSales.Shared.Dto;

public class GetReplicationStatusRequestDto
{
    /// <summary>Место, состояние которого нужно показать на обоих экземплярах (необязательно).</summary>
    public int? SeatId { get; set; }
}
