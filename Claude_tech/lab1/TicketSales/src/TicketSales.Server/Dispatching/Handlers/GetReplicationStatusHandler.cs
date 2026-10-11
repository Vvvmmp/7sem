using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Replication;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetReplicationStatusHandler : IOperationHandler
{
    private readonly ReplicationManager _replication;

    public GetReplicationStatusHandler(ReplicationManager replication)
    {
        _replication = replication;
    }

    public string OperationName => "GetReplicationStatus";

    public Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.ValueKind == JsonValueKind.Object
            ? payload.Deserialize<GetReplicationStatusRequestDto>(JsonDefaults.Options)
            : null;

        return Task.FromResult<object>(_replication.GetStatus(request?.SeatId));
    }
}
