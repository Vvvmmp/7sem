using System;
using System.Text.Json;

namespace TicketSales.Shared.Protocol;

public class Request
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string Operation { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}
