using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Networking;
using TicketSales.Server.PubSub;
using TicketSales.Server.Replication;

var port = 5050;
if (args.Length > 0 && int.TryParse(args[0], out var parsedPort))
{
    port = parsedPort;
}

var serverTimeDelayMs = 0;
if (args.Length > 1 && int.TryParse(args[1], out var parsedDelay))
{
    serverTimeDelayMs = parsedDelay;
}

var primaryConnectionString = args.Length > 2
    ? args[2]
    : "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2";

var replicaConnectionString = args.Length > 3
    ? args[3]
    : "Host=localhost;Port=5434;Database=ticketsales;Username=ticketsales;Password=ticketsales;Timeout=2";

var replication = new ReplicationManager(
    new DataInstance("Primary", primaryConnectionString, TimeSpan.FromMilliseconds(serverTimeDelayMs)),
    new DataInstance("Replica", replicaConnectionString));

try
{
    replication.Initialize();
}
catch (DataUnavailableException ex)
{
    Console.WriteLine($"Не удалось подключиться к базе данных: {ex.Message}");
    Console.WriteLine("Убедитесь, что запущен docker compose (docker compose up -d) и хотя бы один экземпляр доступен.");
    return;
}

var dispatcher = ServerComposition.CreateDispatcher(replication, PubSubSystem.Create());
var server = new TcpServer(IPAddress.Any, port, dispatcher);

Console.WriteLine($"Сервер продажи билетов запущен на порту {server.Port}");
Console.WriteLine("Нажмите Ctrl+C для остановки");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunAsync(cts.Token);

Console.WriteLine("Сервер остановлен");
