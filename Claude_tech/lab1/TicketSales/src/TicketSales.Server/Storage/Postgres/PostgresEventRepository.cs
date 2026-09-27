using System.Collections.Generic;
using Npgsql;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.Postgres;

public class PostgresEventRepository : IEventRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresEventRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Event? GetById(int id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, venue, starts_at, base_price FROM events WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Event> GetAll()
    {
        var result = new List<Event>();
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, venue, starts_at, base_price FROM events";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public Event Add(Event entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO events (title, venue, starts_at, base_price)
            VALUES (@title, @venue, @startsAt, @basePrice)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("title", entity.Title);
        command.Parameters.AddWithValue("venue", entity.Venue);
        command.Parameters.AddWithValue("startsAt", entity.StartsAt);
        command.Parameters.AddWithValue("basePrice", entity.BasePrice);
        entity.Id = (int)command.ExecuteScalar()!;
        return entity;
    }

    public void Update(Event entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE events SET title = @title, venue = @venue, starts_at = @startsAt, base_price = @basePrice WHERE id = @id";
        command.Parameters.AddWithValue("id", entity.Id);
        command.Parameters.AddWithValue("title", entity.Title);
        command.Parameters.AddWithValue("venue", entity.Venue);
        command.Parameters.AddWithValue("startsAt", entity.StartsAt);
        command.Parameters.AddWithValue("basePrice", entity.BasePrice);
        command.ExecuteNonQuery();
    }

    private static Event Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Title = reader.GetString(1),
        Venue = reader.GetString(2),
        StartsAt = reader.GetDateTime(3),
        BasePrice = reader.GetDecimal(4)
    };
}
