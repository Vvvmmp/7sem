using System.Threading;

namespace TicketSales.Server.Networking;

/// <summary>
/// Данные текущего запроса, доступные всем уровням сервера без передачи через параметры:
/// ClientSession создаёт контекст, сервис записывает в него экземпляр данных,
/// ClientSession возвращает этот экземпляр клиенту в ответе.
/// </summary>
public sealed class RequestContext
{
    private static readonly AsyncLocal<RequestContext?> CurrentContext = new();

    public RequestContext(string clientId)
    {
        ClientId = clientId;
    }

    public static RequestContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }

    public string ClientId { get; }
    public string? DataInstance { get; set; }
}
