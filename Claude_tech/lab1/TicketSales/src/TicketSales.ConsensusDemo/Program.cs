using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TicketSales.Server.Cluster;
using TicketSales.Shared.Models;

Console.OutputEncoding = Encoding.UTF8;

var cluster = NodeCluster.Create(heartbeatTimeout: TimeSpan.FromMilliseconds(500));
var experiments = new System.Collections.Generic.List<(string Name, int Available, int Yes, bool Achieved)>();

Header("Часть 1–2. Пять узлов и выбор лидера");
cluster.LogNodeStates();
await cluster.ElectAsync();

Header("Эксперимент 1. Все узлы доступны (5/5)");
await Propose("Эксперимент 1", 1, TicketStatus.Purchased);

Header("Часть 3–4. Отказ лидера Node 5 → heartbeat → повторный выбор");
await cluster.SetNodeStateAsync(5, available: false, votesNo: false);
await cluster.DetectFailuresAsync();

Header("Эксперимент 2. Отказ одного узла (4/5)");
await Propose("Эксперимент 2", 2, TicketStatus.Purchased);

Header("Часть 5. Голосование с отрицательным голосом: Node 3 голосует NO");
await cluster.SetNodeStateAsync(3, available: true, votesNo: true);
await Propose("Голос NO", 3, TicketStatus.Cancelled);
await cluster.SetNodeStateAsync(3, available: true, votesNo: false);

Header("Эксперимент 3. Отказ двух узлов: Node 4, Node 5 (3/5)");
await cluster.SetNodeStateAsync(4, available: false, votesNo: false);
await cluster.DetectFailuresAsync();
await Propose("Эксперимент 3", 4, TicketStatus.Purchased);

Header("Эксперимент 4. Отказ трёх узлов: Node 3, Node 4, Node 5 (2/5)");
await cluster.SetNodeStateAsync(3, available: false, votesNo: false);
await cluster.DetectFailuresAsync();
await Propose("Эксперимент 4", 5, TicketStatus.Purchased);

Header("Обработка ошибок: ошибочные сообщения и голос недоступного узла");
var node1 = cluster.GetNode(1);
node1.Receive(new ClusterMessage(MessageType.Propose, 9, 1, 99, new Proposal(100, 1, TicketStatus.Purchased)));
node1.Receive(new ClusterMessage(MessageType.Coordinator, 2, 1, 0, LeaderId: 2));
node1.Receive(new ClusterMessage(MessageType.Vote, 2, 1, node1.Term));
cluster.GetNode(5).Receive(new ClusterMessage(MessageType.Propose, 2, 5, node1.Term, new Proposal(100, 1, TicketStatus.Purchased)));

Header("Восстановление Node 3, 4, 5 и одновременный запуск выборов");
await cluster.SetNodeStateAsync(3, available: true, votesNo: false);
await cluster.SetNodeStateAsync(4, available: true, votesNo: false);
await cluster.SetNodeStateAsync(5, available: true, votesNo: false);
await Task.WhenAll(cluster.ElectAsync(1), cluster.ElectAsync(2), cluster.ElectAsync(3));
Console.WriteLine($"Лидеров среди доступных узлов: {cluster.AvailableNodes.Count(n => n.Role == NodeRole.Leader)}");
await Propose("После восстановления", 6, TicketStatus.Cancelled);

Header("Итог экспериментов");
Console.WriteLine("| Эксперимент | Доступно | YES | Majority | Результат |");
Console.WriteLine("| --- | --- | --- | --- | --- |");
foreach (var e in experiments)
{
    Console.WriteLine($"| {e.Name} | {e.Available}/{cluster.Nodes.Count} | {e.Yes} | {cluster.Majority} | {(e.Achieved ? "CONSENSUS ACHIEVED" : "CONSENSUS NOT ACHIEVED")} |");
}

async Task Propose(string name, int ticketId, TicketStatus status)
{
    var result = await cluster.ProposeAsync(ticketId, status);
    experiments.Add((name, cluster.AvailableNodes.Count, result.Yes, result.Achieved));
}

static void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine($"===== {title} =====");
}
