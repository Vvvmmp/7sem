using System;
using TicketSales.Client;
using Xunit;

namespace TicketSales.Tests;

public class TimeSynchronizerTests
{
    [Fact]
    public void CalculateOffset_ClockAheadBySevenSeconds_ReturnsNegativeOffset()
    {
        var trueSendTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var clientClockOffset = TimeSpan.FromSeconds(7);
        var requestSentAtClientClock = trueSendTime + clientClockOffset;
        var serverTimeUtc = trueSendTime;
        var rtt = TimeSpan.Zero;

        var offset = TimeSynchronizer.CalculateOffset(serverTimeUtc, requestSentAtClientClock, rtt);

        Assert.Equal(TimeSpan.FromSeconds(-7), offset);
    }

    [Fact]
    public void CalculateOffset_WithRtt_UsesMidpointOfRoundTrip()
    {
        var requestSentAtClientClock = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var serverTimeUtc = new DateTime(2026, 1, 1, 10, 0, 1, DateTimeKind.Utc);
        var rtt = TimeSpan.FromSeconds(2);

        var offset = TimeSynchronizer.CalculateOffset(serverTimeUtc, requestSentAtClientClock, rtt);

        Assert.Equal(TimeSpan.Zero, offset);
    }

    [Fact]
    public void ApplyOffset_AddsOffsetToLocalClock()
    {
        var localNow = new DateTime(2026, 1, 1, 10, 0, 7, DateTimeKind.Utc);
        var offset = TimeSpan.FromSeconds(-7);

        var corrected = TimeSynchronizer.ApplyOffset(localNow, offset);

        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), corrected);
    }
}
