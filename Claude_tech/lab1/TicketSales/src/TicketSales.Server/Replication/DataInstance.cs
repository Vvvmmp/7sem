using System;
using Npgsql;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Replication;

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

    public bool SimulatedFailure
    {
        get => _simulatedFailure;
        set => _simulatedFailure = value;
    }

    public InstanceState CheckState()
    {
        if (_simulatedFailure)
        {
            return InstanceState.Unavailable;
        }

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
