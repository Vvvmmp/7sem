using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Cluster;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetClusterStatusHandler : IOperationHandler
{
    private readonly NodeCluster _cluster;

    public GetClusterStatusHandler(NodeCluster cluster)
    {
        _cluster = cluster;
    }

    public string OperationName => "GetClusterStatus";

    public Task<object> HandleAsync(JsonElement payload) => Task.FromResult<object>(_cluster.GetStatus());
}

public class SetNodeStateHandler : IOperationHandler
{
    private readonly NodeCluster _cluster;

    public SetNodeStateHandler(NodeCluster cluster)
    {
        _cluster = cluster;
    }

    public string OperationName => "SetNodeState";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<SetNodeStateRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Не указан узел");

        await _cluster.SetNodeStateAsync(request.NodeId, request.Available, request.VotesNo);
        return _cluster.GetStatus();
    }
}

public class CheckHeartbeatHandler : IOperationHandler
{
    private readonly NodeCluster _cluster;

    public CheckHeartbeatHandler(NodeCluster cluster)
    {
        _cluster = cluster;
    }

    public string OperationName => "CheckHeartbeat";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        await _cluster.DetectFailuresAsync();
        return _cluster.GetStatus();
    }
}

public class StartElectionHandler : IOperationHandler
{
    private readonly NodeCluster _cluster;

    public StartElectionHandler(NodeCluster cluster)
    {
        _cluster = cluster;
    }

    public string OperationName => "StartElection";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        await _cluster.ElectAsync();
        return _cluster.GetStatus();
    }
}

public class ProposeReservationStatusHandler : IOperationHandler
{
    private readonly NodeCluster _cluster;
    private readonly ITicketSalesService _service;

    public ProposeReservationStatusHandler(NodeCluster cluster, ITicketSalesService service)
    {
        _cluster = cluster;
        _service = service;
    }

    public string OperationName => "ProposeReservationStatus";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<ProposeReservationStatusRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Не указаны билет и статус");

        var result = await _cluster.ProposeAsync(request.TicketId, request.Status);
        if (!result.Achieved)
        {
            throw new ConsensusNotAchievedException(
                $"CONSENSUS NOT ACHIEVED: YES = {result.Yes}, Majority = {result.Majority}, " +
                $"доступно узлов {_cluster.AvailableNodes.Count}/{_cluster.Nodes.Count}. Изменение не применено");
        }

        if (request.Status == TicketStatus.Purchased)
        {
            var bought = await _service.BuyTicket(new BuyTicketRequestDto { TicketId = request.TicketId });
            result.Applied = $"Билет {bought.TicketId} куплен: PaymentId = {bought.PaymentId}, сумма {bought.Amount}";
        }
        else
        {
            var cancelled = await _service.CancelTicket(new CancelTicketRequestDto { TicketId = request.TicketId });
            result.Applied = $"Билет {cancelled.TicketId} → {cancelled.Status}";
        }

        _cluster.Log.Write($"Решение применено к данным: {result.Applied}");
        return result;
    }
}
