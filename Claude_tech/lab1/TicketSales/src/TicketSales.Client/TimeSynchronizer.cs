using System;

namespace TicketSales.Client;

public static class TimeSynchronizer
{
    public static TimeSpan CalculateOffset(DateTime serverTimeUtc, DateTime requestSentAtClientClock, TimeSpan rtt)
    {
        var estimatedTrueTimeAtRequest = requestSentAtClientClock + TimeSpan.FromTicks(rtt.Ticks / 2);
        return serverTimeUtc - estimatedTrueTimeAtRequest;
    }

    public static DateTime ApplyOffset(DateTime clientClockNow, TimeSpan offset)
    {
        return clientClockNow + offset;
    }
}
