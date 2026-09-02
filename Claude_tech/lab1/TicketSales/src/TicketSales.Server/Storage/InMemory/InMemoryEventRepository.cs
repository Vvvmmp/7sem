using TicketSales.Server.Storage;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.InMemory;

public class InMemoryEventRepository : InMemoryRepository<Event>, IEventRepository
{
    public InMemoryEventRepository() : base(e => e.Id, (e, id) => e.Id = id)
    {
    }
}
