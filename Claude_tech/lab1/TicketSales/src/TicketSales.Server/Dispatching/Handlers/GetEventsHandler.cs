using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetEventsHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public GetEventsHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "GetEvents";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.ValueKind == JsonValueKind.Object
            ? payload.Deserialize<GetEventsRequestDto>(JsonDefaults.Options) ?? new GetEventsRequestDto()
            : new GetEventsRequestDto();

        return await _service.GetEvents(request);
    }
}
