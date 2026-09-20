using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Networking;

public class ClientSession
{
    private readonly TcpClient _client;
    private readonly OperationDispatcher _dispatcher;

    public ClientSession(TcpClient client, OperationDispatcher dispatcher)
    {
        _client = client;
        _dispatcher = dispatcher;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var client = _client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (IOException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            Response response;
            try
            {
                var request = JsonSerializer.Deserialize<Request>(line, JsonDefaults.Options);
                if (request is null)
                {
                    response = Response.Fail(string.Empty, ErrorCodes.ValidationError, "Пустой запрос");
                }
                else
                {
                    Log($"-> {request.Operation} (requestId={request.RequestId}) начало обработки");
                    response = await _dispatcher.DispatchAsync(request);
                    Log($"<- {request.Operation} (requestId={request.RequestId}) обработка завершена, success={response.Success}");
                }
            }
            catch (JsonException)
            {
                response = Response.Fail(string.Empty, ErrorCodes.ValidationError, "Некорректный формат запроса, ожидается JSON");
            }

            try
            {
                await JsonLineProtocol.SendAsync(writer, response);
            }
            catch (IOException)
            {
                break;
            }
        }
    }

    private static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] [Thread {Environment.CurrentManagedThreadId}] {message}");
    }
}
