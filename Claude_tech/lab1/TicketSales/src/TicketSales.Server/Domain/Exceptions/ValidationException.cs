using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Domain.Exceptions;

public class ValidationException : DomainException
{
    public ValidationException(string message) : base(ErrorCodes.ValidationError, message)
    {
    }
}
