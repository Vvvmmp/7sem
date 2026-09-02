using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TicketSales.Shared.Protocol;

public static class JsonLineProtocol
{
    public static async Task SendAsync<T>(StreamWriter writer, T message)
    {
        var json = JsonSerializer.Serialize(message, JsonDefaults.Options);
        await writer.WriteLineAsync(json);
    }

    public static async Task<T?> ReceiveAsync<T>(StreamReader reader)
    {
        var line = await reader.ReadLineAsync();
        if (line is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(line, JsonDefaults.Options);
    }
}
