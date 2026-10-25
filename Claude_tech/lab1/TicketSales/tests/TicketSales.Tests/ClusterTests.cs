using System;
using System.Linq;
using System.Threading.Tasks;
using TicketSales.Server.Cluster;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Shared.Models;
using Xunit;

namespace TicketSales.Tests;

public class ClusterTests
{
    private readonly NodeCluster _cluster = NodeCluster.Create(heartbeatTimeout: TimeSpan.FromMilliseconds(20), writeToConsole: false);

    private async Task FailAndDetect(params int[] ids)
    {
        foreach (var id in ids)
        {
            await _cluster.SetNodeStateAsync(id, available: false, votesNo: false);
        }

        await _cluster.DetectFailuresAsync();
    }

    [Fact]
    public void Create_FiveNodes_AvailableFollowersWithUniqueIds()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, _cluster.Nodes.Select(n => n.Id));
        Assert.All(_cluster.Nodes, n =>
        {
            Assert.Equal(NodeStatus.Available, n.Status);
            Assert.Equal(NodeRole.Follower, n.Role);
            Assert.Equal(4, n.Peers.Length);
            Assert.DoesNotContain(n.Id, n.Peers);
        });
    }

    [Fact]
    public void Create_DuplicateId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new NodeCluster(new[] { 1, 2, 2 }, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task AvailableNodes_ExcludeFailedNodes()
    {
        await _cluster.SetNodeStateAsync(2, available: false, votesNo: false);

        Assert.Equal(new[] { 1, 3, 4, 5 }, _cluster.AvailableNodes.Select(n => n.Id));
        Assert.True(_cluster.ConsensusPossible);
    }

    [Fact]
    public async Task Election_AvailableNodeWithMaxId_BecomesLeader()
    {
        var leader = await _cluster.ElectAsync();

        Assert.Equal(5, leader!.Id);
        Assert.Same(leader, _cluster.Leader);
        Assert.All(_cluster.Nodes, n => Assert.Equal(5, n.LeaderId));
        Assert.True(_cluster.Log.Contains("Node 5 elected as LEADER"));
    }

    [Fact]
    public async Task Election_FailedNode_CannotBeElected()
    {
        await _cluster.SetNodeStateAsync(5, available: false, votesNo: false);

        var leader = await _cluster.ElectAsync();

        Assert.Equal(4, leader!.Id);
        Assert.True(_cluster.Log.Contains("Node 5 excluded"));
    }

    [Fact]
    public async Task Heartbeat_DetectsLeaderFailure()
    {
        await _cluster.ElectAsync();
        await _cluster.SetNodeStateAsync(5, available: false, votesNo: false);

        var leaderFailed = await _cluster.DetectFailuresAsync();

        Assert.True(leaderFailed);
        Assert.True(_cluster.Log.Contains("Node 5 → NO RESPONSE"));
        Assert.True(_cluster.Log.Contains("Node 5 → HEARTBEAT TIMEOUT"));
        Assert.Equal(NodeStatus.Failed, _cluster.GetNode(1).KnownStatusOf(5));
    }

    [Fact]
    public async Task LeaderFailure_NewLeaderIsElected_AndOnlyOneLeaderExists()
    {
        await _cluster.ElectAsync();

        await FailAndDetect(5);

        Assert.Equal(4, _cluster.Leader!.Id);
        Assert.Single(_cluster.AvailableNodes, n => n.Role == NodeRole.Leader);
        Assert.All(_cluster.AvailableNodes, n => Assert.Equal(4, n.LeaderId));
        Assert.True(_cluster.Log.Contains("New election started"));
    }

    [Fact]
    public async Task Heartbeat_AllAlive_LeaderDoesNotChange()
    {
        await _cluster.ElectAsync();

        Assert.False(await _cluster.DetectFailuresAsync());
        Assert.Equal(5, _cluster.Leader!.Id);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(4, 3)]
    [InlineData(3, 2)]
    public void Majority_IsHalfPlusOne(int size, int majority)
    {
        Assert.Equal(majority, NodeCluster.Create(size, writeToConsole: false).Majority);
    }

    [Fact]
    public async Task Vote_AllAgree_PositiveVotesAndConsensus()
    {
        await _cluster.ElectAsync();

        var result = await _cluster.ProposeAsync(1, TicketStatus.Purchased);

        Assert.True(result.Achieved);
        Assert.Equal(5, result.Yes);
        Assert.All(result.Votes.Values, v => Assert.Equal("YES", v));
        Assert.All(_cluster.Nodes, n => Assert.Equal(TicketStatus.Purchased, n.LastCommitted!.Status));
    }

    [Fact]
    public async Task Vote_NodeDisagrees_NegativeVoteIsCounted()
    {
        await _cluster.ElectAsync();
        await _cluster.SetNodeStateAsync(2, available: true, votesNo: true);
        await _cluster.SetNodeStateAsync(3, available: true, votesNo: true);

        var result = await _cluster.ProposeAsync(1, TicketStatus.Cancelled);

        Assert.Equal("NO", result.Votes[2]);
        Assert.Equal(2, result.No);
        Assert.Equal(3, result.Yes);
        Assert.True(result.Achieved);
    }

    [Fact]
    public async Task Vote_InvalidReservationStatus_AllVoteNo_NoConsensus()
    {
        await _cluster.ElectAsync();

        var result = await _cluster.ProposeAsync(1, TicketStatus.Reserved);

        Assert.Equal(5, result.No);
        Assert.False(result.Achieved);
        Assert.All(_cluster.Nodes, n => Assert.Null(n.LastCommitted));
    }

    [Theory]
    [InlineData(new int[0], 5)]
    [InlineData(new[] { 5 }, 4)]
    [InlineData(new[] { 5, 4 }, 3)]
    public async Task Consensus_IsAchieved_WhileMajorityIsAvailable(int[] failed, int expectedYes)
    {
        await _cluster.ElectAsync();
        await FailAndDetect(failed);

        var result = await _cluster.ProposeAsync(1, TicketStatus.Purchased);

        Assert.True(result.Achieved);
        Assert.Equal(expectedYes, result.Yes);
        Assert.Equal(failed.Length, result.NoResponse);
    }

    [Fact]
    public async Task Consensus_IsNotAchieved_WithoutMajority_AndValueIsNotApplied()
    {
        await _cluster.ElectAsync();
        await FailAndDetect(5, 4, 3);

        var result = await _cluster.ProposeAsync(1, TicketStatus.Purchased);

        Assert.Equal(2, result.LeaderId);
        Assert.Equal(2, result.Yes);
        Assert.Equal(3, result.NoResponse);
        Assert.False(result.Achieved);
        Assert.False(_cluster.ConsensusPossible);
        Assert.All(_cluster.Nodes, n => Assert.Null(n.LastCommitted));
        Assert.True(_cluster.Log.Contains("CONSENSUS NOT ACHIEVED"));
    }

    [Fact]
    public async Task FailedNode_CannotVote_AndDoesNotProcessMessages()
    {
        await _cluster.ElectAsync();
        var node = _cluster.GetNode(3);
        node.Fail();

        var reply = node.Receive(new ClusterMessage(MessageType.Propose, 5, 3, node.Term, new Proposal(1, 1, TicketStatus.Purchased)));

        Assert.Equal(MessageType.Error, reply.Type);
        Assert.Throws<InvalidOperationException>(() => node.Vote(new Proposal(1, 1, TicketStatus.Purchased)));
        await Assert.ThrowsAsync<NodeUnreachableException>(() => _cluster.GetNode(1).SendAsync(MessageType.Heartbeat, 3));
    }

    [Fact]
    public async Task UnknownNode_CannotBeContacted()
    {
        await Assert.ThrowsAsync<NodeUnreachableException>(() => _cluster.GetNode(1).SendAsync(MessageType.Heartbeat, 42));
        Assert.Throws<NotFoundException>(() => _cluster.GetNode(42));
    }

    [Fact]
    public async Task InvalidMessages_AreRejectedWithoutChangingState()
    {
        await _cluster.ElectAsync();
        var node = _cluster.GetNode(1);

        var unknownSender = node.Receive(new ClusterMessage(MessageType.Propose, 9, 1, 99, new Proposal(10, 1, TicketStatus.Purchased)));
        var staleCoordinator = node.Receive(new ClusterMessage(MessageType.Coordinator, 2, 1, 0, LeaderId: 2));
        var notFromLeader = node.Receive(new ClusterMessage(MessageType.Propose, 2, 1, node.Term, new Proposal(10, 1, TicketStatus.Purchased)));
        var unexpectedType = node.Receive(new ClusterMessage(MessageType.Vote, 2, 1, node.Term));

        Assert.All(new[] { unknownSender, staleCoordinator, notFromLeader, unexpectedType }, r => Assert.Equal(MessageType.Error, r.Type));
        Assert.Equal(5, node.LeaderId);
        Assert.Equal(0, node.LastAcceptedVersion);
    }

    [Fact]
    public async Task ConcurrentMessagesAndElections_KeepSingleLeader()
    {
        await _cluster.ElectAsync();
        var before = _cluster.GetNode(5).ReceivedMessages;

        var alive = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => _cluster.GetNode(1).SendHeartbeatAsync(5, verbose: false)));
        Assert.All(alive, Assert.True);
        Assert.Equal(50, _cluster.GetNode(5).ReceivedMessages - before);

        await Task.WhenAll(_cluster.ElectAsync(1), _cluster.ElectAsync(2), _cluster.ElectAsync(3));

        Assert.Single(_cluster.AvailableNodes, n => n.Role == NodeRole.Leader);
        Assert.Equal(5, _cluster.Leader!.Id);
    }

    [Fact]
    public async Task ConcurrentProposals_GetDistinctVersions()
    {
        await _cluster.ElectAsync();

        var results = await Task.WhenAll(Enumerable.Range(1, 5).Select(i => _cluster.ProposeAsync(i, TicketStatus.Purchased)));

        Assert.All(results, r => Assert.True(r.Achieved));
        Assert.Equal(5, _cluster.GetNode(1).Committed.Select(p => p.Version).Distinct().Count());
    }

    [Fact]
    public async Task AfterLeaderChange_ProposalIsMadeByNewLeader()
    {
        await _cluster.ElectAsync();
        await _cluster.ProposeAsync(1, TicketStatus.Purchased);
        await _cluster.SetNodeStateAsync(5, available: false, votesNo: false);

        var result = await _cluster.ProposeAsync(2, TicketStatus.Cancelled);

        Assert.Equal(4, result.LeaderId);
        Assert.True(result.Achieved);
        Assert.Equal("NO RESPONSE", result.Votes[5]);
        Assert.Equal(2, _cluster.GetNode(1).LastCommitted!.Version);
    }

    [Fact]
    public async Task RecoveredNode_RejoinsAndBecomesLeaderAgain()
    {
        await _cluster.ElectAsync();
        await FailAndDetect(5);
        await _cluster.ProposeAsync(1, TicketStatus.Purchased);

        await _cluster.SetNodeStateAsync(5, available: true, votesNo: false);
        var result = await _cluster.ProposeAsync(2, TicketStatus.Purchased);

        Assert.Equal(5, _cluster.Leader!.Id);
        Assert.Single(_cluster.AvailableNodes, n => n.Role == NodeRole.Leader);
        Assert.True(result.Achieved);
        Assert.Equal(5, result.Yes);
    }

    [Fact]
    public async Task Propose_InvalidTicketId_IsRejected()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _cluster.ProposeAsync(0, TicketStatus.Purchased));
    }
}
