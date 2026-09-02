using System;
using System.Linq;
using TicketSales.Server.Storage;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.InMemory;

public class InMemoryCustomerRepository : InMemoryRepository<Customer>, ICustomerRepository
{
    public InMemoryCustomerRepository() : base(c => c.Id, (c, id) => c.Id = id)
    {
    }

    public Customer? FindByEmail(string email)
    {
        return Storage.Values.FirstOrDefault(c => c.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }
}
