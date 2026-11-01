using System.Threading;

namespace TicketSales.Server.Networking;

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
