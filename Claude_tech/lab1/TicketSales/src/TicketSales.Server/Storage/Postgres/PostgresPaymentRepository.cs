using System;
using System.Collections.Generic;
using Npgsql;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.Postgres;

public class PostgresPaymentRepository : IPaymentRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresPaymentRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Payment? GetById(int id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, ticket_id, amount, status, created_at FROM payments WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Payment> GetAll()
    {
        var result = new List<Payment>();
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, ticket_id, amount, status, created_at FROM payments";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public Payment Add(Payment entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO payments (ticket_id, amount, status, created_at)
            VALUES (@ticketId, @amount, @status, @createdAt)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("ticketId", entity.TicketId);
        command.Parameters.AddWithValue("amount", entity.Amount);
        command.Parameters.AddWithValue("status", entity.Status.ToString());
        command.Parameters.AddWithValue("createdAt", entity.CreatedAt);
        entity.Id = (int)command.ExecuteScalar()!;
        return entity;
    }

    public void Update(Payment entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE payments SET ticket_id = @ticketId, amount = @amount, status = @status, created_at = @createdAt WHERE id = @id";
        command.Parameters.AddWithValue("id", entity.Id);
        command.Parameters.AddWithValue("ticketId", entity.TicketId);
        command.Parameters.AddWithValue("amount", entity.Amount);
        command.Parameters.AddWithValue("status", entity.Status.ToString());
        command.Parameters.AddWithValue("createdAt", entity.CreatedAt);
        command.ExecuteNonQuery();
    }

    private static Payment Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        TicketId = reader.GetInt32(1),
        Amount = reader.GetDecimal(2),
        Status = Enum.Parse<PaymentStatus>(reader.GetString(3)),
        CreatedAt = reader.GetDateTime(4)
    };
}
