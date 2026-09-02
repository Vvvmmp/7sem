using System;

namespace TicketSales.Shared.Models;

public class Payment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
