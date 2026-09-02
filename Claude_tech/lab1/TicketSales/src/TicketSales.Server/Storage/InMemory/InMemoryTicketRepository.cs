using TicketSales.Server.Storage;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.InMemory;

public class InMemoryTicketRepository : InMemoryRepository<Ticket>, ITicketRepository
{
    public InMemoryTicketRepository() : base(t => t.Id, (t, id) => t.Id = id)
    {
    }
}
