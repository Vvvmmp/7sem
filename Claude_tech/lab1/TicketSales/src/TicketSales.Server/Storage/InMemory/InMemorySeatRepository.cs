using System.Collections.Generic;
using System.Linq;
using TicketSales.Server.Storage;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.InMemory;

public class InMemorySeatRepository : InMemoryRepository<Seat>, ISeatRepository
{
    public InMemorySeatRepository() : base(s => s.Id, (s, id) => s.Id = id)
    {
    }

    public IReadOnlyList<Seat> GetByEvent(int eventId)
    {
        return Storage.Values.Where(s => s.EventId == eventId).ToList();
    }
}
