using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.PubSub;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetPubSubStatusHandler : IOperationHandler
{
    private readonly PubSubSystem _pubSub;

    public GetPubSubStatusHandler(PubSubSystem pubSub)
    {
        _pubSub = pubSub;
    }

    public string OperationName => "GetPubSubStatus";

    public Task<object> HandleAsync(JsonElement payload) => Task.FromResult<object>(_pubSub.GetStatus());
}

public class SetPubSubAvailabilityHandler : IOperationHandler
{
    private readonly PubSubSystem _pubSub;

    public SetPubSubAvailabilityHandler(PubSubSystem pubSub)
    {
        _pubSub = pubSub;
    }

    public string OperationName => "SetPubSubAvailability";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<SetPubSubAvailabilityRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Не указан компонент Pub/Sub");

        return await _pubSub.SetAvailabilityAsync(request.Component, request.Available, request.TransientFailures);
    }
}

public class SubscribeHandler : IOperationHandler
{
    private readonly PubSubSystem _pubSub;

    public SubscribeHandler(PubSubSystem pubSub)
    {
        _pubSub = pubSub;
    }

    public string OperationName => "Subscribe";

    public Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<SubscribeRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Не указан Subscriber и тип события");

        _pubSub.Subscribe(request.Subscriber, request.EventType);
        return Task.FromResult<object>(_pubSub.GetStatus());
    }
}
