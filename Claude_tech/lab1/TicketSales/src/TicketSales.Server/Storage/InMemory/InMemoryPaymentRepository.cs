using TicketSales.Server.Storage;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.InMemory;

public class InMemoryPaymentRepository : InMemoryRepository<Payment>, IPaymentRepository
{
    public InMemoryPaymentRepository() : base(p => p.Id, (p, id) => p.Id = id)
    {
    }
}
