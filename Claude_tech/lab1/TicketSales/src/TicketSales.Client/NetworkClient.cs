using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Shared.Protocol;

namespace TicketSales.Client;

public class NetworkClient : IDisposable
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private string? _host;
    private int _port;

    public async Task ConnectAsync(string host, int port, int timeoutMs = 3000)
    {
        _host = host;
        _port = port;
        _client = new TcpClient();

        try
        {
            var connectTask = _client.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));

            if (completedTask != connectTask || !_client.Connected)
            {
                throw new ServerUnavailableException($"Сервер {host}:{port} недоступен", new TimeoutException("Истекло время ожидания подключения"));
            }

            await connectTask;
        }
        catch (SocketException ex)
        {
            throw new ServerUnavailableException($"Сервер {host}:{port} недоступен", ex);
        }

        var stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public async Task<Response> SendAsync(string operation, object payload)
    {
        if (_writer is null || _reader is null)
        {
            throw new NetworkException("Нет соединения с сервером");
        }

        var request = new Request
        {
            Operation = operation,
            Payload = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options)
        };

        try
        {
            await JsonLineProtocol.SendAsync(_writer, request);
            var response = await JsonLineProtocol.ReceiveAsync<Response>(_reader);
            return response ?? throw new NetworkException("Сервер закрыл соединение без ответа");
        }
        catch (IOException ex)
        {
            throw new NetworkException("Ошибка сетевого взаимодействия с сервером", ex);
        }
        catch (SocketException ex)
        {
            throw new NetworkException("Ошибка сетевого взаимодействия с сервером", ex);
        }
    }

    public async Task ReconnectAsync(int timeoutMs = 3000)
    {
        if (_host is null)
        {
            throw new InvalidOperationException("Клиент ещё не подключался к серверу");
        }

        Dispose();
        await ConnectAsync(_host, _port, timeoutMs);
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }
}
