using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class CancelTicketHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public CancelTicketHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "CancelTicket";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<CancelTicketRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для отмены билета");

        return await _service.CancelTicket(request);
    }
}
