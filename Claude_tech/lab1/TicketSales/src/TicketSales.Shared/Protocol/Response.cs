using System.Text.Json;

namespace TicketSales.Shared.Protocol;

public class Response
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public JsonElement? Data { get; set; }
    public ErrorInfo? Error { get; set; }

    public static Response Ok(string requestId, object? data)
    {
        return new Response
        {
            RequestId = requestId,
            Success = true,
            Data = data is null ? null : JsonSerializer.SerializeToElement(data, JsonDefaults.Options)
        };
    }

    public static Response Fail(string requestId, string code, string message)
    {
        return new Response
        {
            RequestId = requestId,
            Success = false,
            Error = new ErrorInfo { Code = code, Message = message }
        };
    }
}
