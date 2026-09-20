using System.Text.Json;
using System.Threading.Tasks;

namespace TicketSales.Server.Dispatching;

public interface IOperationHandler
{
    string OperationName { get; }
    Task<object> HandleAsync(JsonElement payload);
}
