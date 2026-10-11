using System;
using Npgsql;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Replication;

/// <summary>
/// Один экземпляр хранения данных (Primary или Replica): своя БД PostgreSQL,
/// свои репозитории и свой сервис предметной области поверх них.
/// </summary>
public class DataInstance
{
    private readonly string _connectionString;
    private volatile bool _simulatedFailure;

    public DataInstance(string name, string connectionString, TimeSpan serverTimeArtificialDelay = default)
    {
        Name = name;
        _connectionString = connectionString;
        ConnectionFactory = new PostgresConnectionFactory(connectionString);
        Service = new TicketSalesServicePostgres(
            new PostgresEventRepository(ConnectionFactory),
            new PostgresSeatRepository(ConnectionFactory),
            new PostgresTicketRepository(ConnectionFactory),
            new PostgresCustomerRepository(ConnectionFactory),
            new PostgresPaymentRepository(ConnectionFactory),
            ConnectionFactory,
            serverTimeArtificialDelay);
    }

    public string Name { get; }
    public PostgresConnectionFactory ConnectionFactory { get; }
    public ITicketSalesService Service { get; }

    /// <summary>
    /// Программная модель отказа ("имитация ошибки подключения"): экземпляр считается
    /// недоступным, сервер к нему не обращается, хотя сама БД продолжает работать.
    /// </summary>
    public bool SimulatedFailure
    {
        get => _simulatedFailure;
        set => _simulatedFailure = value;
    }

    /// <summary>Проверка доступности: реальное подключение к БД и запрос SELECT 1.</summary>
    public InstanceState CheckState()
    {
        if (_simulatedFailure)
        {
            return InstanceState.Unavailable;
        }

        // После остановки/перезапуска контейнера в пуле могут остаться "мёртвые" соединения:
        // при ошибке пул очищается и проверка повторяется один раз уже на новом соединении.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var connection = ConnectionFactory.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                command.ExecuteScalar();
                return InstanceState.Available;
            }
            catch (Exception)
            {
                NpgsqlConnection.ClearPool(new NpgsqlConnection(_connectionString));
            }
        }

        return InstanceState.Unavailable;
    }
}
