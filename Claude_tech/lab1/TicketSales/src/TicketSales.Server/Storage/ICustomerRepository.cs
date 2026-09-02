using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage;

public interface ICustomerRepository : IRepository<Customer>
{
    Customer? FindByEmail(string email);
}
