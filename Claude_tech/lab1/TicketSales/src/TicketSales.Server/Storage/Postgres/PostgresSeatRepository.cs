using System;
using System.Collections.Generic;
using Npgsql;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.Postgres;

public class PostgresSeatRepository : ISeatRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresSeatRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Seat? GetById(int id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, event_id, row, number, status FROM seats WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Seat> GetAll()
    {
        var result = new List<Seat>();
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, event_id, row, number, status FROM seats";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public IReadOnlyList<Seat> GetByEvent(int eventId)
    {
        var result = new List<Seat>();
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, event_id, row, number, status FROM seats WHERE event_id = @eventId";
        command.Parameters.AddWithValue("eventId", eventId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public Seat Add(Seat entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO seats (event_id, row, number, status)
            VALUES (@eventId, @row, @number, @status)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("eventId", entity.EventId);
        command.Parameters.AddWithValue("row", entity.Row);
        command.Parameters.AddWithValue("number", entity.Number);
        command.Parameters.AddWithValue("status", entity.Status.ToString());
        entity.Id = (int)command.ExecuteScalar()!;
        return entity;
    }

    public void Update(Seat entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE seats SET event_id = @eventId, row = @row, number = @number, status = @status WHERE id = @id";
        command.Parameters.AddWithValue("id", entity.Id);
        command.Parameters.AddWithValue("eventId", entity.EventId);
        command.Parameters.AddWithValue("row", entity.Row);
        command.Parameters.AddWithValue("number", entity.Number);
        command.Parameters.AddWithValue("status", entity.Status.ToString());
        command.ExecuteNonQuery();
    }

    private static Seat Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        EventId = reader.GetInt32(1),
        Row = reader.GetString(2),
        Number = reader.GetInt32(3),
        Status = Enum.Parse<SeatStatus>(reader.GetString(4))
    };
}
