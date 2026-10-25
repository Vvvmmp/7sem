using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Cluster;

/// <summary>
/// Канал обмена сообщениями между узлами внутри процесса сервера.
/// Узел FAILED не отвечает: отправитель ждёт Timeout и получает NO RESPONSE.
/// </summary>
public class ClusterNetwork
{
    private readonly ConcurrentDictionary<int, ClusterNode> _nodes = new();

    public ClusterNetwork(TimeSpan timeout)
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }

    public void Register(ClusterNode node)
    {
        if (!_nodes.TryAdd(node.Id, node))
        {
            throw new ArgumentException($"Node {node.Id} уже существует: ID узла должен быть уникальным");
        }
    }

    public async Task<ClusterMessage> SendAsync(ClusterMessage message)
    {
        if (!_nodes.TryGetValue(message.To, out var target))
        {
            throw new NodeUnreachableException(message.To, "UNREACHABLE (узел не найден)");
        }

        if (target.Status == NodeStatus.Failed)
        {
            await Task.Delay(Timeout);
            throw new NodeUnreachableException(message.To, "NO RESPONSE");
        }

        // Асинхронная доставка: получатель обрабатывает сообщение не в потоке отправителя.
        await Task.Yield();
        return target.Receive(message);
    }
}
