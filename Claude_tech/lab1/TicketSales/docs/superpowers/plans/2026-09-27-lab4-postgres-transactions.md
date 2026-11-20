# Lab 4: PostgreSQL persistence + lock-protected reservation + transactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the domain data from in-memory dictionaries to PostgreSQL, keep the Lab 3 `lock`-based mutual exclusion for `ReserveSeat` (now guarding real DB writes), and add an explicit BEGIN/COMMIT/ROLLBACK demonstration for one composite operation (create `Ticket` + update `Seat.Status`), all reachable only through the existing Client → Server → Database path (no direct DB access from the client).

**Architecture:** Add a `Storage/Postgres/*` implementation of the existing `IEventRepository`/`ISeatRepository`/`ITicketRepository`/`ICustomerRepository`/`IPaymentRepository` interfaces (raw ADO.NET via Npgsql, synchronous– matches the existing synchronous `IRepository<T>` contract and the existing synchronous `lock`-based `TicketSalesService` from Lab 3). A new `TicketSalesServicePostgres` implements `ITicketSalesService`; its `ReserveSeat` keeps the same `lock (_seatLock) { check; write }` shape as Lab 3, but the body inside the lock issues raw SQL directly against one `NpgsqlConnection`/`NpgsqlTransaction` (bypassing the generic repository calls for just this one method) so the composite write can be demonstrated with and without an explicit transaction, and with a simulated fault between the two writes. `Server/Program.cs` switches to this Postgres-backed stack; the Lab 2/3 in-memory stack, `RaceDemo`, and `SemaphoreDemo` are left untouched (they remain valid historical artifacts of those labs).

**Tech Stack:** PostgreSQL 17 (Docker, `postgres:17-alpine`, via `docker-compose.yml`), `Npgsql` (raw ADO.NET, no ORM), .NET 10 / xunit– same versions as the rest of the repo.

**Spec:** `lab4.md` (repo root next to this plan's project)– Части 1-4, Требования as numbered there.

## Global Constraints

- DBMS: PostgreSQL 17 (`postgres:17-alpine` Docker image)– user's explicit choice, justified in Task 6 docs as: zero-cost local setup via Docker, full ACID/transaction support needed for Part 3, widely known.
- DB access: raw ADO.NET via `Npgsql`– no EF Core, no migrations tooling. All SQL is hand-written so `BEGIN`/`COMMIT`/`ROLLBACK` are visible in the code for the defense.
- Host port `5433` maps to the container's `5432` (port 5432 is already occupied by another local Docker container on this machine– confirmed via `lsof`), so the compose file must not clash with it.
- Repository interfaces (`IRepository<T>`, `ISeatRepository`, etc.) stay **synchronous**– matches the existing in-memory implementations and the existing synchronous `TicketSalesService`/`lock` pattern from Lab 3; do not introduce `async` into these interfaces.
- Only one composite operation gets explicit transaction control: `ReserveSeat` (create `Ticket` + update `Seat.Status`), per the user's explicit choice. `BuyTicket`/`CancelTicket`/`GetEvents` use the plain per-call repository methods (each Postgres statement auto-commits on its own, same as Postgres' default behavior), no special transaction demo needed there.
- Do not touch `TicketSalesService.cs`, `TicketSalesServiceSemaphore.cs`, `RaceDemo`, `SemaphoreDemo`, or their tests– those are Lab 2/3 deliverables and must keep working with in-memory storage exactly as committed.
- Single final commit for all of Lab 4 (per user instruction)– do not commit after each task.
- Test project: all Postgres-backed tests share one external DB instance, so the assembly must not run test collections in parallel (add `tests/TicketSales.Tests/xunit.runner.json` with `"parallelizeTestCollections": false`, copied to output).

## Review Focus

- **Server started with no Postgres reachable** (Docker not up / wrong port): the app must print a clear error and exit non-zero, not crash with an unhandled stack trace or hang– Task 1 covers this with a fail-fast connectivity check in `Program.cs`.
- **`ReserveSeat` called with a `SeatId` that doesn't exist in the DB** (not just event/seat validation logic, but the actual Postgres read returning no row): must surface `NotFoundException` → `NOT_FOUND`, not throw an unhandled `NullReferenceException` from a null `NpgsqlDataReader` read– Task 3 covers this in `PostgresSeatRepository.GetById`.
- **Concurrent `ReserveSeat` on two different seats at the same time** (not the same seat): must NOT serialize unnecessarily-slow– the `lock` covers the whole critical section so this is technically serialized by design (matches Lab 3's single shared `_seatLock`), but the test suite should still assert it doesn't deadlock or throw when hammered with mixed same-seat/different-seat load– covered by reusing the existing 5-clients-one-seat concurrency pattern (only same-seat contention needs a correctness assertion; cross-seat throughput isn't a stated requirement, so this is a smoke check, not a hard assertion).
- **`SimulateErrorAfterTicketInsert=true` combined with `UseTransaction=false`, run twice on the same seat**: the first run leaves a partial state (ticket exists, seat still `Free`); if this path doesn't handle it, a second `ReserveSeat` on the same seat would still succeed and create a **second** ticket for the same seat while the first orphaned ticket also exists– Task 5 explicitly asserts the seat stays reservable exactly once even after a partial-failure run, and that both tickets/situations are visible via `GetSeatSnapshot`.
- **`GetSeatSnapshot` for a seat ID that doesn't exist**: must return a domain error (`NOT_FOUND`), not an empty/zeroed success response that would be misread as "0 tickets, seat free"– Task 5 covers this with an explicit test.

---

### Task 1: Docker Compose + Npgsql wiring + fail-fast connectivity check

**Files:**
- Create: `docker-compose.yml` (repo root: `/Users/vladislav/University/7sem/Claude_tech/lab1/TicketSales/docker-compose.yml`)
- Modify: `src/TicketSales.Server/TicketSales.Server.csproj` (add Npgsql package reference)
- Create: `src/TicketSales.Server/Storage/Postgres/PostgresConnectionFactory.cs`
- Modify: `src/TicketSales.Server/Program.cs` (connection string wiring + fail-fast check– full wiring to repositories happens in Task 2)

**Interfaces:**
- Produces: `PostgresConnectionFactory(string connectionString)` with `NpgsqlConnection Open()` (opens and returns a ready-to-use connection; throws `NpgsqlException`/`SocketException` if the server is unreachable).

- [ ] **Step 1: Write `docker-compose.yml`**

```yaml
services:
  postgres:
    image: postgres:17-alpine
    container_name: ticketsales-postgres
    environment:
      POSTGRES_DB: ticketsales
      POSTGRES_USER: ticketsales
      POSTGRES_PASSWORD: ticketsales
    ports:
      - "5433:5432"
    volumes:
      - ticketsales-postgres-data:/var/lib/postgresql/data

volumes:
  ticketsales-postgres-data:
```

Port `5433` (host) → `5432` (container) because port 5432 is already taken by another local container on this machine (confirmed via `lsof -iTCP -sTCP:LISTEN`).

- [ ] **Step 2: Bring it up and confirm it's reachable**

Run: `docker compose up -d` then `docker compose ps`
Expected: `ticketsales-postgres` shows `running`/`healthy`.

Run: `docker exec ticketsales-postgres psql -U ticketsales -d ticketsales -c 'select 1;'`
Expected: prints `1`– confirms credentials and DB name are correct before writing any C# against it.

- [ ] **Step 3: Add the Npgsql package to the server project**

```bash
cd /Users/vladislav/University/7sem/Claude_tech/lab1/TicketSales
dotnet add src/TicketSales.Server/TicketSales.Server.csproj package Npgsql
```

- [ ] **Step 4: `PostgresConnectionFactory`**

```csharp
// src/TicketSales.Server/Storage/Postgres/PostgresConnectionFactory.cs
using Npgsql;

namespace TicketSales.Server.Storage.Postgres;

public class PostgresConnectionFactory
{
    private readonly string _connectionString;

    public PostgresConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public NpgsqlConnection Open()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
```

- [ ] **Step 5: Fail-fast connectivity check in `Program.cs`**

Add a constant default connection string and an optional 3rd CLI arg override, plus a startup check that exits cleanly (not an unhandled crash) if Postgres isn't reachable:

```csharp
var connectionString = args.Length > 2
    ? args[2]
    : "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales";

var connectionFactory = new PostgresConnectionFactory(connectionString);

try
{
    using var probe = connectionFactory.Open();
}
catch (Exception ex)
{
    Console.WriteLine($"Не удалось подключиться к базе данных: {ex.Message}");
    Console.WriteLine("Убедитесь, что запущен docker compose (docker compose up -d) и БД доступна.");
    return;
}
```

Place this right after computing `connectionString`/`connectionFactory`, before any repository is constructed. (The rest of `Program.cs`'s wiring– repositories, schema init, service, handlers– is done in Task 2; this step only adds the connection string + fail-fast probe so Task 1 is independently testable.)

Add `using TicketSales.Server.Storage.Postgres;` to `Program.cs`.

- [ ] **Step 6: Verify the fail-fast path manually**

Run: `docker compose stop postgres` then `dotnet run --project src/TicketSales.Server`
Expected: prints the "Не удалось подключиться..." message and exits– no stack trace, no hang.

Run: `docker compose start postgres` then re-run the same command
Expected: passes the probe silently (Task 2 will make it actually start serving).

---

### Task 2: PostgreSQL schema + repository implementations, wired into `Program.cs`

**Files:**
- Create: `src/TicketSales.Server/Storage/Postgres/PostgresSchemaInitializer.cs`
- Create: `src/TicketSales.Server/Storage/Postgres/PostgresEventRepository.cs`
- Create: `src/TicketSales.Server/Storage/Postgres/PostgresSeatRepository.cs`
- Create: `src/TicketSales.Server/Storage/Postgres/PostgresTicketRepository.cs`
- Create: `src/TicketSales.Server/Storage/Postgres/PostgresCustomerRepository.cs`
- Create: `src/TicketSales.Server/Storage/Postgres/PostgresPaymentRepository.cs`
- Modify: `src/TicketSales.Server/Program.cs` (replace `InMemory*Repository` construction with `Postgres*Repository`, call schema init, keep using the existing Lab 3 `TicketSalesService` for now– Task 3 swaps it for `TicketSalesServicePostgres`)

**Interfaces:**
- Consumes: `PostgresConnectionFactory.Open()` (Task 1).
- Produces: `PostgresSchemaInitializer.EnsureCreatedAndSeeded(PostgresConnectionFactory factory)` (idempotent: creates tables if missing, seeds `events`+`seats` only if `events` is empty– mirrors `SeedData.Populate`). `PostgresEventRepository`, `PostgresSeatRepository`, `PostgresTicketRepository`, `PostgresCustomerRepository`, `PostgresPaymentRepository`– each implements the same interface the in-memory version does (`IEventRepository`, `ISeatRepository`, `ITicketRepository`, `ICustomerRepository`, `IPaymentRepository`), same method signatures, so `TicketSalesService`/`TicketSalesServicePostgres` can use either without changes to the interfaces.

- [ ] **Step 1: Schema + seed initializer**

```csharp
// src/TicketSales.Server/Storage/Postgres/PostgresSchemaInitializer.cs
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

    /// <summary>Used only by the test suite and TransactionDemo, for a clean/repeatable starting state.</summary>
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
```

This mirrors `SeedData.Populate` exactly (2 events, 10+8 seats), but the caller no longer gets fixed IDs 1/2 for free– Postgres assigns them via `SERIAL`. Since `ResetAndSeed` always drops everything first, in practice event 1 / seat 1 will again be IDs 1/1 on a freshly reset DB (sequences restart because the tables themselves are recreated), so existing test expectations (`SeedData.FirstEventId == 1`, `SeedData.FirstEventFirstSeatId == 1`) keep working against a `ResetAndSeed`'d database. Do not reuse `SeedData` itself (it's in-memory-repository-specific); this is a intentionally separate, SQL-native seeding routine.

- [ ] **Step 2: Repository implementations**

```csharp
// src/TicketSales.Server/Storage/Postgres/PostgresEventRepository.cs
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
```

```csharp
// src/TicketSales.Server/Storage/Postgres/PostgresSeatRepository.cs
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
```

```csharp
// src/TicketSales.Server/Storage/Postgres/PostgresCustomerRepository.cs
using System.Collections.Generic;
using Npgsql;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Storage.Postgres;

public class PostgresCustomerRepository : ICustomerRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public PostgresCustomerRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Customer? GetById(int id)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, full_name, email FROM customers WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<Customer> GetAll()
    {
        var result = new List<Customer>();
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, full_name, email FROM customers";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public Customer? FindByEmail(string email)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, full_name, email FROM customers WHERE lower(email) = lower(@email)";
        command.Parameters.AddWithValue("email", email);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public Customer Add(Customer entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO customers (full_name, email)
            VALUES (@fullName, @email)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("fullName", entity.FullName);
        command.Parameters.AddWithValue("email", entity.Email);
        entity.Id = (int)command.ExecuteScalar()!;
        return entity;
    }

    public void Update(Customer entity)
    {
        using var connection = _connectionFactory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE customers SET full_name = @fullName, email = @email WHERE id = @id";
        command.Parameters.AddWithValue("id", entity.Id);
        command.Parameters.AddWithValue("fullName", entity.FullName);
        command.Parameters.AddWithValue("email", entity.Email);
        command.ExecuteNonQuery();
    }

    private static Customer Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        FullName = reader.GetString(1),
        Email = reader.GetString(2)
    };
}
```

```csharp
// src/TicketSales.Server/Storage/Postgres/PostgresTicketRepository.cs
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
```

```csharp
// src/TicketSales.Server/Storage/Postgres/PostgresPaymentRepository.cs
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
```

- [ ] **Step 3: Wire `Program.cs` to Postgres (still using the existing `TicketSalesService`, Task 3 swaps it)**

Replace the in-memory repository construction and `SeedData.Populate(...)` call with:

```csharp
PostgresSchemaInitializer.EnsureCreatedAndSeeded(connectionFactory);

var eventRepository = new PostgresEventRepository(connectionFactory);
var seatRepository = new PostgresSeatRepository(connectionFactory);
var ticketRepository = new PostgresTicketRepository(connectionFactory);
var customerRepository = new PostgresCustomerRepository(connectionFactory);
var paymentRepository = new PostgresPaymentRepository(connectionFactory);
```

Remove the `using TicketSales.Server.Storage.InMemory;` line and the `SeedData.Populate(...)` call (schema init now seeds). Keep constructing `TicketSalesService` exactly as before for this task– swapping to `TicketSalesServicePostgres` happens in Task 3, so this task's manual test below still exercises the Lab 3 service class, just now pointed at Postgres-backed repositories (proves the repositories work before the more complex service rewrite).

- [ ] **Step 4: Manual smoke test– prove Часть 1 end-to-end**

```bash
docker compose up -d
dotnet run --project src/TicketSales.Server -- 5050
```
(separate terminal)
```bash
dotnet run --project src/TicketSales.Client
```
Use menu `1` (список событий)– expect 2 events with free-seat counts (10 and 8) read from Postgres. Use menu `2` (забронировать место) on event 1 / seat 1 with some name/email– expect success. Repeat menu `1`– expect event 1's free seat count to have dropped by one. This proves reads, an insert (customer/ticket), and an update (seat) all round-trip through Postgres via the server (Требование 4/5 Часть 1).

Run: `docker exec ticketsales-postgres psql -U ticketsales -d ticketsales -c "select id, status from seats where id=1;"`
Expected: `status` is `Reserved`.

---

### Task 3: `TicketSalesServicePostgres` with `lock`-protected `ReserveSeat` (Часть 2)

**Files:**
- Create: `src/TicketSales.Server/Domain/Services/TicketSalesServicePostgres.cs`
- Modify: `src/TicketSales.Server/Program.cs` (construct `TicketSalesServicePostgres` instead of `TicketSalesService`)
- Create: `tests/TicketSales.Tests/PostgresTestServerFixture.cs`
- Test: `tests/TicketSales.Tests/PostgresReserveSeatConcurrencyTests.cs`
- Create: `tests/TicketSales.Tests/xunit.runner.json`
- Modify: `tests/TicketSales.Tests/TicketSales.Tests.csproj` (copy `xunit.runner.json` to output, add `Npgsql` package reference so tests can talk to Postgres directly for setup/assertions, add `ProjectReference` is not needed– tests already reference `TicketSales.Server`)

**Interfaces:**
- Consumes: `PostgresConnectionFactory`, `PostgresSchemaInitializer.ResetAndSeed` (Task 2), all `Postgres*Repository` classes (Task 2).
- Produces: `TicketSalesServicePostgres(IEventRepository, ISeatRepository, ITicketRepository, ICustomerRepository, IPaymentRepository, PostgresConnectionFactory connectionFactory) : ITicketSalesService`– same constructor shape (repos first) plus the connection factory needed for the raw-SQL composite `ReserveSeat`. `GetEvents`/`BuyTicket`/`CancelTicket`/`GetServerTime` delegate to the same repository calls as `TicketSalesService` (copy those method bodies verbatim– they don't need the raw-SQL treatment, only `ReserveSeat` does).

- [ ] **Step 1: Write `xunit.runner.json` and wire it into the test csproj**

```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "parallelizeAssembly": false,
  "parallelizeTestCollections": false
}
```

In `TicketSales.Tests.csproj`, add inside an `<ItemGroup>`:
```xml
<Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
```
and add the Npgsql package:
```bash
dotnet add tests/TicketSales.Tests/TicketSales.Tests.csproj package Npgsql
```

- [ ] **Step 2: `PostgresTestServerFixture`**

```csharp
// tests/TicketSales.Tests/PostgresTestServerFixture.cs
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.Postgres;
using Xunit;

namespace TicketSales.Tests;

public class PostgresTestServerFixture : IAsyncLifetime
{
    public const string ConnectionString = "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales";

    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public int Port { get; private set; }

    public Task InitializeAsync()
    {
        var connectionFactory = new PostgresConnectionFactory(ConnectionString);
        PostgresSchemaInitializer.ResetAndSeed(connectionFactory);

        var eventRepository = new PostgresEventRepository(connectionFactory);
        var seatRepository = new PostgresSeatRepository(connectionFactory);
        var ticketRepository = new PostgresTicketRepository(connectionFactory);
        var customerRepository = new PostgresCustomerRepository(connectionFactory);
        var paymentRepository = new PostgresPaymentRepository(connectionFactory);

        var service = new TicketSalesServicePostgres(
            eventRepository, seatRepository, ticketRepository, customerRepository, paymentRepository, connectionFactory);

        IOperationHandler[] handlers =
        {
            new GetEventsHandler(service),
            new ReserveSeatHandler(service),
            new BuyTicketHandler(service),
            new CancelTicketHandler(service),
            new GetServerTimeHandler(service),
            new GetSeatSnapshotHandler(service)
        };

        var dispatcher = new OperationDispatcher(handlers);
        var server = new TcpServer(IPAddress.Loopback, 0, dispatcher);
        Port = server.Port;

        _cts = new CancellationTokenSource();
        _serverTask = server.RunAsync(_cts.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts?.Cancel();
        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
        }
    }
}
```

Note: `GetSeatSnapshotHandler` doesn't exist yet– it's added in Task 4. This fixture file references it, so Task 3's build won't succeed until Task 4 also lands; **do Task 3 and Task 4 in the same working session before running `dotnet build`**, or temporarily comment out that one array line while verifying Task 3 in isolation. (The plan keeps them as separate tasks because they test different requirements– Часть 2 vs Часть 3– but they share this one fixture file.)

- [ ] **Step 3: `TicketSalesServicePostgres`– `GetEvents`/`BuyTicket`/`CancelTicket`/`GetServerTime` (straight copy) + `ReserveSeat` (lock-protected raw SQL)**

```csharp
// src/TicketSales.Server/Domain/Services/TicketSalesServicePostgres.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Storage;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;

namespace TicketSales.Server.Domain.Services;

public class TicketSalesServicePostgres : ITicketSalesService
{
    private readonly IEventRepository _eventRepository;
    private readonly ISeatRepository _seatRepository;
    private readonly ITicketRepository _ticketRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly object _seatLock = new();

    public TicketSalesServicePostgres(
        IEventRepository eventRepository,
        ISeatRepository seatRepository,
        ITicketRepository ticketRepository,
        ICustomerRepository customerRepository,
        IPaymentRepository paymentRepository,
        PostgresConnectionFactory connectionFactory)
    {
        _eventRepository = eventRepository;
        _seatRepository = seatRepository;
        _ticketRepository = ticketRepository;
        _customerRepository = customerRepository;
        _paymentRepository = paymentRepository;
        _connectionFactory = connectionFactory;
    }

    public Task<GetEventsResponseDto> GetEvents(GetEventsRequestDto request)
    {
        var events = _eventRepository.GetAll().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.TitleContains))
        {
            events = events.Where(e => e.Title.Contains(request.TitleContains, StringComparison.OrdinalIgnoreCase));
        }

        var result = new GetEventsResponseDto();
        foreach (var e in events)
        {
            var freeSeats = _seatRepository.GetByEvent(e.Id).Count(s => s.Status == SeatStatus.Free);
            result.Events.Add(new EventDto
            {
                Id = e.Id,
                Title = e.Title,
                StartsAt = e.StartsAt,
                Venue = e.Venue,
                BasePrice = e.BasePrice,
                FreeSeats = freeSeats
            });
        }

        return Task.FromResult(result);
    }

    public Task<ReserveSeatResponseDto> ReserveSeat(ReserveSeatRequestDto request)
    {
        if (request.EventId <= 0 || request.SeatId <= 0)
        {
            throw new ValidationException("Не указан идентификатор события или места");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerFullName))
        {
            throw new ValidationException("Не указано имя покупателя");
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail) || !request.CustomerEmail.Contains('@'))
        {
            throw new ValidationException("Некорректный email покупателя");
        }

        var eventEntity = _eventRepository.GetById(request.EventId)
            ?? throw new NotFoundException("Событие не найдено");

        var seatExists = _seatRepository.GetById(request.SeatId)
            ?? throw new NotFoundException("Место не найдено");

        if (seatExists.EventId != eventEntity.Id)
        {
            throw new ValidationException("Указанное место не принадлежит указанному событию");
        }

        var customer = _customerRepository.FindByEmail(request.CustomerEmail);
        if (customer is null)
        {
            customer = _customerRepository.Add(new Customer
            {
                FullName = request.CustomerFullName,
                Email = request.CustomerEmail
            });
        }

        int ticketId;
        SeatStatus finalStatus;

        lock (_seatLock)
        {
            Log($"ReserveSeat: seat={request.SeatId} LOCK");

            using var connection = _connectionFactory.Open();
            using var transaction = request.UseTransaction ? connection.BeginTransaction() : null;

            try
            {
                using var checkCommand = connection.CreateCommand();
                checkCommand.Transaction = transaction;
                checkCommand.CommandText = "SELECT status FROM seats WHERE id = @id";
                checkCommand.Parameters.AddWithValue("id", request.SeatId);
                var currentStatusText = (string)checkCommand.ExecuteScalar()!;
                var currentStatus = Enum.Parse<SeatStatus>(currentStatusText);

                Log($"ReserveSeat: seat={request.SeatId} проверка статуса -> {currentStatus}");

                if (currentStatus != SeatStatus.Free)
                {
                    Log($"ReserveSeat: seat={request.SeatId} FAIL (уже занято)");
                    Log($"ReserveSeat: seat={request.SeatId} UNLOCK");
                    throw new ValidationException("Место уже занято");
                }

                using var insertTicketCommand = connection.CreateCommand();
                insertTicketCommand.Transaction = transaction;
                insertTicketCommand.CommandText = """
                    INSERT INTO tickets (event_id, seat_id, customer_id, status, created_at)
                    VALUES (@eventId, @seatId, @customerId, @status, @createdAt)
                    RETURNING id;
                    """;
                insertTicketCommand.Parameters.AddWithValue("eventId", eventEntity.Id);
                insertTicketCommand.Parameters.AddWithValue("seatId", request.SeatId);
                insertTicketCommand.Parameters.AddWithValue("customerId", customer.Id);
                insertTicketCommand.Parameters.AddWithValue("status", TicketStatus.Reserved.ToString());
                insertTicketCommand.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
                ticketId = (int)insertTicketCommand.ExecuteScalar()!;

                Log($"ReserveSeat: seat={request.SeatId} создан ticketId={ticketId} (Operation 1: Create Ticket)");

                if (request.SimulateErrorAfterTicketInsert)
                {
                    Log($"ReserveSeat: seat={request.SeatId} ИСКУССТВЕННАЯ ОШИБКА между Operation 1 и Operation 2");
                    throw new ValidationException("Искусственная ошибка между операциями (демонстрация транзакции)");
                }

                using var updateSeatCommand = connection.CreateCommand();
                updateSeatCommand.Transaction = transaction;
                updateSeatCommand.CommandText = "UPDATE seats SET status = @status WHERE id = @id";
                updateSeatCommand.Parameters.AddWithValue("status", SeatStatus.Reserved.ToString());
                updateSeatCommand.Parameters.AddWithValue("id", request.SeatId);
                updateSeatCommand.ExecuteNonQuery();

                Log($"ReserveSeat: seat={request.SeatId} обновлён статус -> Reserved (Operation 2: Update Seat)");

                transaction?.Commit();
                finalStatus = SeatStatus.Reserved;

                Log($"ReserveSeat: seat={request.SeatId} SUCCESS{(transaction != null ? " COMMIT" : "")}");
                Log($"ReserveSeat: seat={request.SeatId} UNLOCK");
            }
            catch
            {
                transaction?.Rollback();
                if (transaction != null)
                {
                    Log($"ReserveSeat: seat={request.SeatId} ROLLBACK");
                }
                throw;
            }
        }

        return Task.FromResult(new ReserveSeatResponseDto
        {
            TicketId = ticketId,
            Seat = new SeatDto
            {
                Id = request.SeatId,
                Row = seatExists.Row,
                Number = seatExists.Number,
                Status = finalStatus
            }
        });
    }

    public Task<GetServerTimeResponseDto> GetServerTime()
    {
        return Task.FromResult(new GetServerTimeResponseDto { ServerTimeUtc = DateTime.UtcNow });
    }

    public Task<GetSeatSnapshotResponseDto> GetSeatSnapshot(GetSeatSnapshotRequestDto request)
    {
        var seat = _seatRepository.GetById(request.SeatId)
            ?? throw new NotFoundException("Место не найдено");

        var ticketCount = _ticketRepository.GetAll().Count(t => t.SeatId == request.SeatId);

        return Task.FromResult(new GetSeatSnapshotResponseDto
        {
            Status = seat.Status,
            TicketCount = ticketCount
        });
    }

    private static void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"[{timestamp}] [Thread {Environment.CurrentManagedThreadId}] {message}");
    }

    public Task<BuyTicketResponseDto> BuyTicket(BuyTicketRequestDto request)
    {
        if (request.TicketId <= 0)
        {
            throw new ValidationException("Не указан идентификатор билета");
        }

        var ticket = _ticketRepository.GetById(request.TicketId)
            ?? throw new NotFoundException("Билет не найден");

        if (ticket.Status != TicketStatus.Reserved)
        {
            throw new ValidationException("Билет не может быть куплен в текущем статусе");
        }

        var eventEntity = _eventRepository.GetById(ticket.EventId)
            ?? throw new NotFoundException("Событие для билета не найдено");

        var seat = _seatRepository.GetById(ticket.SeatId)
            ?? throw new NotFoundException("Место для билета не найдено");

        seat.Status = SeatStatus.Sold;
        _seatRepository.Update(seat);

        ticket.Status = TicketStatus.Purchased;
        _ticketRepository.Update(ticket);

        var payment = _paymentRepository.Add(new Payment
        {
            TicketId = ticket.Id,
            Amount = eventEntity.BasePrice,
            Status = PaymentStatus.Completed,
            CreatedAt = DateTime.UtcNow
        });

        return Task.FromResult(new BuyTicketResponseDto
        {
            TicketId = ticket.Id,
            PaymentId = payment.Id,
            Amount = payment.Amount,
            Status = payment.Status
        });
    }

    public Task<CancelTicketResponseDto> CancelTicket(CancelTicketRequestDto request)
    {
        if (request.TicketId <= 0)
        {
            throw new ValidationException("Не указан идентификатор билета");
        }

        var ticket = _ticketRepository.GetById(request.TicketId)
            ?? throw new NotFoundException("Билет не найден");

        if (ticket.Status == TicketStatus.Cancelled)
        {
            throw new ValidationException("Билет уже отменен");
        }

        var seat = _seatRepository.GetById(ticket.SeatId);
        if (seat is not null)
        {
            seat.Status = SeatStatus.Free;
            _seatRepository.Update(seat);
        }

        ticket.Status = TicketStatus.Cancelled;
        _ticketRepository.Update(ticket);

        return Task.FromResult(new CancelTicketResponseDto
        {
            TicketId = ticket.Id,
            Status = ticket.Status
        });
    }
}
```

Note this references `request.UseTransaction`, `request.SimulateErrorAfterTicketInsert` (added to `ReserveSeatRequestDto` in Task 4) and `GetSeatSnapshotRequestDto`/`GetSeatSnapshotResponseDto`/`ITicketSalesService.GetSeatSnapshot` (added in Task 4). **Implement Task 4's DTO/interface changes first, or alongside this file**, since this file won't compile without them– the two tasks are split by requirement (Часть 2 vs Часть 3), not by compile order.

- [ ] **Step 4: Concurrency test (same pattern as Lab 3's `ReserveSeatConcurrencyTests`, now against Postgres)**

```csharp
// tests/TicketSales.Tests/PostgresReserveSeatConcurrencyTests.cs
using System.Linq;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using Xunit;

namespace TicketSales.Tests;

public class PostgresReserveSeatConcurrencyTests : IClassFixture<PostgresTestServerFixture>
{
    private readonly PostgresTestServerFixture _fixture;

    public PostgresReserveSeatConcurrencyTests(PostgresTestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentReserveSeat_OnSameSeat_OnlyOneSucceeds()
    {
        const int seatId = 1;
        const int eventId = 1;
        const int clientsCount = 5;

        var tasks = Enumerable.Range(1, clientsCount).Select(async i =>
        {
            using var client = new NetworkClient();
            await client.ConnectAsync("127.0.0.1", _fixture.Port);
            return await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = eventId,
                SeatId = seatId,
                CustomerFullName = $"Postgres Concurrent Client {i}",
                CustomerEmail = $"pg-concurrent{i}@example.com"
            });
        });

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(1, responses.Count(r => r.Success));
        Assert.Equal(clientsCount - 1, responses.Count(r => !r.Success));
    }
}
```

This test relies on `ReserveSeatRequestDto` defaulting `UseTransaction = true`, `SimulateErrorAfterTicketInsert = false` (Task 4), so ordinary calls behave exactly like Lab 3's `ReserveSeat`– no test-only fields need setting here.

- [ ] **Step 5: Verify (after Task 4's DTO changes are in place too, since this file won't compile otherwise)**

Run: `docker compose up -d` (make sure Postgres is running)
Run: `dotnet test --filter PostgresReserveSeatConcurrencyTests`
Expected: PASS, and repeat 5x in a loop to confirm no flakiness (same check as Lab 3).

---

### Task 4: Composite transaction demo– DTOs, `GetSeatSnapshot`, and the partial-state/commit/rollback tests (Часть 3)

**Files:**
- Modify: `src/TicketSales.Shared/Dto/ReserveSeatRequestDto.cs` (add `UseTransaction`, `SimulateErrorAfterTicketInsert`)
- Create: `src/TicketSales.Shared/Dto/GetSeatSnapshotRequestDto.cs`
- Create: `src/TicketSales.Shared/Dto/GetSeatSnapshotResponseDto.cs`
- Modify: `src/TicketSales.Server/Domain/Services/ITicketSalesService.cs` (add `GetSeatSnapshot`)
- Create: `src/TicketSales.Server/Dispatching/Handlers/GetSeatSnapshotHandler.cs`
- Modify: `src/TicketSales.Server/Program.cs` (register `GetSeatSnapshotHandler`)
- Test: `tests/TicketSales.Tests/PostgresTransactionTests.cs`

**Interfaces:**
- Produces: `ReserveSeatRequestDto.UseTransaction` (bool, default `true`), `ReserveSeatRequestDto.SimulateErrorAfterTicketInsert` (bool, default `false`); `GetSeatSnapshotRequestDto { int SeatId }`; `GetSeatSnapshotResponseDto { SeatStatus Status, int TicketCount }`; `ITicketSalesService.GetSeatSnapshot(GetSeatSnapshotRequestDto) -> Task<GetSeatSnapshotResponseDto>` (implemented in Task 3's `TicketSalesServicePostgres`).

- [ ] **Step 1: Extend `ReserveSeatRequestDto`**

```csharp
// src/TicketSales.Shared/Dto/ReserveSeatRequestDto.cs
namespace TicketSales.Shared.Dto;

public class ReserveSeatRequestDto
{
    public int EventId { get; set; }
    public int SeatId { get; set; }
    public string CustomerFullName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public bool UseTransaction { get; set; } = true;
    public bool SimulateErrorAfterTicketInsert { get; set; } = false;
}
```

Defaults preserve existing behavior for every caller that doesn't set these fields (the real `TicketSales.Client`, `RaceDemo`, `SemaphoreDemo`, and all Lab 3 tests never set them).

- [ ] **Step 2: `GetSeatSnapshot` DTOs + interface + handler**

```csharp
// src/TicketSales.Shared/Dto/GetSeatSnapshotRequestDto.cs
namespace TicketSales.Shared.Dto;

public class GetSeatSnapshotRequestDto
{
    public int SeatId { get; set; }
}
```

```csharp
// src/TicketSales.Shared/Dto/GetSeatSnapshotResponseDto.cs
using TicketSales.Shared.Models;

namespace TicketSales.Shared.Dto;

public class GetSeatSnapshotResponseDto
{
    public SeatStatus Status { get; set; }
    public int TicketCount { get; set; }
}
```

In `ITicketSalesService.cs`, add:
```csharp
Task<GetSeatSnapshotResponseDto> GetSeatSnapshot(GetSeatSnapshotRequestDto request);
```

```csharp
// src/TicketSales.Server/Dispatching/Handlers/GetSeatSnapshotHandler.cs
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Exceptions;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetSeatSnapshotHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public GetSeatSnapshotHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "GetSeatSnapshot";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        var request = payload.Deserialize<GetSeatSnapshotRequestDto>(JsonDefaults.Options)
            ?? throw new ValidationException("Отсутствуют данные для получения состояния места");

        return await _service.GetSeatSnapshot(request);
    }
}
```

This satisfies the "**`GetSeatSnapshot` for a nonexistent seat ID → `NOT_FOUND`**" item from Review Focus: `TicketSalesServicePostgres.GetSeatSnapshot` (Task 3) already throws `NotFoundException` when `_seatRepository.GetById` returns null, and `OperationDispatcher` already converts any `DomainException` to `Response.Fail` with that exception's `Code`– no new dispatcher logic needed.

**Register the handler** in `Program.cs`'s `IOperationHandler[] handlers` array (add `new GetSeatSnapshotHandler(service),`)– needed for the manual/production server, and it's already referenced by `PostgresTestServerFixture` (Task 3, Step 2).

- [ ] **Step 3: The three transaction-behavior tests**

```csharp
// tests/TicketSales.Tests/PostgresTransactionTests.cs
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Models;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class PostgresTransactionTests : IClassFixture<PostgresTestServerFixture>
{
    private readonly PostgresTestServerFixture _fixture;

    public PostgresTransactionTests(PostgresTestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task WithoutTransaction_SimulatedError_LeavesTicketCreatedButSeatStillFree()
    {
        const int seatId = 2; // event 1, seat 2– untouched by other tests sharing this fixture instance
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "No Tx Client",
            CustomerEmail = "no-tx@example.com",
            UseTransaction = false,
            SimulateErrorAfterTicketInsert = true
        });

        Assert.False(response.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        Assert.True(snapshotResponse.Success);
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        // The bug this reproduces: Operation 1 (create Ticket) already committed on its own
        // before the simulated error, so a ticket row exists– but Operation 2 (update Seat)
        // never ran, so the seat is still Free. This is the "Create Order -> SUCCESS,
        // Update Stock -> ERROR" partial-state scenario from lab4.md Часть 3 Требование 2.
        Assert.Equal(SeatStatus.Free, snapshot!.Status);
        Assert.Equal(1, snapshot.TicketCount);
    }

    [Fact]
    public async Task WithTransaction_NoError_CommitsBothChanges()
    {
        const int seatId = 3;
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Tx Commit Client",
            CustomerEmail = "tx-commit@example.com",
            UseTransaction = true,
            SimulateErrorAfterTicketInsert = false
        });

        Assert.True(response.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        Assert.Equal(SeatStatus.Reserved, snapshot!.Status);
        Assert.Equal(1, snapshot.TicketCount);
    }

    [Fact]
    public async Task WithTransaction_SimulatedError_RollsBackBothChanges()
    {
        const int seatId = 4;
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Tx Rollback Client",
            CustomerEmail = "tx-rollback@example.com",
            UseTransaction = true,
            SimulateErrorAfterTicketInsert = true
        });

        Assert.False(response.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        // Unlike the no-transaction case above, the ticket insert is rolled back too–
        // no partial state at all.
        Assert.Equal(SeatStatus.Free, snapshot!.Status);
        Assert.Equal(0, snapshot.TicketCount);
    }

    [Fact]
    public async Task GetSeatSnapshot_ForMissingSeat_ReturnsNotFound()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var response = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = 999_999 });

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.NotFound, response.Error?.Code);
    }

    [Fact]
    public async Task PartialFailureRun_SeatStaysReservableExactlyOnce()
    {
        const int seatId = 5;
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        // First: a no-transaction run with a simulated error leaves the seat Free but an orphan ticket exists.
        var first = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Orphan Ticket Client",
            CustomerEmail = "orphan@example.com",
            UseTransaction = false,
            SimulateErrorAfterTicketInsert = true
        });
        Assert.False(first.Success);

        // Second: a normal (transactional, no error) reservation on the same still-Free seat must succeed exactly once.
        var second = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
        {
            EventId = 1,
            SeatId = seatId,
            CustomerFullName = "Real Client",
            CustomerEmail = "real-client@example.com"
        });
        Assert.True(second.Success);

        var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
        var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);

        Assert.Equal(SeatStatus.Reserved, snapshot!.Status);
        Assert.Equal(2, snapshot.TicketCount); // the orphaned ticket from the failed run + the real one
    }
}
```

- [ ] **Step 4: Run all the new tests**

Run: `docker compose up -d && dotnet test --filter "PostgresTransactionTests|PostgresReserveSeatConcurrencyTests"`
Expected: all PASS.

Run: `dotnet test` (full suite, including the untouched Lab 2/3 in-memory tests)
Expected: all PASS– proves Lab 2/3 deliverables are unaffected.

---

### Task 5: `TicketSales.TransactionDemo` console project (real captured evidence for Часть 3/4)

**Files:**
- Create: `src/TicketSales.TransactionDemo/TicketSales.TransactionDemo.csproj`
- Create: `src/TicketSales.TransactionDemo/Program.cs`
- Modify: `TicketSales.sln` (add the new project)

**Interfaces:**
- Consumes: `PostgresConnectionFactory`, `PostgresSchemaInitializer.ResetAndSeed`, `Postgres*Repository`, `TicketSalesServicePostgres`, `GetSeatSnapshotHandler`, `ReserveSeatRequestDto.UseTransaction`/`SimulateErrorAfterTicketInsert`, `GetSeatSnapshotRequestDto`/`GetSeatSnapshotResponseDto` (Tasks 2-4).

- [ ] **Step 1: Project file**

```xml
<!-- src/TicketSales.TransactionDemo/TicketSales.TransactionDemo.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>TicketSales.TransactionDemo</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TicketSales.Shared\TicketSales.Shared.csproj" />
    <ProjectReference Include="..\TicketSales.Server\TicketSales.Server.csproj" />
    <ProjectReference Include="..\TicketSales.Client\TicketSales.Client.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: `Program.cs`– reset DB, host an in-process server, run 4 experiments on 4 fresh seats**

```csharp
using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.Postgres;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;

Console.OutputEncoding = Encoding.UTF8;

const string connectionString = "Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales";
var connectionFactory = new PostgresConnectionFactory(connectionString);

Console.WriteLine("Сброс и повторное заполнение базы данных для чистого эксперимента...");
PostgresSchemaInitializer.ResetAndSeed(connectionFactory);

var eventRepository = new PostgresEventRepository(connectionFactory);
var seatRepository = new PostgresSeatRepository(connectionFactory);
var ticketRepository = new PostgresTicketRepository(connectionFactory);
var customerRepository = new PostgresCustomerRepository(connectionFactory);
var paymentRepository = new PostgresPaymentRepository(connectionFactory);

var service = new TicketSalesServicePostgres(
    eventRepository, seatRepository, ticketRepository, customerRepository, paymentRepository, connectionFactory);

IOperationHandler[] handlers =
{
    new GetEventsHandler(service),
    new ReserveSeatHandler(service),
    new BuyTicketHandler(service),
    new CancelTicketHandler(service),
    new GetServerTimeHandler(service),
    new GetSeatSnapshotHandler(service)
};

var dispatcher = new OperationDispatcher(handlers);
var server = new TcpServer(IPAddress.Loopback, 0, dispatcher);
using var cts = new CancellationTokenSource();
var serverTask = server.RunAsync(cts.Token);

Console.WriteLine($"Сервер запущен на 127.0.0.1:{server.Port}");
Console.WriteLine();

await RunExperiment("Эксперимент 1: без транзакции, без ошибки", seatId: 1, useTransaction: false, simulateError: false);
await RunExperiment("Эксперимент 2: без транзакции, с искусственной ошибкой", seatId: 2, useTransaction: false, simulateError: true);
await RunExperiment("Эксперимент 3: с транзакцией, без ошибки (COMMIT)", seatId: 3, useTransaction: true, simulateError: false);
await RunExperiment("Эксперимент 4: с транзакцией, с искусственной ошибкой (ROLLBACK)", seatId: 4, useTransaction: true, simulateError: true);

cts.Cancel();
try { await serverTask; } catch { }

async Task RunExperiment(string title, int seatId, bool useTransaction, bool simulateError)
{
    Console.WriteLine($"=== {title} ===");

    using var client = new NetworkClient();
    await client.ConnectAsync("127.0.0.1", server.Port);

    var reserveResponse = await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
    {
        EventId = 1,
        SeatId = seatId,
        CustomerFullName = $"Demo Client (seat {seatId})",
        CustomerEmail = $"demo-seat-{seatId}@example.com",
        UseTransaction = useTransaction,
        SimulateErrorAfterTicketInsert = simulateError
    });

    Console.WriteLine(reserveResponse.Success
        ? $"ReserveSeat: SUCCESS, ticketId={reserveResponse.Data!.Value.Deserialize<ReserveSeatResponseDto>(JsonDefaults.Options)?.TicketId}"
        : $"ReserveSeat: FAIL ({reserveResponse.Error?.Message})");

    var snapshotResponse = await client.SendAsync("GetSeatSnapshot", new GetSeatSnapshotRequestDto { SeatId = seatId });
    var snapshot = snapshotResponse.Data!.Value.Deserialize<GetSeatSnapshotResponseDto>(JsonDefaults.Options);
    Console.WriteLine($"Состояние БД после эксперимента: seat.Status={snapshot?.Status}, ticketCount={snapshot?.TicketCount}");
    Console.WriteLine();
}
```

- [ ] **Step 3: Add to solution and run for real**

```bash
dotnet sln TicketSales.sln add src/TicketSales.TransactionDemo/TicketSales.TransactionDemo.csproj
docker compose up -d
dotnet run --project src/TicketSales.TransactionDemo
```

Expected console shape (capture the **real** output– with real ticket ids– for Task 6's docs, don't hand-write this):
```
=== Эксперимент 1: без транзакции, без ошибки ===
ReserveSeat: SUCCESS, ticketId=...
Состояние БД после эксперимента: seat.Status=Reserved, ticketCount=1

=== Эксперимент 2: без транзакции, с искусственной ошибкой ===
ReserveSeat: FAIL (Искусственная ошибка между операциями (демонстрация транзакции))
Состояние БД после эксперимента: seat.Status=Free, ticketCount=1

=== Эксперимент 3: с транзакцией, без ошибки (COMMIT) ===
ReserveSeat: SUCCESS, ticketId=...
Состояние БД после эксперимента: seat.Status=Reserved, ticketCount=1

=== Эксперимент 4: с транзакцией, с искусственной ошибкой (ROLLBACK) ===
ReserveSeat: FAIL (Искусственная ошибка между операциями (демонстрация транзакции))
Состояние БД после эксперимента: seat.Status=Free, ticketCount=0
```

Experiment 2's `ticketCount=1` (not 0) with `seat.Status=Free` is exactly the "partial state" bug lab4.md Часть 3 Требование 2 asks you to observe and reason about; Experiment 4 shows the same fault fully rolled back.

---

### Task 6: Documentation (`DATABASE.md`) with real captured evidence, README updates

**Files:**
- Create: `DATABASE.md`
- Modify: `README.md`

- [ ] **Step 1: Capture the remaining real evidence needed for the doc**

Run the Task 3 concurrency test again, capturing full server log output (same style as `SYNCHRONIZATION.md`'s captured `RaceDemo` log):
```bash
docker compose up -d
dotnet test --filter PostgresReserveSeatConcurrencyTests --logger "console;verbosity=detailed" 2>&1 | tee /tmp/pg_concurrency_test.log
```
(Server-side `Log(...)` output from `TicketSalesServicePostgres.ReserveSeat` goes to the test host's console– capture the `LOCK`/`проверка статуса`/`SUCCESS`/`FAIL`/`UNLOCK` lines for the doc, same as Lab 3's evidence.)

- [ ] **Step 2: Write `DATABASE.md`**

Structure, in order, answering every numbered defense point from lab4.md's "Защита лабораторной работы" list:
1. **СУБД и обоснование**– PostgreSQL 17 (`postgres:17-alpine` via Docker), chosen because: zero local install (just `docker compose up -d`), full transaction/ACID support (required for Часть 3), the team already has the image pulled.
2. **Структура БД**– the 5 tables from Task 2's schema (`events`, `seats`, `customers`, `tickets`, `payments`), their PKs/FKs/constraints (`seats` has `UNIQUE (event_id, row, number)`, `customers.email` is `UNIQUE`), as plain text (no ERD image needed– the `CREATE TABLE` SQL from `PostgresSchemaInitializer` IS the structure, quote it).
3. **Архитектура Client → Server → Database**– reference `TicketSalesServicePostgres`/`Postgres*Repository`; the client only ever talks to the server over TCP (`NetworkClient`/`JsonLineProtocol`), never touches Npgsql– quote the constructor of `NetworkClient` to show it has no DB dependency at all.
4. **Работающие операции с БД**– `GetEvents` (read), `ReserveSeat`/`BuyTicket`/`CancelTicket` (write), reusing Task 2 Step 4's manual smoke-test transcript.
5-9 (параллельная работа, общий ресурс, критическая секция, lock, результат конкурентного обращения)– reuse Task 6 Step 1's captured log, same narrative structure as `SYNCHRONIZATION.md`'s Часть 1 section (this doc can literally say "тот же общий ресурс и тот же принцип критической секции, что и в ЛР №3 (см. SYNCHRONIZATION.md); отличие– теперь пишем в Postgres, а не в словарь в памяти").
10-18 (составная операция, без/с транзакцией, ошибка, COMMIT, ROLLBACK, состояния БД)– reuse Task 5's captured `TransactionDemo` transcript verbatim, plus fill in:

**Таблица (Часть 4, Требование 3– конкурентный доступ):**

| Механизм | Клиент | Время отправки | Вход в критическую секцию | Выход | Результат |
| --- | --- | --- | --- | --- | --- |
| Без синхронизации | Client 1 | *(см. CONCURRENCY.md, ЛР №2– тот же эксперимент)* |– |– | SUCCESS |
| Без синхронизации | Client 2 | |– |– | SUCCESS (Race Condition) |
| lock | Client 1 | *(из captured лога Task 6 Step 1)* | | | |
| lock | Client 2 | | | | |

**Таблица (Часть 4, Требование 4– транзакция):**

| Эксперимент | Транзакция | Операция 1 | Операция 2 | Ошибка | Состояние БД |
| --- | --- | --- | --- | --- | --- |
| 1 | Нет | выполнена | выполнена | Нет | изменения сохранены |
| 2 | Нет | выполнена | не выполнена | Да | **частично изменено** (ticket создан, seat всё ещё Free) |
| 3 | Да | выполнена | выполнена | Нет | изменения сохранены (COMMIT) |
| 4 | Да | выполнена | не выполнена | Да | **изменений нет** (ROLLBACK, ни ticket ни seat не изменились) |

**Таблица сравнения (Часть 4, Требование 5):** answer all 8 rows plainly from what the code/tests just proved (e.g. "Атомарность: без транзакции– нет (Operation 1 и 2 независимы), с транзакцией– да (обе или ни одной)"; "Использование ROLLBACK: без транзакции– невозможно (нечего откатывать, Operation 1 уже закоммичена), с транзакцией– да").

**Таблица итогового эксперимента (Часть 4, Требование 6):** fill "Полученный результат" column from the actual test run output (all "Ожидаемый результат" cells were achieved– cite the passing test names).

19-21 (заполненные таблицы, unit-тесты, обработка ошибок)– list the 5 new test files/methods from Tasks 3-4 by name, and describe the Task 1 Step 6 fail-fast manual check as the "сервер недоступен"/"соединение с БД отсутствует" error-handling evidence, plus note that `OperationDispatcher`'s existing generic `catch (Exception)` (unchanged since Lab 2) already turns any unexpected Npgsql exception into a graceful `INTERNAL_ERROR` response rather than crashing the server process.

- [ ] **Step 3: `README.md` updates**

```markdown
## База данных (ЛР №4)

```bash
docker compose up -d          # поднимает PostgreSQL 17 на localhost:5433
dotnet run --project src/TicketSales.Server -- 5050
dotnet run --project src/TicketSales.Client
```

Схема создаётся и заполняется автоматически при первом запуске сервера
(`PostgresSchemaInitializer.EnsureCreatedAndSeeded`). Подробности–
[DATABASE.md](DATABASE.md).

## Демонстрация транзакций (ЛР №4, Часть 3)

```bash
docker compose up -d
dotnet run --project src/TicketSales.TransactionDemo
```

Сбрасывает БД и прогоняет 4 эксперимента (без транзакции/с транзакцией ×
без ошибки/с искусственной ошибкой), показывая COMMIT и ROLLBACK. Реальный
вывод– в [DATABASE.md](DATABASE.md).
```

---

### Task 7: Final verification and single Lab 4 commit

- [ ] **Step 1: Full clean build**

Run: `docker compose up -d && dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Full test suite, twice**

Run: `dotnet test && dotnet test`
Expected: all tests PASS both times (Lab 2/3 in-memory tests + all new Postgres tests).

- [ ] **Step 3: Review the diff, scoped to the lab directory**

Run: `git status` and `git diff` (only under `Claude_tech/lab1/TicketSales/`, same scoping discipline as the Lab 2 and Lab 3 commits– do not stage sibling unrelated directories in this monorepo, and do not stage the `docs/superpowers/plans/` planning file).

- [ ] **Step 4: Single commit for all of Lab 4**

```bash
git add README.md DATABASE.md docker-compose.yml TicketSales.sln src/ tests/
git commit -m "$(cat <<'EOF'
Lab №4: PostgreSQL persistence, lock-protected reservation, transactions

- Add docker-compose.yml (postgres:17-alpine on host port 5433) and a
  raw-ADO.NET (Npgsql) Storage/Postgres/* implementation of the existing
  repository interfaces; Server/Program.cs now runs against Postgres
  instead of the in-memory Lab 2/3 repositories (which stay untouched,
  still backing RaceDemo/SemaphoreDemo).
- TicketSalesServicePostgres.ReserveSeat keeps Lab 3's lock-protected
  critical section, now guarding two real writes (INSERT ticket, UPDATE
  seat) issued directly against one NpgsqlConnection/NpgsqlTransaction,
  so the same operation can be run with or without an explicit
  transaction and with a simulated fault between the two writes
  (ReserveSeatRequestDto.UseTransaction/SimulateErrorAfterTicketInsert).
- Add GetSeatSnapshot operation (seat status + ticket count) so the real
  Client -> Server -> Database path can prove partial-state vs
  COMMIT vs ROLLBACK outcomes without touching the DB directly.
- Add TicketSales.TransactionDemo project: resets the DB and runs the
  4 required experiments end to end.
- DATABASE.md documents the schema, the architecture, and captured
  real output for every required table and defense point.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
EOF
)"
git log --oneline -6
```

## Self-Review Notes (already applied above)

- **Spec coverage:** Часть 1 Требования 1-5 → Tasks 1-2. Часть 2 Требования 1-5 → Task 3. Часть 3 Требования 1-6 → Tasks 3-5. Часть 4 Требования 1-6 (таблицы) → Task 6; Требование 7 (unit-тесты) → Tasks 3-4 (5 tests: concurrency, partial-state, commit, rollback, missing-seat NOT_FOUND, plus the reservable-again check); Требование 8 (ошибки) → Task 1 Step 5/6 (fail-fast) + existing `OperationDispatcher` catch-all (documented, not re-implemented) + `GetSeatSnapshot`'s `NOT_FOUND` test; Требование 9 (программа) → Tasks 2-5 collectively.
- **No placeholders:** every step has real, compilable code (no TODOs).
- **Type consistency:** `ReserveSeatRequestDto.UseTransaction`/`SimulateErrorAfterTicketInsert` (Task 4) match their usage in `TicketSalesServicePostgres.ReserveSeat` (Task 3) and in every test (Tasks 3-5). `GetSeatSnapshotResponseDto.Status`/`TicketCount` (Task 4) match their usage in `TicketSalesServicePostgres.GetSeatSnapshot` (Task 3) and every test/demo that deserializes it (Tasks 3-5). `PostgresConnectionFactory.Open()` (Task 1) is the one method every repository and the service use– signature never changes across tasks.
- **Review Focus coverage:** DB-unreachable-at-startup → Task 1 Step 5/6. Nonexistent-seat `NotFoundException` → `PostgresSeatRepository.GetById` (Task 2) returning `null` on no row, feeding the existing `?? throw new NotFoundException(...)` pattern already used in `TicketSalesServicePostgres.ReserveSeat` (Task 3)– no special-casing needed, `reader.Read()` returning `false` is the null path. Mixed same/different-seat concurrency smoke check → Task 3 Step 4's test only exercises same-seat (the stated scenario); cross-seat throughput isn't a hard requirement, left as-is as reasoned in Review Focus. Partial-failure-then-reservable-again → Task 4's `PartialFailureRun_SeatStaysReservableExactlyOnce` test. `GetSeatSnapshot` on a missing seat → Task 4's `GetSeatSnapshot_ForMissingSeat_ReturnsNotFound` test.
