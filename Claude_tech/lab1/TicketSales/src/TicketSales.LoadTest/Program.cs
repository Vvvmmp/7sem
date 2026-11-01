using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

Console.OutputEncoding = Encoding.UTF8;

var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 ? int.Parse(args[1]) : 5050;
var clients = args.Length > 2 ? int.Parse(args[2]) : 50;
var requestsPerClient = args.Length > 3 ? int.Parse(args[3]) : 40;

Console.WriteLine($"Нагрузочный тест: {host}:{port}, клиентов {clients}, запросов на клиента {requestsPerClient}");

var results = new ConcurrentBag<(string Operation, double Ms, string Outcome)>();
var connectErrors = 0;
var total = Stopwatch.StartNew();

await Task.WhenAll(Enumerable.Range(1, clients).Select(async clientId =>
{
    using var client = new NetworkClient();
    try
    {
        await client.ConnectAsync(host, port);
    }
    catch (ServerUnavailableException)
    {
        System.Threading.Interlocked.Increment(ref connectErrors);
        return;
    }

    var random = new Random(clientId);
    for (var i = 0; i < requestsPerClient; i++)
    {
        var (operation, payload) = (i % 10) switch
        {
            9 => ("ReserveSeat", (object)new ReserveSeatRequestDto
            {
                EventId = 1,
                SeatId = random.Next(1, 11),
                CustomerFullName = $"Load Client {clientId}",
                CustomerEmail = $"load{clientId}_{i}_{Guid.NewGuid():N}@example.com"
            }),
            3 or 6 => ("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = random.Next(1, 11) }),
            1 => ("GetServerTime", new GetServerTimeRequestDto()),
            _ => ("GetEvents", new GetEventsRequestDto())
        };

        var sw = Stopwatch.StartNew();
        string outcome;
        try
        {
            var response = await client.SendAsync(operation, payload);
            outcome = response.Success
                ? "OK"
                : response.Error?.Code is ErrorCodes.ValidationError or ErrorCodes.NotFound ? "REJECTED" : "ERROR";
        }
        catch (NetworkException)
        {
            outcome = "ERROR";
        }

        results.Add((operation, sw.Elapsed.TotalMilliseconds, outcome));
    }
}));

total.Stop();

var all = results.ToList();
if (all.Count == 0)
{
    Console.WriteLine($"Сервер {host}:{port} недоступен: ни один клиент не подключился");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"{"Операция",-16} {"Запросов",8} {"OK",6} {"Отказ",6} {"Ошибки",7} {"avg,ms",8} {"p50",7} {"p95",7} {"p99",7} {"max",7}");
foreach (var group in all.GroupBy(r => r.Operation).OrderBy(g => g.Key).Append(all.GroupBy(_ => "ИТОГО").Single()))
{
    var latencies = group.Select(r => r.Ms).OrderBy(ms => ms).ToList();
    Console.WriteLine(
        $"{group.Key,-16} {latencies.Count,8} {group.Count(r => r.Outcome == "OK"),6} {group.Count(r => r.Outcome == "REJECTED"),6} " +
        $"{group.Count(r => r.Outcome == "ERROR"),7} {latencies.Average(),8:F1} {Percentile(latencies, 50),7:F1} " +
        $"{Percentile(latencies, 95),7:F1} {Percentile(latencies, 99),7:F1} {latencies[^1],7:F1}");
}

Console.WriteLine();
Console.WriteLine($"Время: {total.Elapsed.TotalSeconds:F2} s, пропускная способность: {all.Count / total.Elapsed.TotalSeconds:F0} запросов/с");
Console.WriteLine($"Ошибки подключения: {connectErrors}");
Console.WriteLine("Отказ — ожидаемый отказ бизнес-логики (например, место уже занято), Ошибки — сбои сервера/сети.");

return all.Any(r => r.Outcome == "ERROR") || connectErrors > 0 ? 1 : 0;

static double Percentile(System.Collections.Generic.List<double> sorted, int percentile) =>
    sorted[Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1)];
