using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Replication;

namespace TicketSales.Server.Configuration;

/// <summary>
/// depends_on не гарантирует, что PostgreSQL уже принимает подключения, поэтому сервер
/// повторяет инициализацию экземпляров данных, пока БД не станет доступна или не закончатся попытки.
/// </summary>
public static class DatabaseStartup
{
    public static async Task InitializeWithRetryAsync(
        ReplicationManager replication, int attempts, TimeSpan delay, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                replication.Initialize();
                ReplicationLog.Write($"Startup → БД доступна (попытка {attempt}/{attempts})");
                return;
            }
            catch (Exception ex) when (ex is DataUnavailableException or NpgsqlException or SocketException)
            {
                if (attempt >= attempts)
                {
                    throw new DataUnavailableException(
                        $"База данных недоступна после {attempts} попыток подключения: {ex.Message}");
                }

                ReplicationLog.Write(
                    $"Startup → БД не готова (попытка {attempt}/{attempts}): {ex.Message}. Повтор через {delay.TotalMilliseconds:F0} ms");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
