using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Replication;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class SetInstanceAvailabilityHandler : IOperationHandler
{
    private readonly ReplicationManager _replication;

    public SetInstanceAvailabilityHandler(ReplicationManager replication)
    {
        _replication = replication;
    }

    public string OperationName => "SetInstanceAvailability";

    public Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<SetInstanceAvailabilityRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для изменения состояния экземпляра");

        return Task.FromResult<object>(_replication.SetAvailability(request.Instance, request.Available));
    }
}
