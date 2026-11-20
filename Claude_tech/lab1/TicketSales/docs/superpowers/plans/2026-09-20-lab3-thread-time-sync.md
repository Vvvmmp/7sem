# Lab 3: Thread Synchronization + Client/Server Time Sync– Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the Race Condition demonstrated in Lab 2 (`TicketSalesService.ReserveSeat`) with `lock`, add a `SemaphoreSlim` alternative in a second project for comparison, and implement client/server clock-offset simulation + RTT-aware time synchronization, all with automated tests.

**Architecture:** Keep the existing layering (Networking → Dispatching → Domain.Services → Storage). The critical section lives inside `TicketSalesService` (the shared resource is `Seat.Status`, same as Lab 2). Time sync is a pure, network-agnostic calculation (`TimeSynchronizer`) in the Client project, driven by a new `GetServerTime` operation on the existing Request/Response protocol– no protocol redesign needed.

**Tech Stack:** C# / .NET 10, xunit 2.9, `lock`/`Monitor`, `SemaphoreSlim`, TCP + newline-delimited JSON (existing `JsonLineProtocol`).

**Spec:** `lab3.md` (repo root next to this plan's project)– Часть 1 (Требования 1–11) and Часть 2 (Требования 1–11).

## Global Constraints

- Target framework for all new projects: `net10.0`, `ImplicitUsings`/`Nullable` enabled– copy the pattern from `src/TicketSales.RaceDemo/TicketSales.RaceDemo.csproj`.
- Do not change the wire format of `Request`/`Response` (`src/TicketSales.Shared/Protocol/Request.cs`, `Response.cs`)– only add new DTOs/operations, matching the existing `GetEvents`/`ReserveSeat` pattern.
- Every `IOperationHandler[]` array is currently duplicated in 3 places and must be kept in sync when adding a handler: `src/TicketSales.Server/Program.cs`, `src/TicketSales.RaceDemo/Program.cs`, `tests/TicketSales.Tests/TestServerFixture.cs`.
- Logging convention already established in Lab 2 (`src/TicketSales.Server/Networking/ClientSession.cs:83-87`, `src/TicketSales.Server/Domain/Services/TicketSalesService.cs:135-139`): `[HH:mm:ss.fff] [Thread N] message`. Reuse/extend this, don't invent a new format.
- Only one final commit for all of Lab 3 (per user instruction)– do not commit after each task; commit once everything below is done and `dotnet build && dotnet test` pass.

---

### Task 1: `GetServerTime` operation (server + protocol plumbing)

**Files:**
- Create: `src/TicketSales.Shared/Dto/GetServerTimeRequestDto.cs`
- Create: `src/TicketSales.Shared/Dto/GetServerTimeResponseDto.cs`
- Create: `src/TicketSales.Server/Dispatching/Handlers/GetServerTimeHandler.cs`
- Modify: `src/TicketSales.Server/Domain/Services/ITicketSalesService.cs`
- Modify: `src/TicketSales.Server/Domain/Services/TicketSalesService.cs`
- Modify: `src/TicketSales.Server/Program.cs` (handlers array + optional delay arg)
- Modify: `src/TicketSales.RaceDemo/Program.cs` (handlers array)
- Modify: `tests/TicketSales.Tests/TestServerFixture.cs` (handlers array)
- Test: `tests/TicketSales.Tests/GetServerTimeTests.cs`

**Interfaces:**
- Produces: `GetServerTimeResponseDto { DateTime ServerTimeUtc }`, `ITicketSalesService.GetServerTime()` returning `Task<GetServerTimeResponseDto>`, operation name `"GetServerTime"`.

- [ ] **Step 1: Add the DTOs**

```csharp
// src/TicketSales.Shared/Dto/GetServerTimeRequestDto.cs
namespace TicketSales.Shared.Dto;

public class GetServerTimeRequestDto
{
}
```

```csharp
// src/TicketSales.Shared/Dto/GetServerTimeResponseDto.cs
using System;

namespace TicketSales.Shared.Dto;

public class GetServerTimeResponseDto
{
    public DateTime ServerTimeUtc { get; set; }
}
```

- [ ] **Step 2: Add `GetServerTime` to the service interface and implementation**

In `ITicketSalesService.cs`, add:

```csharp
Task<GetServerTimeResponseDto> GetServerTime();
```

In `TicketSalesService.cs`, add a field `private readonly TimeSpan _serverTimeArtificialDelay;`, accept it as an optional constructor parameter (`TimeSpan serverTimeArtificialDelay = default`) appended after the existing 5 repository parameters, store it, and implement:

```csharp
public async Task<GetServerTimeResponseDto> GetServerTime()
{
    if (_serverTimeArtificialDelay > TimeSpan.Zero)
    {
        await Task.Delay(_serverTimeArtificialDelay);
    }

    return new GetServerTimeResponseDto { ServerTimeUtc = DateTime.UtcNow };
}
```

This is Требование 7 (искусственная задержка перед ответом) made configurable instead of hardcoded, defaulting to `TimeSpan.Zero` so existing call sites (tests, RaceDemo) that don't pass it are unaffected.

- [ ] **Step 3: Add the handler**

```csharp
// src/TicketSales.Server/Dispatching/Handlers/GetServerTimeHandler.cs
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Server.Domain.Services;
using TicketSales.Shared.Protocol;

namespace TicketSales.Server.Dispatching.Handlers;

public class GetServerTimeHandler : IOperationHandler
{
    private readonly ITicketSalesService _service;

    public GetServerTimeHandler(ITicketSalesService service)
    {
        _service = service;
    }

    public string OperationName => "GetServerTime";

    public async Task<object> HandleAsync(JsonElement payload)
    {
        return await _service.GetServerTime();
    }
}
```

- [ ] **Step 4: Register the handler in all three handler arrays**

In `src/TicketSales.Server/Program.cs`, add `new GetServerTimeHandler(service),` to the `IOperationHandler[] handlers` array. Also read an optional second CLI arg for the artificial delay and pass it to `TicketSalesService`:

```csharp
var serverTimeDelayMs = 0;
if (args.Length > 1 && int.TryParse(args[1], out var parsedDelay))
{
    serverTimeDelayMs = parsedDelay;
}
```

and construct the service as:

```csharp
var service = new TicketSalesService(
    eventRepository,
    seatRepository,
    ticketRepository,
    customerRepository,
    paymentRepository,
    TimeSpan.FromMilliseconds(serverTimeDelayMs));
```

In `src/TicketSales.RaceDemo/Program.cs` and `tests/TicketSales.Tests/TestServerFixture.cs`, just add `new GetServerTimeHandler(service),` to their handler arrays (no delay needed there, default `TimeSpan.Zero` applies).

- [ ] **Step 5: Write the integration test**

```csharp
// tests/TicketSales.Tests/GetServerTimeTests.cs
using System;
using System.Text.Json;
using TicketSales.Client;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class GetServerTimeTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public GetServerTimeTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async System.Threading.Tasks.Task GetServerTime_ReturnsCurrentUtcTime()
    {
        using var client = new NetworkClient();
        await client.ConnectAsync("127.0.0.1", _fixture.Port);

        var before = DateTime.UtcNow;
        var response = await client.SendAsync("GetServerTime", new GetServerTimeRequestDto());
        var after = DateTime.UtcNow;

        Assert.True(response.Success);
        var result = response.Data!.Value.Deserialize<GetServerTimeResponseDto>(JsonDefaults.Options);
        Assert.NotNull(result);
        Assert.InRange(result!.ServerTimeUtc, before.AddSeconds(-1), after.AddSeconds(1));
    }
}
```

- [ ] **Step 6: Build and run this test**

Run: `dotnet test --filter GetServerTimeTests`
Expected: PASS

---

### Task 2: Fix the Race Condition with `lock`

**Files:**
- Modify: `src/TicketSales.Server/Domain/Services/TicketSalesService.cs`
- Test: `tests/TicketSales.Tests/ReserveSeatConcurrencyTests.cs`

**Interfaces:**
- Consumes: `ISeatRepository.GetById/Update`, `ITicketRepository.Add` (unchanged signatures).
- Produces: same `ReserveSeat`/`BuyTicket`/`CancelTicket` signatures– only internal locking changes, no public API change.

**Design decision (write this reasoning into `CONCURRENCY.md` in Task 6):** the shared resource is `Seat.Status`. The critical section is exactly "check `Status == Free`" through "write `Status = Reserved` + create the `Ticket`"– nothing else. Customer lookup/creation touches a *different* resource (`ICustomerRepository`) and doesn't need to be atomic with the seat write, so it stays **outside** the lock (Требование 4: "не следует помещать под блокировку весь метод"). The same single lock object also guards `BuyTicket` and `CancelTicket` because they mutate the same `Seat.Status` field.

- [ ] **Step 1: Write the failing concurrency test**

```csharp
// tests/TicketSales.Tests/ReserveSeatConcurrencyTests.cs
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Storage.InMemory;
using TicketSales.Shared.Dto;
using TicketSales.Shared.Protocol;
using Xunit;

namespace TicketSales.Tests;

public class ReserveSeatConcurrencyTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public ReserveSeatConcurrencyTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentReserveSeat_OnSameSeat_OnlyOneSucceeds()
    {
        const int seatId = SeedData.FirstEventFirstSeatId + 2; // untouched by other tests in this fixture
        const int eventId = SeedData.FirstEventId;
        const int clientsCount = 5;

        var tasks = Enumerable.Range(1, clientsCount).Select(async i =>
        {
            using var client = new NetworkClient();
            await client.ConnectAsync("127.0.0.1", _fixture.Port);
            return await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = eventId,
                SeatId = seatId,
                CustomerFullName = $"Concurrent Client {i}",
                CustomerEmail = $"concurrent{i}@example.com"
            });
        });

        var responses = await Task.WhenAll(tasks);

        Assert.Equal(1, responses.Count(r => r.Success));
        Assert.Equal(clientsCount - 1, responses.Count(r => !r.Success));
    }
}
```

Note: pick a `seatId` not used by other test classes sharing `TestServerFixture` (check `SuccessfulScenarioTests`, `InvalidDataTests` etc. for which seat ids they already reserve– offset by an unused one, e.g. `SeedData.FirstEventFirstSeatId + 2`, and verify against `SeedData.Populate` which creates seat ids 1–10 for event 1).

- [ ] **Step 2: Run it to confirm it currently fails (or flakily passes) without the fix**

Run: `dotnet test --filter ReserveSeatConcurrencyTests`
Expected: FAIL or flaky (multiple `Success = true` responses), proving the race still exists before the fix.

- [ ] **Step 3: Add the lock object and wrap the critical section**

In `TicketSalesService.cs`, add a field:

```csharp
private readonly object _seatLock = new();
```

Rewrite `ReserveSeat` so the customer lookup happens before the lock, and the check+write happens inside it:

```csharp
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

    var seat = _seatRepository.GetById(request.SeatId)
        ?? throw new NotFoundException("Место не найдено");

    if (seat.EventId != eventEntity.Id)
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

    Ticket ticket;
    lock (_seatLock)
    {
        Log($"ReserveSeat: seat={seat.Id} LOCK");
        Log($"ReserveSeat: seat={seat.Id} проверка статуса -> {seat.Status}");

        if (_reservationDelay > TimeSpan.Zero)
        {
            Thread.Sleep(_reservationDelay);
        }

        if (seat.Status != SeatStatus.Free)
        {
            Log($"ReserveSeat: seat={seat.Id} FAIL (уже занято)");
            Log($"ReserveSeat: seat={seat.Id} UNLOCK");
            throw new ValidationException("Место уже занято");
        }

        seat.Status = SeatStatus.Reserved;
        _seatRepository.Update(seat);

        ticket = _ticketRepository.Add(new Ticket
        {
            EventId = eventEntity.Id,
            SeatId = seat.Id,
            CustomerId = customer.Id,
            Status = TicketStatus.Reserved,
            CreatedAt = DateTime.UtcNow
        });

        Log($"ReserveSeat: seat={seat.Id} SUCCESS (customer={customer.Email}, ticketId={ticket.Id})");
        Log($"ReserveSeat: seat={seat.Id} UNLOCK");
    }

    return Task.FromResult(new ReserveSeatResponseDto
    {
        TicketId = ticket.Id,
        Seat = new SeatDto
        {
            Id = seat.Id,
            Row = seat.Row,
            Number = seat.Number,
            Status = seat.Status
        }
    });
}
```

Add a field `private readonly TimeSpan _reservationDelay;` set from a new optional constructor parameter `TimeSpan reservationDelay = default` (appended after `serverTimeArtificialDelay`), used only for the manual two-terminal demonstration in Требование 7/Часть 1 (documented in Task 6). Add `using System.Threading;` for `Thread.Sleep`.

Apply the same `lock (_seatLock) { ... }` treatment to the shared-state part of `BuyTicket` (the `seat.Status = SeatStatus.Sold; _seatRepository.Update(seat); ticket.Status = ...` block) and `CancelTicket` (the `seat.Status = SeatStatus.Free` block)– same lock object, same log pattern, keeping validation/lookups outside the lock.

- [ ] **Step 4: Run the concurrency test again**

Run: `dotnet test --filter ReserveSeatConcurrencyTests`
Expected: PASS, deterministically (run it 10 times in a loop to confirm no flakiness: `for i in 1 2 3 4 5 6 7 8 9 10; do dotnet test --filter ReserveSeatConcurrencyTests || break; done`)

- [ ] **Step 5: Run the full existing test suite to check nothing regressed**

Run: `dotnet test`
Expected: all PASS (including `SuccessfulScenarioTests`, `InvalidDataTests`, `UnknownOperationTests`, `ServerUnavailableTests`).

---

### Task 3: `SemaphoreSlim` alternative in a second project (Требование 9)

**Files:**
- Create: `src/TicketSales.Server/Domain/Services/TicketSalesServiceSemaphore.cs`
- Create: `src/TicketSales.SemaphoreDemo/TicketSales.SemaphoreDemo.csproj`
- Create: `src/TicketSales.SemaphoreDemo/Program.cs`
- Modify: `TicketSales.sln` (add new project)
- Test: `tests/TicketSales.Tests/ReserveSeatSemaphoreConcurrencyTests.cs`

**Interfaces:**
- Produces: `TicketSalesServiceSemaphore : ITicketSalesService`– same public surface as `TicketSalesService`, so it's a drop-in replacement for handlers.

- [ ] **Step 1: Create the SemaphoreSlim-based service variant**

Copy `TicketSalesService.cs` to `TicketSalesServiceSemaphore.cs`, rename the class, replace the `object _seatLock` field with `private readonly SemaphoreSlim _seatSemaphore = new(1, 1);`, and replace every `lock (_seatLock) { ... }` block with:

```csharp
await _seatSemaphore.WaitAsync();
try
{
    // same body as the lock block, with Log(...) lines saying "WAIT"/"ACQUIRED"/"RELEASE" instead of LOCK/UNLOCK
}
finally
{
    _seatSemaphore.Release();
}
```

This requires `ReserveSeat`, `BuyTicket`, `CancelTicket` to actually `await` here, so change their return type usage from `Task.FromResult(...)` returns to plain `async Task<...>` methods (the method signatures in `ITicketSalesService` are unchanged– still `Task<...>`).

- [ ] **Step 2: Create the new console project**

```xml
<!-- src/TicketSales.SemaphoreDemo/TicketSales.SemaphoreDemo.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>TicketSales.SemaphoreDemo</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TicketSales.Shared\TicketSales.Shared.csproj" />
    <ProjectReference Include="..\TicketSales.Server\TicketSales.Server.csproj" />
    <ProjectReference Include="..\TicketSales.Client\TicketSales.Client.csproj" />
  </ItemGroup>

</Project>
```

`Program.cs` is a copy of `src/TicketSales.RaceDemo/Program.cs` with `TicketSalesService` replaced by `TicketSalesServiceSemaphore` in the wiring– everything else (5 concurrent clients reserving the same seat, printing the summary) is identical.

- [ ] **Step 3: Add the project to the solution**

Run: `dotnet sln TicketSales.sln add src/TicketSales.SemaphoreDemo/TicketSales.SemaphoreDemo.csproj`

- [ ] **Step 4: Write the concurrency test for the semaphore variant**

```csharp
// tests/TicketSales.Tests/ReserveSeatSemaphoreConcurrencyTests.cs
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TicketSales.Client;
using TicketSales.Server.Dispatching;
using TicketSales.Server.Dispatching.Handlers;
using TicketSales.Server.Domain.Services;
using TicketSales.Server.Networking;
using TicketSales.Server.Storage.InMemory;
using TicketSales.Shared.Dto;
using Xunit;

namespace TicketSales.Tests;

public class ReserveSeatSemaphoreConcurrencyTests
{
    [Fact]
    public async Task ConcurrentReserveSeat_WithSemaphoreService_OnlyOneSucceeds()
    {
        var eventRepository = new InMemoryEventRepository();
        var seatRepository = new InMemorySeatRepository();
        var ticketRepository = new InMemoryTicketRepository();
        var customerRepository = new InMemoryCustomerRepository();
        var paymentRepository = new InMemoryPaymentRepository();
        SeedData.Populate(eventRepository, seatRepository);

        var service = new TicketSalesServiceSemaphore(
            eventRepository, seatRepository, ticketRepository, customerRepository, paymentRepository);

        IOperationHandler[] handlers =
        {
            new GetEventsHandler(service),
            new ReserveSeatHandler(service),
            new BuyTicketHandler(service),
            new CancelTicketHandler(service),
            new GetServerTimeHandler(service)
        };
        var dispatcher = new OperationDispatcher(handlers);
        var server = new TcpServer(IPAddress.Loopback, 0, dispatcher);
        using var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        var tasks = Enumerable.Range(1, 5).Select(async i =>
        {
            using var client = new NetworkClient();
            await client.ConnectAsync("127.0.0.1", server.Port);
            return await client.SendAsync("ReserveSeat", new ReserveSeatRequestDto
            {
                EventId = SeedData.FirstEventId,
                SeatId = SeedData.FirstEventFirstSeatId,
                CustomerFullName = $"Semaphore Client {i}",
                CustomerEmail = $"semaphore{i}@example.com"
            });
        });

        var responses = await Task.WhenAll(tasks);

        cts.Cancel();
        try { await serverTask; } catch { }

        Assert.Equal(1, responses.Count(r => r.Success));
    }
}
```

- [ ] **Step 5: Run the new test and the full suite**

Run: `dotnet test --filter ReserveSeatSemaphoreConcurrencyTests`
Expected: PASS
Run: `dotnet build` (solution-wide, confirms `TicketSales.SemaphoreDemo` compiles)
Expected: Build succeeded, 0 errors

---

### Task 4: `TimeSynchronizer`– pure offset/RTT math + unit tests (Часть 2, Требования 3–6)

**Files:**
- Create: `src/TicketSales.Client/TimeSynchronizer.cs`
- Test: `tests/TicketSales.Tests/TimeSynchronizerTests.cs`

**Interfaces:**
- Produces:
  - `TimeSynchronizer.CalculateOffset(DateTime serverTimeUtc, DateTime requestSentAtClientClock, TimeSpan rtt) -> TimeSpan`
  - `TimeSynchronizer.ApplyOffset(DateTime clientClockNow, TimeSpan offset) -> DateTime`
  - `record TimeSyncResult(DateTime ServerTimeUtc, TimeSpan Rtt, TimeSpan Offset, DateTime CorrectedTimeUtc)`

- [ ] **Step 1: Write the failing unit tests (directly encode the lab's own worked examples)**

```csharp
// tests/TicketSales.Tests/TimeSynchronizerTests.cs
using System;
using TicketSales.Client;
using Xunit;

namespace TicketSales.Tests;

public class TimeSynchronizerTests
{
    [Fact]
    public void CalculateOffset_ClockAheadBySevenSeconds_ReturnsNegativeOffset()
    {
        // Требование 1 example: true time 10:00:00, client clock reads 10:00:07 (offset=+7s)
        var trueSendTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var clientClockOffset = TimeSpan.FromSeconds(7);
        var requestSentAtClientClock = trueSendTime + clientClockOffset; // 10:00:07
        var serverTimeUtc = trueSendTime; // server, unaffected, responds instantly: rtt = 0
        var rtt = TimeSpan.Zero;

        var offset = TimeSynchronizer.CalculateOffset(serverTimeUtc, requestSentAtClientClock, rtt);

        Assert.Equal(TimeSpan.FromSeconds(-7), offset);
    }

    [Fact]
    public void CalculateOffset_WithRtt_UsesMidpointOfRoundTrip()
    {
        // Требование 6 example: sent 10:00:00, server time 10:00:01, received 10:00:02 -> RTT=2s
        var requestSentAtClientClock = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var serverTimeUtc = new DateTime(2026, 1, 1, 10, 0, 1, DateTimeKind.Utc);
        var rtt = TimeSpan.FromSeconds(2);

        var offset = TimeSynchronizer.CalculateOffset(serverTimeUtc, requestSentAtClientClock, rtt);

        // midpoint (client clock) = 10:00:00 + 1s = 10:00:01 == serverTimeUtc -> offset 0
        Assert.Equal(TimeSpan.Zero, offset);
    }

    [Fact]
    public void ApplyOffset_AddsOffsetToLocalClock()
    {
        var localNow = new DateTime(2026, 1, 1, 10, 0, 7, DateTimeKind.Utc);
        var offset = TimeSpan.FromSeconds(-7);

        var corrected = TimeSynchronizer.ApplyOffset(localNow, offset);

        Assert.Equal(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), corrected);
    }
}
```

- [ ] **Step 2: Run to confirm it fails to compile (type doesn't exist yet)**

Run: `dotnet test --filter TimeSynchronizerTests`
Expected: FAIL (build error, `TimeSynchronizer` not found)

- [ ] **Step 3: Implement `TimeSynchronizer`**

```csharp
// src/TicketSales.Client/TimeSynchronizer.cs
using System;

namespace TicketSales.Client;

public static class TimeSynchronizer
{
    public static TimeSpan CalculateOffset(DateTime serverTimeUtc, DateTime requestSentAtClientClock, TimeSpan rtt)
    {
        var estimatedTrueTimeAtRequest = requestSentAtClientClock + TimeSpan.FromTicks(rtt.Ticks / 2);
        return serverTimeUtc - estimatedTrueTimeAtRequest;
    }

    public static DateTime ApplyOffset(DateTime clientClockNow, TimeSpan offset)
    {
        return clientClockNow + offset;
    }
}

public record TimeSyncResult(DateTime ServerTimeUtc, TimeSpan Rtt, TimeSpan Offset, DateTime CorrectedTimeUtc);
```

- [ ] **Step 4: Run the tests again**

Run: `dotnet test --filter TimeSynchronizerTests`
Expected: PASS (all 3 tests)

---

### Task 5: Client UI– clock offset simulation + time sync flow (Часть 2, Требования 1, 2, 10)

**Files:**
- Modify: `src/TicketSales.Client/Program.cs`

**Interfaces:**
- Consumes: `TimeSynchronizer.CalculateOffset`, `TimeSynchronizer.ApplyOffset` (Task 4), `NetworkClient.SendAsync` (existing), `GetServerTimeRequestDto`/`GetServerTimeResponseDto` (Task 1).

- [ ] **Step 1: Add a startup prompt for the simulated clock offset**

Right after the existing `port` prompt block and before `using var client = new NetworkClient();`, add:

```csharp
Console.Write("Смещение локальных часов клиента в секундах (для моделирования рассинхронизации) [0]: ");
var offsetInput = Console.ReadLine();
var clockOffsetSeconds = 0d;
if (!string.IsNullOrWhiteSpace(offsetInput) && double.TryParse(offsetInput.Trim(), out var parsedOffset))
{
    clockOffsetSeconds = parsedOffset;
}
var clockOffset = TimeSpan.FromSeconds(clockOffsetSeconds);
TimeSpan? syncedOffset = null;
```

- [ ] **Step 2: Add two new menu items and renumber Exit**

In `PrintMenu()`:

```csharp
static void PrintMenu()
{
    Console.WriteLine();
    Console.WriteLine("1. Список событий");
    Console.WriteLine("2. Забронировать место");
    Console.WriteLine("3. Купить билет");
    Console.WriteLine("4. Отменить билет");
    Console.WriteLine("5. Показать локальное время (с учетом смещения)");
    Console.WriteLine("6. Синхронизировать время с сервером");
    Console.WriteLine("7. Выход");
    Console.Write("Выберите пункт: ");
}
```

Update the `switch` in the main loop to add cases `"5"` and `"6"`, and move the exit case to `"7"`:

```csharp
case "5":
    HandleShowLocalTime(clockOffset);
    break;
case "6":
    syncedOffset = await HandleSynchronizeTime(client, clockOffset);
    break;
case "7":
    running = false;
    break;
```

- [ ] **Step 3: Implement `HandleShowLocalTime` and `HandleSynchronizeTime`**

```csharp
static void HandleShowLocalTime(TimeSpan clockOffset)
{
    var localTime = TimeSynchronizer.ApplyOffset(DateTime.UtcNow, clockOffset);
    Console.WriteLine($"Локальное время клиента (со смещением {clockOffset.TotalSeconds:+0;-0;0}s): {localTime:HH:mm:ss.fff}");
}

static async Task<TimeSpan> HandleSynchronizeTime(NetworkClient client, TimeSpan clockOffset)
{
    var requestSentAtClientClock = TimeSynchronizer.ApplyOffset(DateTime.UtcNow, clockOffset);
    var trueSendInstant = DateTime.UtcNow;

    var response = await client.SendAsync("GetServerTime", new GetServerTimeRequestDto());

    var trueReceiveInstant = DateTime.UtcNow;
    var rtt = trueReceiveInstant - trueSendInstant;

    if (!response.Success || response.Data is null)
    {
        Console.WriteLine($"Ошибка синхронизации времени: {response.Error?.Message}");
        return TimeSpan.Zero;
    }

    var result = response.Data.Value.Deserialize<GetServerTimeResponseDto>(JsonDefaults.Options);
    if (result is null)
    {
        Console.WriteLine("Ошибка обработки ответа сервера: пустые данные времени");
        return TimeSpan.Zero;
    }

    var offset = TimeSynchronizer.CalculateOffset(result.ServerTimeUtc, requestSentAtClientClock, rtt);
    var correctedNow = TimeSynchronizer.ApplyOffset(TimeSynchronizer.ApplyOffset(DateTime.UtcNow, clockOffset), offset);

    Console.WriteLine($"Локальное время клиента (до синхронизации): {requestSentAtClientClock:HH:mm:ss.fff}");
    Console.WriteLine($"Время сервера: {result.ServerTimeUtc:HH:mm:ss.fff}");
    Console.WriteLine($"RTT: {rtt.TotalMilliseconds:F0} ms (оценка задержки RTT/2 = {rtt.TotalMilliseconds / 2:F0} ms)");
    Console.WriteLine($"Вычисленная поправка (offset): {offset.TotalMilliseconds:F0} ms");
    Console.WriteLine($"Скорректированное время клиента: {correctedNow:HH:mm:ss.fff}");

    return offset;
}
```

Add `using TicketSales.Shared.Dto;` if not already present (it already is, per the existing top-level usings in `Program.cs`).

- [ ] **Step 4: Manual verification**

Run: `dotnet run --project src/TicketSales.Server -- 5050 1000` (1000ms artificial delay on `GetServerTime` so RTT is visible)
Run (separate terminal): `dotnet run --project src/TicketSales.Client`, enter offset `7`, choose `5` (see local time = real time + 7s), choose `6` (see Local/Server/RTT/Offset/Corrected time– corrected time should land close to real UTC time despite the +7s simulated skew and ~1s artificial delay).

Capture this real console output for Table 2 in the lab writeup (Task 6).

---

### Task 6: Documentation– update `CONCURRENCY.md`, README, fill lab tables with real captured runs

**Files:**
- Modify: `CONCURRENCY.md` (add a "Часть 2 / ЛР №3" section, or create `SYNCHRONIZATION.md` if `CONCURRENCY.md` gets too long– prefer one new file `SYNCHRONIZATION.md` since `CONCURRENCY.md`'s header explicitly says "синхронизация... не реализуется– это задача ЛР №3", so ЛР3 content belongs in its own doc)
- Create: `SYNCHRONIZATION.md`
- Modify: `README.md`

- [ ] **Step 1: Run the real experiments and capture output**

```bash
dotnet run --project src/TicketSales.RaceDemo        # after the lock fix: expect 1/5 success
dotnet run --project src/TicketSales.SemaphoreDemo   # semaphore variant: expect 1/5 success
```

Paste the actual terminal output (with real timestamps/thread ids) into `SYNCHRONIZATION.md` as evidence for Требование 7 (Часть 1) and Проверка 1.

- [ ] **Step 2: Write `SYNCHRONIZATION.md`**

Structure it to directly answer, in order: Требования 3 (race analysis: read/check/write locations, why interleaving was possible– link to the already-committed `CONCURRENCY.md` section 4), Требование 4-6 (critical section boundaries + the lock-placement pitfall from Требование 6, using the actual before/after code), Требование 9 (four questions per mechanism: lock vs SemaphoreSlim– what it synchronizes, cross-process capability, fit for this scenario, difference from lock), Таблица 1 filled with the real captured run above, then the time-sync section answering Часть 2 Требования 1-8 with the real console output from Task 5 Step 4, and Таблица 2 filled with those real numbers.

- [ ] **Step 3: Update `README.md`**

Add run instructions for `TicketSales.SemaphoreDemo` and the server's optional second CLI argument (artificial `GetServerTime` delay), e.g.:

```markdown
## Race Condition (устранена в ЛР №3, см. SYNCHRONIZATION.md)

```bash
dotnet run --project src/TicketSales.RaceDemo
dotnet run --project src/TicketSales.SemaphoreDemo
```

## Синхронизация времени

```bash
dotnet run --project src/TicketSales.Server -- 5050 1000
dotnet run --project src/TicketSales.Client
```
```

---

### Task 7: Final verification and single Lab 3 commit

- [ ] **Step 1: Full clean build**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings introduced by new code, 0 errors.

- [ ] **Step 2: Full test suite, twice (flakiness check for the concurrency tests)**

Run: `dotnet test && dotnet test`
Expected: all tests PASS both times.

- [ ] **Step 3: Review the diff**

Run: `git status` and `git diff` (scoped to `Claude_tech/lab1/TicketSales/`, do not touch sibling directories in the monorepo per the earlier Lab 2 commit's scoping).

- [ ] **Step 4: Single commit for all of Lab 3**

```bash
git add Claude_tech/lab1/TicketSales
git commit -m "$(cat <<'EOF'
Lab №3: synchronize seat reservation and add client/server time sync

- Fix the Lab 2 Race Condition: TicketSalesService.ReserveSeat/BuyTicket/
  CancelTicket now guard the check-then-write on Seat.Status with a lock,
  keeping unrelated work (customer lookup) outside the critical section.
- Add TicketSalesServiceSemaphore + TicketSales.SemaphoreDemo project as
  a SemaphoreSlim-based alternative to lock, for comparison.
- Add GetServerTime operation with an optional artificial response delay,
  and a TimeSynchronizer (client-side) that estimates clock offset from
  RTT/2 the way the lab describes, plus a simulated per-client clock
  offset in TicketSales.Client for demonstrating clock drift.
- SYNCHRONIZATION.md documents the fix, the lock vs SemaphoreSlim
  comparison, and captured before/after experiment output.
EOF
)"
```

## Self-Review Notes (already applied above)

- Spec coverage: Часть 1 Требования 1-11 -> Tasks 1-3, 6, 7. Часть 2 Требования 1-11 -> Tasks 1, 4, 5, 6. Проверки 1-3 -> Task 6 Step 1 + Task 5 Step 4.
- No placeholders: every step has real code, not "add appropriate handling".
- Type consistency: `TimeSynchronizer.CalculateOffset`/`ApplyOffset` signatures match between Task 4 (definition) and Task 5 (usage). `GetServerTimeResponseDto.ServerTimeUtc` matches between Task 1 (definition), Task 1 test, and Task 5 usage. `ITicketSalesService.GetServerTime()` matches between Task 1 and Task 3 (`TicketSalesServiceSemaphore` implements the same interface).
