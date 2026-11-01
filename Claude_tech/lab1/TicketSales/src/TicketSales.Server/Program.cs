
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Cluster;
using TicketSales.Server.Configuration;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Networking;
using TicketSales.Server.PubSub;
using TicketSales.Server.Replication;

ServerSettings settings;
try
{
    settings = ServerSettings.Load(args, Environment.GetEnvironmentVariables());
}
catch (ConfigurationException ex)
{
    Console.WriteLine($"Ошибка конфигурации: {ex.Message}");
    return 2;
}

ReplicationLog.Write($"Конфигурация → {settings.Describe()}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    ReplicationLog.Write("SIGTERM → остановка сервера");
    cts.Cancel();
});

var replication = new ReplicationManager(
    new DataInstance("Primary", settings.PrimaryConnectionString, TimeSpan.FromMilliseconds(settings.ServerTimeDelayMs)),
    new DataInstance("Replica", settings.ReplicaConnectionString));

try
{
    await DatabaseStartup.InitializeWithRetryAsync(replication, settings.DbConnectAttempts, settings.DbRetryDelay, cts.Token);
}
catch (DataUnavailableException ex)
{
    Console.WriteLine($"Не удалось подключиться к базе данных: {ex.Message}");
    Console.WriteLine("Убедитесь, что запущены контейнеры БД (docker compose up -d) и параметры DB_* указаны верно.");
    return 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Сервер остановлен во время ожидания базы данных");
    return 0;
}

var cluster = NodeCluster.Create(heartbeatTimeout: TimeSpan.FromSeconds(1));
cluster.LogNodeStates();
await cluster.ElectAsync();

var dispatcher = ServerComposition.CreateDispatcher(replication, PubSubSystem.Create(), cluster);

TcpServer server;
try
{
    server = new TcpServer(IPAddress.Any, settings.Port, dispatcher);
}
catch (SocketException ex)
{
    Console.WriteLine($"Не удалось открыть порт {settings.Port}: {ex.Message}. Порт занят или недоступен, укажите другой (TICKETSALES_PORT).");
    return 1;
}

Console.WriteLine($"Сервер продажи билетов запущен на порту {server.Port} (режим {settings.Mode})");
Console.WriteLine("Нажмите Ctrl+C для остановки");

cluster.StartMonitoring(TimeSpan.FromSeconds(3), cts.Token);

await server.RunAsync(cts.Token);

Console.WriteLine("Сервер остановлен");
return 0;
