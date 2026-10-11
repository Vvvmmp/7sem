namespace TicketSales.Shared.Dto;

public class SetInstanceAvailabilityRequestDto
{
    /// <summary>"Primary" или "Replica".</summary>
    public string Instance { get; set; } = string.Empty;

    /// <summary>false — смоделировать отказ, true — восстановить экземпляр.</summary>
    public bool Available { get; set; }
}
