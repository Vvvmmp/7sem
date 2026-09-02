namespace TicketSales.Shared.Models;

public enum SeatStatus
{
    Free,
    Reserved,
    Sold
}

public enum TicketStatus
{
    Reserved,
    Purchased,
    Cancelled
}

public enum PaymentStatus
{
    Completed,
    Failed
}
