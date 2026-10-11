using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Replication;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Networking;

public class ClientSession
{
    private static int _clientCounter;

    private readonly TcpClient _client;
    private readonly OperationDispatcher _dispatcher;
    private readonly string _clientId;

    public ClientSession(TcpClient client, OperationDispatcher dispatcher)
    {
        _client = client;
        _dispatcher = dispatcher;
        _clientId = $"Client {Interlocked.Increment(ref _clientCounter)}";
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
                    var context = new RequestContext(_clientId);
                    RequestContext.Current = context;

                    ReplicationLog.Write($"Request received: {request.Operation} (requestId={request.RequestId})");
                    response = await _dispatcher.DispatchAsync(request);
                    response.DataInstance = context.DataInstance;

                    var instanceInfo = context.DataInstance is null ? string.Empty : $" (экземпляр: {context.DataInstance})";
                    ReplicationLog.Write(response.Success
                        ? $"Request → SUCCESS{instanceInfo}"
                        : $"Request → FAIL{instanceInfo}: {response.Error?.Code} {response.Error?.Message}");

                    RequestContext.Current = null;
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
}
