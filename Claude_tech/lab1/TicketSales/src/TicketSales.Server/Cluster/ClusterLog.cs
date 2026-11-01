using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TicketSales.Server.Replication;

namespace TicketSales.Server.Cluster;

public class ClusterLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    public bool WriteToConsole { get; init; } = true;

    public IReadOnlyList<string> Entries => _entries.ToList();

    public void Write(string message)
    {
        _entries.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        if (WriteToConsole)
        {
            ReplicationLog.Write(message);
        }
    }

    public bool Contains(string text) => _entries.Any(e => e.Contains(text, StringComparison.Ordinal));
}
