using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Replication;

namespace TicketSales.Server.Dispatching.Handlers;

public class SynchronizeInstancesHandler : IOperationHandler
{
    private readonly ReplicationManager _replication;

    public SynchronizeInstancesHandler(ReplicationManager replication)
    {
        _replication = replication;
    }

    public string OperationName => "SynchronizeInstances";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        return await _replication.SynchronizeAsync();
    }
}
