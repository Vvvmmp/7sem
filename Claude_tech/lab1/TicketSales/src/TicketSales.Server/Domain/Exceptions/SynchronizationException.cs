using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Domain.Exceptions;

public class SynchronizationException : DomainException
{
    public SynchronizationException(string message) : base(ErrorCodes.SynchronizationError, message)
    {
    }
}
