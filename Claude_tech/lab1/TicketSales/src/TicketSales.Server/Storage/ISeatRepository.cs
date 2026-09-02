using System.Collections.Generic;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage;

public interface ISeatRepository : IRepository<Seat>
{
    IReadOnlyList<Seat> GetByEvent(int eventId);
}
