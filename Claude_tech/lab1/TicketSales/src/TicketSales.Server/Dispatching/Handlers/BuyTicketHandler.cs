using System.Text.Json;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class BuyTicketHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public BuyTicketHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "BuyTicket";

    public object Handle(JsonElement payload)
    {
        var request = payload.Deserialize<BuyTicketRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для покупки билета");

        return _service.BuyTicket(request);
    }
}
