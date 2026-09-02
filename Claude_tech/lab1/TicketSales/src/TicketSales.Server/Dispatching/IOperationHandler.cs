using System.Text.Json;

namespace TicketSales.Server.Dispatching;

public interface IOperationHandler
{
    string OperationName { get; }
    object Handle(JsonElement payload);
}
