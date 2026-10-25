namespace TicketSales.Shared.Protocol;

public static class ErrorCodes
{
    public const string UnknownOperation = "UNKNOWN_OPERATION";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";
    public const string DataUnavailable = "DATA_UNAVAILABLE";
    public const string SynchronizationError = "SYNCHRONIZATION_ERROR";
    public const string ConsensusNotAchieved = "CONSENSUS_NOT_ACHIEVED";
}
