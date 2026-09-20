using System.Text.Json;
using System.Threading.Tasks;
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

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<BuyTicketRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для покупки билета");

        return await _service.BuyTicket(request);
    }
}
