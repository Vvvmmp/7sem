using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Cluster;

/// <summary>
/// Кластер узлов: выбор лидера (доступный узел с максимальным ID), обнаружение отказа через heartbeat,
/// повторные выборы, предложение значения лидером и принятие решения большинством (Majority = N / 2 + 1).
/// </summary>
public class NodeCluster
{
    public const int DefaultSize = 5;

    private readonly SemaphoreSlim _electionGate = new(1, 1);
    private readonly SemaphoreSlim _detectGate = new(1, 1);
    private readonly SemaphoreSlim _proposalGate = new(1, 1);

    public NodeCluster(IReadOnlyCollection<int> ids, TimeSpan heartbeatTimeout, ClusterLog? log = null)
    {
        if (ids.Count == 0)
        {
            throw new ArgumentException("Кластер должен содержать хотя бы один узел");
        }

        Log = log ?? new ClusterLog();
        Network = new ClusterNetwork(heartbeatTimeout);
        Nodes = ids.OrderBy(id => id).Select(id => new ClusterNode(id, ids, Network, Log)).ToList();
        foreach (var node in Nodes)
        {
            Network.Register(node);
        }
    }

    public static NodeCluster Create(int size = DefaultSize, TimeSpan? heartbeatTimeout = null, bool writeToConsole = true) =>
        new(Enumerable.Range(1, size).ToArray(), heartbeatTimeout ?? TimeSpan.FromSeconds(1), new ClusterLog { WriteToConsole = writeToConsole });

    public IReadOnlyList<ClusterNode> Nodes { get; }
    public ClusterNetwork Network { get; }
    public ClusterLog Log { get; }

    public int Majority => Nodes.Count / 2 + 1;

    public IReadOnlyList<ClusterNode> AvailableNodes => Nodes.Where(n => n.Status == NodeStatus.Available).ToList();

    public bool ConsensusPossible => AvailableNodes.Count >= Majority;

    /// <summary>Лидер среди доступных узлов. Отказавший лидер сюда не попадает, пока его не обнаружит heartbeat.</summary>
    public ClusterNode? Leader => AvailableNodes.FirstOrDefault(n => n.Role == NodeRole.Leader);

    public ClusterNode GetNode(int id) =>
        Nodes.FirstOrDefault(n => n.Id == id) ?? throw new NotFoundException($"Node {id} не существует");

    public void LogNodeStates()
    {
        foreach (var node in Nodes)
        {
            Log.Write($"Node {node.Id} → {Describe(node)}");
        }
    }

    public async Task<ClusterNode?> ElectAsync(int? initiatorId = null, string reason = "Election started")
    {
        await _electionGate.WaitAsync();
        try
        {
            var initiator = initiatorId is { } id ? GetNode(id) : AvailableNodes.FirstOrDefault();
            if (initiator is null || initiator.Status == NodeStatus.Failed)
            {
                Log.Write(initiator is null
                    ? "Election impossible: нет доступных узлов"
                    : $"Election impossible: Node {initiator.Id} FAILED и не может начать выборы");
                return null;
            }

            Log.Write($"{reason} (инициатор Node {initiator.Id})");

            var replies = await Task.WhenAll(initiator.Peers.Select(async peer =>
            {
                try
                {
                    var reply = await initiator.SendAsync(MessageType.Election, peer);
                    return (Peer: peer, Reply: reply.Type == MessageType.Alive ? reply : null);
                }
                catch (NodeUnreachableException)
                {
                    return (Peer: peer, Reply: (ClusterMessage?)null);
                }
            }));

            var candidates = new List<int>();
            foreach (var (peer, reply) in replies.Append((initiator.Id, null)).OrderBy(r => r.Item1))
            {
                if (peer == initiator.Id || reply is not null)
                {
                    candidates.Add(peer);
                    Log.Write($"Node {peer} AVAILABLE");
                }
                else
                {
                    initiator.MarkPeer(peer, NodeStatus.Failed);
                    Log.Write($"Node {peer} excluded (FAILED, нет ответа)");
                }
            }

            var leaderId = candidates.Max();
            var term = replies.Where(r => r.Reply is not null).Select(r => r.Reply!.Term).Append(initiator.Term).Max() + 1;
            var version = replies.Where(r => r.Reply is not null).Select(r => r.Reply!.Version).Append(initiator.LastAcceptedVersion).Max();

            initiator.ApplyCoordinator(term, leaderId, version);
            await Task.WhenAll(replies.Where(r => r.Reply is not null).Select(async r =>
            {
                try
                {
                    await initiator.SendAsync(MessageType.Coordinator, r.Peer, term, leaderId: leaderId, version: version);
                }
                catch (NodeUnreachableException ex)
                {
                    Log.Write($"COORDINATOR не доставлен: {ex.Message}");
                }
            }));

            Log.Write($"Node {leaderId} elected as LEADER (Term {term})");
            LogNodeStates();

            var available = AvailableNodes.Count;
            if (available < Majority)
            {
                Log.Write($"Доступно {available}/{Nodes.Count} < Majority {Majority}: лидер выбран, но консенсус невозможен");
            }

            Log.Write($"Проверка: LEADER среди доступных узлов = {AvailableNodes.Count(n => n.Role == NodeRole.Leader)}");
            return GetNode(leaderId);
        }
        finally
        {
            _electionGate.Release();
        }
    }

    /// <summary>
    /// Failure detector: follower с наименьшим ID шлёт HEARTBEAT лидеру; нет ответа → лидер FAILED → новые выборы.
    /// Затем лидер проверяет followers. verbose = false — в журнал попадают только изменения состояния.
    /// </summary>
    public async Task<bool> DetectFailuresAsync(bool verbose = true)
    {
        await _detectGate.WaitAsync();
        try
        {
            var observer = AvailableNodes.Where(n => n.Role == NodeRole.Follower).FirstOrDefault() ?? AvailableNodes.FirstOrDefault();
            if (observer is null)
            {
                Log.Write("Heartbeat: нет доступных узлов");
                return false;
            }

            if (observer.LeaderId is not { } leaderId)
            {
                await ElectAsync(observer.Id, $"Node {observer.Id}: лидер неизвестен → Election started");
                return true;
            }

            if (leaderId != observer.Id && !await observer.SendHeartbeatAsync(leaderId, verbose))
            {
                Log.Write($"Node {leaderId} → HEARTBEAT TIMEOUT");
                Log.Write($"Node {leaderId} → FAILED (LEADER недоступен)");
                await ElectAsync(observer.Id, "New election started");
                return true;
            }

            var leader = GetNode(leaderId);
            await Task.WhenAll(leader.Peers.Select(async peer =>
            {
                var before = leader.KnownStatusOf(peer);
                var alive = await leader.SendHeartbeatAsync(peer, verbose);
                if (before == NodeStatus.Available && !alive)
                {
                    Log.Write($"Node {peer} → HEARTBEAT TIMEOUT");
                    Log.Write($"Node {peer} → FAILED");
                }
                else if (before == NodeStatus.Failed && alive)
                {
                    Log.Write($"Node {peer} → AVAILABLE (снова отвечает на heartbeat)");
                }
            }));

            return false;
        }
        finally
        {
            _detectGate.Release();
        }
    }

    public void StartMonitoring(TimeSpan interval, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken);
                    await DetectFailuresAsync(verbose: false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Write($"Heartbeat monitor → ERROR: {ex.Message}");
                }
            }
        }, cancellationToken);
    }

    /// <summary>Моделирование отказа / восстановления узла и его несогласия при голосовании.</summary>
    public async Task SetNodeStateAsync(int nodeId, bool available, bool votesNo)
    {
        var node = GetNode(nodeId);
        if (node.VotesNo != votesNo)
        {
            node.VotesNo = votesNo;
            Log.Write($"Node {nodeId} → голосование: {(votesNo ? "всегда NO" : "по правилам")}");
        }

        if (!available && node.Status == NodeStatus.Available)
        {
            var role = node.Role;
            node.Fail();
            Log.Write($"Node {nodeId} → FAILED (смоделирован отказ, роль была {role.ToString().ToUpperInvariant()})");
        }
        else if (available && node.Status == NodeStatus.Failed)
        {
            node.Recover();
            Log.Write($"Node {nodeId} → AVAILABLE (восстановлен, FOLLOWER)");
            await ElectAsync(nodeId, $"New election started: Node {nodeId} вернулся в кластер");
        }
    }

    /// <summary>
    /// Лидер предлагает ReservationStatus билета, рассылает PROPOSE остальным узлам, собирает голоса
    /// и объявляет CONSENSUS ACHIEVED только при YES ≥ Majority. Без большинства значение не применяется.
    /// </summary>
    public async Task<ConsensusResultDto> ProposeAsync(int ticketId, TicketStatus status)
    {
        if (ticketId <= 0)
        {
            throw new ValidationException("Id билета должен быть положительным");
        }

        await _proposalGate.WaitAsync();
        try
        {
            var leader = await EnsureLeaderAsync();
            var result = new ConsensusResultDto { Majority = Majority };
            if (leader is null)
            {
                Log.Write("CONSENSUS NOT ACHIEVED: нет доступного лидера");
                return result;
            }

            var proposal = new Proposal(leader.LastAcceptedVersion + 1, ticketId, status);
            result.LeaderId = leader.Id;
            result.Proposal = proposal.ToString();
            Log.Write($"Leader Node {leader.Id} → PROPOSE {proposal}");

            var self = leader.Vote(proposal);
            var replies = await Task.WhenAll(leader.Peers.Select(async peer =>
            {
                Log.Write($"Leader Node {leader.Id} → Node {peer}: PROPOSE");
                try
                {
                    var reply = await leader.SendAsync(MessageType.Propose, peer, proposal: proposal);
                    return reply.Type == MessageType.Vote
                        ? (Peer: peer, Vote: reply.Agree ? "YES" : "NO", reply.Reason)
                        : (Peer: peer, Vote: "INVALID", reply.Reason);
                }
                catch (NodeUnreachableException ex)
                {
                    return (Peer: peer, Vote: ex.Reason, Reason: (string?)null);
                }
            }));

            foreach (var (peer, vote, reason) in replies.Append((leader.Id, self.Agree ? "YES" : "NO", self.Reason)).OrderBy(r => r.Item1))
            {
                result.Votes[peer] = vote;
                Log.Write($"Node {peer} → {vote}{(reason is null ? string.Empty : $" ({reason})")}");
            }

            result.Yes = result.Votes.Values.Count(v => v == "YES");
            result.No = result.Votes.Values.Count(v => v == "NO");
            result.NoResponse = result.Votes.Count - result.Yes - result.No;
            result.Achieved = result.Yes >= Majority;

            Log.Write($"YES = {result.Yes}, NO = {result.No}, NO RESPONSE = {result.NoResponse}");
            Log.Write($"Majority = {Majority} (из {Nodes.Count})");

            if (!result.Achieved)
            {
                Log.Write($"CONSENSUS NOT ACHIEVED: {proposal} не принято");
                return result;
            }

            leader.Commit(proposal);
            await Task.WhenAll(replies.Where(r => r.Vote is "YES" or "NO").Select(r => leader.SendAsync(MessageType.Commit, r.Peer, proposal: proposal)));
            Log.Write($"CONSENSUS ACHIEVED: {proposal} принято");
            return result;
        }
        finally
        {
            _proposalGate.Release();
        }
    }

    public ClusterStatusDto GetStatus()
    {
        var leader = Leader;
        return new ClusterStatusDto
        {
            Nodes = Nodes.Select(n => new NodeStatusDto
            {
                Id = n.Id,
                Status = n.Status,
                Role = n.Role,
                LeaderId = n.LeaderId,
                VotesNo = n.VotesNo,
                LastAcceptedVersion = n.LastAcceptedVersion,
                LastCommitted = n.LastCommitted?.ToString()
            }).ToList(),
            LeaderId = leader?.Id,
            Term = leader?.Term ?? Nodes.Max(n => n.Term),
            Available = AvailableNodes.Count,
            Majority = Majority,
            ConsensusPossible = ConsensusPossible,
            LastLog = Log.Entries.TakeLast(20).ToList()
        };
    }

    private async Task<ClusterNode?> EnsureLeaderAsync()
    {
        if (Leader is { } leader)
        {
            return leader;
        }

        await DetectFailuresAsync();
        return Leader ?? await ElectAsync();
    }

    private static string Describe(ClusterNode node) =>
        node.Status == NodeStatus.Failed ? "FAILED" : $"AVAILABLE, {node.Role.ToString().ToUpperInvariant()}";
}
