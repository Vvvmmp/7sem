using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Domain.Exceptions;

public class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(ErrorCodes.NotFound, message)
    {
    }
}
