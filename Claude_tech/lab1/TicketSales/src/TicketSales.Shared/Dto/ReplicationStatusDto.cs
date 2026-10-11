using System.Collections.Generic;

namespace TicketSales.Shared.Dto;

public class ReplicationStatusDto
{
    public InstanceStatusDto Primary { get; set; } = new();
    public InstanceStatusDto Replica { get; set; } = new();

    /// <summary>Экземпляр, который будет обслуживать следующий запрос; null — нет доступного экземпляра.</summary>
    public string? ActiveInstance { get; set; }

    /// <summary>null — сравнение невозможно (один из экземпляров недоступен).</summary>
    public bool? Consistent { get; set; }

    public List<string> Differences { get; set; } = new();
}
