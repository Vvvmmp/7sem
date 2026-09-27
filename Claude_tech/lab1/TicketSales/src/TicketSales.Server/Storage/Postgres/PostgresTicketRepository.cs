using System;
using System.Collections.Generic;
using Npgsql;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.Postgres;

public class PostgresTicketRepository : ITicketRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresTicketRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Ticket? GetById(int id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, event_id, seat_id, customer_id, status, created_at FROM tickets WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Ticket> GetAll()
    {
        var result = new List<Ticket>();
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, event_id, seat_id, customer_id, status, created_at FROM tickets";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public Ticket Add(Ticket entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tickets (event_id, seat_id, customer_id, status, created_at)
            VALUES (@eventId, @seatId, @customerId, @status, @createdAt)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("eventId", entity.EventId);
        command.Parameters.AddWithValue("seatId", entity.SeatId);
        command.Parameters.AddWithValue("customerId", entity.CustomerId);
        command.Parameters.AddWithValue("status", entity.Status.ToString());
        command.Parameters.AddWithValue("createdAt", entity.CreatedAt);
        entity.Id = (int)command.ExecuteScalar()!;
        return entity;
    }

    public void Update(Ticket entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tickets SET event_id = @eventId, seat_id = @seatId, customer_id = @customerId, status = @status, created_at = @createdAt WHERE id = @id";
        command.Parameters.AddWithValue("id", entity.Id);
        command.Parameters.AddWithValue("eventId", entity.EventId);
        command.Parameters.AddWithValue("seatId", entity.SeatId);
        command.Parameters.AddWithValue("customerId", entity.CustomerId);
        command.Parameters.AddWithValue("status", entity.Status.ToString());
        command.Parameters.AddWithValue("createdAt", entity.CreatedAt);
        command.ExecuteNonQuery();
    }

    private static Ticket Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        EventId = reader.GetInt32(1),
        SeatId = reader.GetInt32(2),
        CustomerId = reader.GetInt32(3),
        Status = Enum.Parse<TicketStatus>(reader.GetString(4)),
        CreatedAt = reader.GetDateTime(5)
    };
}
