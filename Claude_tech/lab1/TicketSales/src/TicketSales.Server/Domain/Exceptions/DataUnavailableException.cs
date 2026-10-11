using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Domain.Exceptions;

public class DataUnavailableException : DomainException
{
    public DataUnavailableException(string message) : base(ErrorCodes.DataUnavailable, message)
    {
    }
}
