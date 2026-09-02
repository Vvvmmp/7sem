using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;

namespace TicketSales.Server.Networking;

public class TcpServer
{
    private readonly TcpListener _listener;
    private readonly OperationDispatcher _dispatcher;

    public TcpServer(IPAddress address, int port, OperationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _listener = new TcpListener(address, port);
        _listener.Start();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var session = new ClientSession(client, _dispatcher);
        await session.RunAsync(cancellationToken);
    }
}
