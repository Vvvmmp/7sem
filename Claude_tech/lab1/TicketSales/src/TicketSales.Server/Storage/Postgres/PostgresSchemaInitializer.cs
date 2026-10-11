using Npgsql;

namespace TicketSales.Server.Storage.Postgres;

public static class PostgresSchemaInitializer
{
    private const string CreateTablesSql = """
        CREATE TABLE IF NOT EXISTS events (
            id SERIAL PRIMARY KEY,
            title TEXT NOT NULL,
            venue TEXT NOT NULL,
            starts_at TIMESTAMPTZ NOT NULL,
            base_price NUMERIC NOT NULL
        );

        CREATE TABLE IF NOT EXISTS seats (
            id SERIAL PRIMARY KEY,
            event_id INTEGER NOT NULL REFERENCES events(id),
            row TEXT NOT NULL,
            number INTEGER NOT NULL,
            status TEXT NOT NULL,
            UNIQUE (event_id, row, number)
        );

        CREATE TABLE IF NOT EXISTS customers (
            id SERIAL PRIMARY KEY,
            full_name TEXT NOT NULL,
            email TEXT NOT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS tickets (
            id SERIAL PRIMARY KEY,
            event_id INTEGER NOT NULL REFERENCES events(id),
            seat_id INTEGER NOT NULL REFERENCES seats(id),
            customer_id INTEGER NOT NULL REFERENCES customers(id),
            status TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL
        );

        CREATE TABLE IF NOT EXISTS payments (
            id SERIAL PRIMARY KEY,
            ticket_id INTEGER NOT NULL REFERENCES tickets(id),
            amount NUMERIC NOT NULL,
            status TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL
        );
        """;

    private const string DropTablesSql = """
        DROP TABLE IF EXISTS payments;
        DROP TABLE IF EXISTS tickets;
        DROP TABLE IF EXISTS customers;
        DROP TABLE IF EXISTS seats;
        DROP TABLE IF EXISTS events;
        """;

    public static void EnsureCreatedAndSeeded(PostgresConnectionFactory factory)
    {
        using var connection = factory.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = CreateTablesSql;
            create.ExecuteNonQuery();
        }

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM events";
        var eventCount = (long)countCommand.ExecuteScalar()!;
        if (eventCount == 0)
        {
            SeedInto(connection);
        }
    }

    /// <summary>Только схема, без начальных данных: Replica получает данные копированием из Primary.</summary>
    public static void EnsureCreated(PostgresConnectionFactory factory)
    {
        using var connection = factory.Open();
        using var create = connection.CreateCommand();
        create.CommandText = CreateTablesSql;
        create.ExecuteNonQuery();
    }

    /// <summary>Used only by the test suite and ReplicationDemo, for a clean/repeatable starting state.</summary>
    public static void ResetAndSeed(PostgresConnectionFactory factory)
    {
        using var connection = factory.Open();
        using (var drop = connection.CreateCommand())
        {
            drop.CommandText = DropTablesSql;
            drop.ExecuteNonQuery();
        }

        using (var create = connection.CreateCommand())
        {
            create.CommandText = CreateTablesSql;
            create.ExecuteNonQuery();
        }

        SeedInto(connection);
    }

    private static void SeedInto(NpgsqlConnection connection)
    {
        using var insertEvent1 = connection.CreateCommand();
        insertEvent1.CommandText = """
            INSERT INTO events (title, venue, starts_at, base_price)
            VALUES ('Концерт группы Horizon', 'Дворец спорта', now() + interval '14 days', 1500)
            RETURNING id;
            """;
        var event1Id = (int)insertEvent1.ExecuteScalar()!;

        for (var row = 1; row <= 2; row++)
        {
            for (var number = 1; number <= 5; number++)
            {
                using var insertSeat = connection.CreateCommand();
                insertSeat.CommandText = "INSERT INTO seats (event_id, row, number, status) VALUES (@eventId, @row, @number, 'Free')";
                insertSeat.Parameters.AddWithValue("eventId", event1Id);
                insertSeat.Parameters.AddWithValue("row", row.ToString());
                insertSeat.Parameters.AddWithValue("number", number);
                insertSeat.ExecuteNonQuery();
            }
        }

        using var insertEvent2 = connection.CreateCommand();
        insertEvent2.CommandText = """
            INSERT INTO events (title, venue, starts_at, base_price)
            VALUES ('Стендап вечер', 'Клуб Смех', now() + interval '7 days', 800)
            RETURNING id;
            """;
        var event2Id = (int)insertEvent2.ExecuteScalar()!;

        for (var number = 1; number <= 8; number++)
        {
            using var insertSeat = connection.CreateCommand();
            insertSeat.CommandText = "INSERT INTO seats (event_id, row, number, status) VALUES (@eventId, '1', @number, 'Free')";
            insertSeat.Parameters.AddWithValue("eventId", event2Id);
            insertSeat.Parameters.AddWithValue("number", number);
            insertSeat.ExecuteNonQuery();
        }
    }
}
