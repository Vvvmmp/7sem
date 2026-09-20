using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class ReserveSeatHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public ReserveSeatHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "ReserveSeat";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<ReserveSeatRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для бронирования места");

        return await _service.ReserveSeat(request);
    }
}
