using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetSeatSnapshotHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public GetSeatSnapshotHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "GetSeatSnapshot";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<GetSeatSnapshotRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для получения состояния места");

        return await _service.GetSeatSnapshot(request);
    }
}
