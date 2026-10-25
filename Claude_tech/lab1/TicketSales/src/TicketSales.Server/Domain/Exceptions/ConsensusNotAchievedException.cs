using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Domain.Exceptions;

public class ConsensusNotAchievedException : DomainException
{
    public ConsensusNotAchievedException(string message) : base(ErrorCodes.ConsensusNotAchieved, message)
    {
    }
}
