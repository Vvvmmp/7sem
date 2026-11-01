using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Cluster;

public class ClusterNode
{
    private readonly object _lock = new();
    private readonly ClusterNetwork _network;
    private readonly ClusterLog _log;
    private readonly Dictionary<int, NodeStatus> _knownPeers;
    private readonly List<Proposal> _committed = new();
    private int _receivedMessages;

    public ClusterNode(int id, IEnumerable<int> clusterIds, ClusterNetwork network, ClusterLog log)
    {
        if (id <= 0)
        {
            throw new ArgumentException("ID узла должен быть положительным");
        }

        Id = id;
        Peers = clusterIds.Where(p => p != id).OrderBy(p => p).ToArray();
        _knownPeers = Peers.ToDictionary(p => p, _ => NodeStatus.Available);
        _network = network;
        _log = log;
    }

    public int Id { get; }
    public int[] Peers { get; }
    public NodeStatus Status { get; private set; } = NodeStatus.Available;
    public NodeRole Role { get; private set; } = NodeRole.Follower;
    public int? LeaderId { get; private set; }
    public int Term { get; private set; }
    public int LastAcceptedVersion { get; private set; }

    public bool VotesNo { get; set; }

    public int ReceivedMessages => Volatile.Read(ref _receivedMessages);

    public Proposal? LastCommitted
    {
        get { lock (_lock) return _committed.LastOrDefault(); }
    }

    public IReadOnlyList<Proposal> Committed
    {
        get { lock (_lock) return _committed.ToList(); }
    }

    public NodeStatus KnownStatusOf(int peerId)
    {
        lock (_lock) return _knownPeers[peerId];
    }

    public void Fail()
    {
        lock (_lock)
        {
            Status = NodeStatus.Failed;
        }
    }

    public void Recover()
    {
        lock (_lock)
        {
            Status = NodeStatus.Available;
            Role = NodeRole.Follower;
            LeaderId = null;
        }
    }

    public Task<ClusterMessage> SendAsync(MessageType type, int to, int? term = null, Proposal? proposal = null, int? leaderId = null, int version = 0) =>
        _network.SendAsync(new ClusterMessage(type, Id, to, term ?? Term, proposal, leaderId, version));

    public async Task<bool> SendHeartbeatAsync(int to, bool verbose)
    {
        if (verbose)
        {
            _log.Write($"Node {Id} → HEARTBEAT → Node {to}");
        }

        try
        {
            var reply = await SendAsync(MessageType.Heartbeat, to);
            var alive = reply.Type == MessageType.Alive;
            if (verbose)
            {
                _log.Write(alive ? $"Node {to} → ALIVE" : $"Node {to} → ERROR ({reply.Reason})");
            }

            MarkPeer(to, alive ? NodeStatus.Available : NodeStatus.Failed);
            return alive;
        }
        catch (NodeUnreachableException ex)
        {
            if (verbose)
            {
                _log.Write($"Node {to} → {ex.Reason}");
            }

            MarkPeer(to, NodeStatus.Failed);
            return false;
        }
    }

    public void MarkPeer(int peerId, NodeStatus status)
    {
        lock (_lock)
        {
            if (_knownPeers.ContainsKey(peerId))
            {
                _knownPeers[peerId] = status;
            }
        }
    }

    public bool ApplyCoordinator(int term, int leaderId, int version)
    {
        lock (_lock)
        {
            if (Status == NodeStatus.Failed || term < Term)
            {
                return false;
            }

            Term = term;
            LeaderId = leaderId;
            Role = leaderId == Id ? NodeRole.Leader : NodeRole.Follower;
            LastAcceptedVersion = Math.Max(LastAcceptedVersion, version);
            return true;
        }
    }

    public (bool Agree, string? Reason) Vote(Proposal proposal)
    {
        lock (_lock)
        {
            if (Status == NodeStatus.Failed)
            {
                throw new InvalidOperationException($"Node {Id} FAILED и не может голосовать");
            }

            string? reason = null;
            if (VotesNo)
            {
                reason = "узел не согласен";
            }
            else if (proposal.Version <= LastAcceptedVersion)
            {
                reason = $"версия {proposal.Version} не новее принятой {LastAcceptedVersion}";
            }
            else if (proposal.Status is not (TicketStatus.Purchased or TicketStatus.Cancelled))
            {
                reason = $"недопустимый ReservationStatus {proposal.Status}";
            }

            if (reason is null)
            {
                LastAcceptedVersion = proposal.Version;
            }

            return (reason is null, reason);
        }
    }

    public void Commit(Proposal proposal)
    {
        lock (_lock)
        {
            _committed.Add(proposal);
            LastAcceptedVersion = Math.Max(LastAcceptedVersion, proposal.Version);
        }
    }

    public ClusterMessage Receive(ClusterMessage message)
    {
        Interlocked.Increment(ref _receivedMessages);

        if (Status == NodeStatus.Failed)
        {
            return Error(message, "узел FAILED и не обрабатывает сообщения");
        }

        if (message.To != Id)
        {
            return Error(message, $"сообщение адресовано Node {message.To}");
        }

        if (!Peers.Contains(message.From))
        {
            return Error(message, $"неизвестный отправитель Node {message.From}");
        }

        MarkPeer(message.From, NodeStatus.Available);

        switch (message.Type)
        {
            case MessageType.Heartbeat:
            case MessageType.Election:
                return Reply(message, MessageType.Alive) with { Version = LastAcceptedVersion };

            case MessageType.Coordinator when message.LeaderId is { } leaderId:
                return ApplyCoordinator(message.Term, leaderId, message.Version)
                    ? Reply(message, MessageType.Ack)
                    : Error(message, $"устаревший Term {message.Term} < {Term}");

            case MessageType.Propose when message.Proposal is { } proposal:
                if (message.From != LeaderId || message.Term < Term)
                {
                    return Error(message, $"PROPOSE не от текущего лидера (лидер Node {LeaderId?.ToString() ?? "?"}, Term {Term})");
                }

                var (agree, reason) = Vote(proposal);
                return Reply(message, MessageType.Vote) with { Agree = agree, Reason = reason };

            case MessageType.Commit when message.Proposal is { } proposal:
                if (message.From != LeaderId)
                {
                    return Error(message, "COMMIT не от текущего лидера");
                }

                Commit(proposal);
                return Reply(message, MessageType.Ack);

            default:
                return Error(message, $"некорректное сообщение {message.Type}");
        }
    }

    private ClusterMessage Reply(ClusterMessage request, MessageType type) =>
        new(type, Id, request.From, Term);

    private ClusterMessage Error(ClusterMessage request, string reason)
    {
        _log.Write($"Node {Id} → ERROR: {request.Type} от Node {request.From} отклонено ({reason})");
        return new ClusterMessage(MessageType.Error, Id, request.From, Term, Reason: reason);
    }
}
