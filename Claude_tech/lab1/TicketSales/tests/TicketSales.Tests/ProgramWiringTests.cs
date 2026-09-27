using System;
using System.IO;
using Xunit;

namespace TicketSales.Tests;

public class ProgramWiringTests
{
    private static string ProgramSource => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TicketSales.Server", "Program.cs"));

    [Fact]
    public void Program_WiresTicketSalesServicePostgres_NotThePlainInMemoryStyleService()
    {
        // TicketSalesService's lock protects a Seat snapshot fetched via GetById before the
        // lock is entered. Against PostgresSeatRepository (a fresh row on every call, not a
        // shared in-memory reference) that snapshot is never re-read inside the critical
        // section, so the lock stops protecting against a real double-reservation.
        // TicketSalesServicePostgres re-reads status with a fresh SELECT inside the lock/
        // transaction, so it is the only safe pairing with the Postgres repositories.
        var source = ProgramSource;

        Assert.Contains("new TicketSalesServicePostgres(", source);
        Assert.DoesNotContain("new TicketSalesService(", source);
    }
}
