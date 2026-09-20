using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Services;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetServerTimeHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public GetServerTimeHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "GetServerTime";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        return await _service.GetServerTime();
    }
}
