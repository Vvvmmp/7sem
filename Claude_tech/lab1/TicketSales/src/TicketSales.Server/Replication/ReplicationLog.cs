using System;
using TicketSales.Server.Networking;

namespace TicketSales.Server.Replication;

/// <summary>Журнал операций/репликации: [время] [клиент] событие.</summary>
public static class ReplicationLog
{
    public static void Write(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var client = RequestContext.Current?.ClientId ?? "Server";
        Console.WriteLine($"[{timestamp}] [{client}] {message}");
    }
}
