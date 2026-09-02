using System.Text.Json;
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

    public object Handle(JsonElement payload)
    {
        var request = payload.Deserialize<CancelTicketRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для отмены билета");

        return _service.CancelTicket(request);
    }
}
