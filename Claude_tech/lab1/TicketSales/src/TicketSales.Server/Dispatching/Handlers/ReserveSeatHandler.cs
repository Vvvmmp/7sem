using System.Text.Json;
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

    public object Handle(JsonElement payload)
    {
        var request = payload.Deserialize<ReserveSeatRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для бронирования места");

        return _service.ReserveSeat(request);
    }
}
