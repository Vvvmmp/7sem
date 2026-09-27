# База данных, транзакции и конкурентный доступ (ЛР №4)

Продолжение [CONCURRENCY.md](CONCURRENCY.md) (ЛР №2) и [SYNCHRONIZATION.md](SYNCHRONIZATION.md)
(ЛР №3). Здесь состояние предметной области переносится из памяти сервера
в PostgreSQL, а составная операция бронирования получает явную поддержку
транзакций (COMMIT/ROLLBACK).

## 1. СУБД и обоснование выбора

**PostgreSQL 17** (`postgres:17-alpine`, поднимается через `docker-compose.yml`):
не требует локальной установки — только `docker compose up -d`; полная
поддержка ACID-транзакций (`BEGIN`/`COMMIT`/`ROLLBACK`), необходимая для
Части 3; доступ из C# через `Npgsql` — прямой, без ORM, поэтому в коде видно
ровно то, что выполняется на сервере СУБД.

Хост-порт намеренно `5433` (а не стандартный `5432`) — на машине уже занят
`5432` другим локальным контейнером (проверено `lsof -iTCP -sTCP:LISTEN`).

## 2. Структура базы данных

Схема создаётся автоматически при первом запуске сервера
(`PostgresSchemaInitializer.EnsureCreatedAndSeeded`,
[src/TicketSales.Server/Storage/Postgres/PostgresSchemaInitializer.cs](src/TicketSales.Server/Storage/Postgres/PostgresSchemaInitializer.cs)):

```sql
CREATE TABLE events (
    id SERIAL PRIMARY KEY,
    title TEXT NOT NULL,
    venue TEXT NOT NULL,
    starts_at TIMESTAMPTZ NOT NULL,
    base_price NUMERIC NOT NULL
);

CREATE TABLE seats (
    id SERIAL PRIMARY KEY,
    event_id INTEGER NOT NULL REFERENCES events(id),
    row TEXT NOT NULL,
    number INTEGER NOT NULL,
    status TEXT NOT NULL,
    UNIQUE (event_id, row, number)
);

CREATE TABLE customers (
    id SERIAL PRIMARY KEY,
    full_name TEXT NOT NULL,
    email TEXT NOT NULL UNIQUE
);

CREATE TABLE tickets (
    id SERIAL PRIMARY KEY,
    event_id INTEGER NOT NULL REFERENCES events(id),
    seat_id INTEGER NOT NULL REFERENCES seats(id),
    customer_id INTEGER NOT NULL REFERENCES customers(id),
    status TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE payments (
    id SERIAL PRIMARY KEY,
    ticket_id INTEGER NOT NULL REFERENCES tickets(id),
    amount NUMERIC NOT NULL,
    status TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);
```

Первичные ключи — `SERIAL` (автоинкремент); внешние ключи связывают
`seats→events`, `tickets→events/seats/customers`, `payments→tickets`;
ограничения целостности — `UNIQUE (event_id, row, number)` на местах
(нельзя завести два одинаковых места одного события) и `UNIQUE` на
`customers.email`. Статусы (`SeatStatus`/`TicketStatus`/`PaymentStatus`)
хранятся как `TEXT` (`Enum.ToString()`/`Enum.Parse`) — без отдельного
Postgres-типа `ENUM`, чтобы не усложнять миграции ради лабораторной работы.

## 3. Архитектура Client → Server → Database

Клиент никогда не открывает соединение с БД — в
[`NetworkClient`](src/TicketSales.Client/NetworkClient.cs) нет ни одного
упоминания Npgsql, только TCP и JSON-протокол. Путь запроса:

```
TicketSales.Client (NetworkClient)
        │  TCP + JSON (JsonLineProtocol)
        ▼
TicketSales.Server (ClientSession → OperationDispatcher → Handler)
        │  вызов ITicketSalesService
        ▼
TicketSalesServicePostgres
        │  Npgsql (PostgresConnectionFactory)
        ▼
PostgreSQL
```

`Storage/Postgres/*Repository` реализуют те же интерфейсы
(`IEventRepository`, `ISeatRepository`, `ITicketRepository`,
`ICustomerRepository`, `IPaymentRepository`), что и in-memory версия из
ЛР №2/№3 — `Domain`/`Dispatching` не изменились ни на строчку, поменялся
только `Storage`-слой и то, что конструирует `Program.cs`.

## 4. Работающие операции с БД (сквозной ручной прогон)

```bash
docker compose up -d
dotnet run --project src/TicketSales.Server -- 5070
dotnet run --project src/TicketSales.Client
```

`1` (GetEvents, чтение) → 2 события, 10 и 8 свободных мест. `2`
(ReserveSeat, событие 1 / место 1) → `Место забронировано. TicketId = 1`.
Повторный `1` → у события 1 свободных мест стало 9. Прямая проверка:

```
$ docker exec ticketsales-postgres psql -U ticketsales -d ticketsales -c "select id, status from seats where id=1;"
 id |  status
----+----------
  1 | Reserved

$ ... -c "select * from tickets;"
 id | event_id | seat_id | customer_id |  status  | created_at
  1 |        1 |       1 |           1 | Reserved | 2026-09-27 20:56:13...

$ ... -c "select * from customers;"
 id |  full_name  |      email
  1 | Test Testov | test@example.com
```

Чтение (`GetEvents`), добавление (`Add` для `Customer`/`Ticket`) и
изменение (`Update` для `Seat`) — все три подтверждены реальными строками
в БД, а не только ответом сервера.

## 5-9. Параллельная работа, общий ресурс, критическая секция, lock, результат

Общий ресурс тот же, что в ЛР №2/№3: `Seat.Status`. Критическая секция —
та же логика (`check Free → write Reserved`), но теперь внутри неё ещё и
`INSERT` в `tickets` — оба SQL-запроса выполняются на одном
`NpgsqlConnection` под одним C#-`lock` (`TicketSalesServicePostgres.ReserveSeat`,
[src/TicketSales.Server/Domain/Services/TicketSalesServicePostgres.cs](src/TicketSales.Server/Domain/Services/TicketSalesServicePostgres.cs)):

```csharp
lock (_seatLock)
{
    // SELECT status FROM seats WHERE id = @id
    // if not Free -> FAIL
    // INSERT INTO tickets (...) RETURNING id
    // UPDATE seats SET status = 'Reserved' WHERE id = @id
    // transaction?.Commit()
}
```

Реальный прогон — 5 клиентов одновременно шлют `ReserveSeat` на одно и то
же место (`tests/TicketSales.Tests/PostgresReserveSeatConcurrencyTests.cs`):

```
[00:02:32.521] [Thread 19] ReserveSeat: seat=1 LOCK
[00:02:32.523] [Thread 19] ReserveSeat: seat=1 проверка статуса -> Free
[00:02:32.525] [Thread 19] ReserveSeat: seat=1 создан ticketId=1 (Operation 1: Create Ticket)
[00:02:32.525] [Thread 19] ReserveSeat: seat=1 обновлён статус -> Reserved (Operation 2: Update Seat)
[00:02:32.526] [Thread 19] ReserveSeat: seat=1 SUCCESS COMMIT
[00:02:32.526] [Thread 19] ReserveSeat: seat=1 UNLOCK
[00:02:32.526] [Thread 8]  ReserveSeat: seat=1 LOCK
[00:02:32.527] [Thread 8]  ReserveSeat: seat=1 проверка статуса -> Reserved
[00:02:32.527] [Thread 8]  ReserveSeat: seat=1 FAIL (уже занято)
[00:02:32.527] [Thread 8]  ReserveSeat: seat=1 UNLOCK
[00:02:32.528] [Thread 11] ReserveSeat: seat=1 LOCK -> Reserved -> FAIL -> UNLOCK
[00:02:32.528] [Thread 15] ReserveSeat: seat=1 LOCK -> Reserved -> FAIL -> UNLOCK
[00:02:32.529] [Thread 23] ReserveSeat: seat=1 LOCK -> Reserved -> FAIL -> UNLOCK

Passed TicketSales.Tests.PostgresReserveSeatConcurrencyTests.ConcurrentReserveSeat_OnSameSeat_OnlyOneSucceeds
```

Ровно один `SUCCESS` (Thread 19), остальные четыре — строго последовательные
`FAIL`, каждый со своим `LOCK`→`UNLOCK` — ни один поток не входит в
критическую секцию, пока в ней другой. Почему lock, а не что-то ещё:
критическая секция здесь — синхронный ADO.NET-код без `await` внутри
(`NpgsqlCommand.ExecuteScalar`/`ExecuteNonQuery` — блокирующие вызовы), а
для такого кода `lock` — простейший и самый дешёвый механизм (см.
[SYNCHRONIZATION.md](SYNCHRONIZATION.md) для сравнения с `SemaphoreSlim`,
где это уже разобрано на примере ЛР №3).

## 10-18. Составная операция, COMMIT, ROLLBACK — реальный прогон

Составная операция (по заданию, вариант 6 — продажа билетов):
**создание `Ticket` + изменение `Seat.Status`**. Демонстрационный проект
[`src/TicketSales.TransactionDemo`](src/TicketSales.TransactionDemo) сбрасывает
БД и прогоняет 4 эксперимента через реальный `Client → Server → Database` путь:

```bash
docker compose up -d
dotnet run --project src/TicketSales.TransactionDemo
```

```
=== Эксперимент 1: без транзакции, без ошибки ===
ReserveSeat: SUCCESS, ticketId=1
Состояние БД после эксперимента: seat.Status=Reserved, ticketCount=1

=== Эксперимент 2: без транзакции, с искусственной ошибкой ===
[00:02:21.415] ReserveSeat: seat=2 создан ticketId=2 (Operation 1: Create Ticket)
[00:02:21.415] ReserveSeat: seat=2 ИСКУССТВЕННАЯ ОШИБКА между Operation 1 и Operation 2
ReserveSeat: FAIL (Искусственная ошибка между операциями (демонстрация транзакции))
Состояние БД после эксперимента: seat.Status=Free, ticketCount=1

=== Эксперимент 3: с транзакцией, без ошибки (COMMIT) ===
[00:02:21.421] ReserveSeat: seat=3 обновлён статус -> Reserved (Operation 2: Update Seat)
[00:02:21.422] ReserveSeat: seat=3 SUCCESS COMMIT
ReserveSeat: SUCCESS, ticketId=3
Состояние БД после эксперимента: seat.Status=Reserved, ticketCount=1

=== Эксперимент 4: с транзакцией, с искусственной ошибкой (ROLLBACK) ===
[00:02:21.426] ReserveSeat: seat=4 ИСКУССТВЕННАЯ ОШИБКА между Operation 1 и Operation 2
[00:02:21.427] ReserveSeat: seat=4 ROLLBACK
ReserveSeat: FAIL (Искусственная ошибка между операциями (демонстрация транзакции))
Состояние БД после эксперимента: seat.Status=Free, ticketCount=0
```

**Эксперимент 2 — некорректное/частичное состояние без транзакции.**
Ответы на Требование 2 Часть 3:
1. Какая операция успела выполниться? — `INSERT INTO tickets` (Operation 1).
2. Какая не выполнилась? — `UPDATE seats` (Operation 2), т.к. между ними
   выброшено исключение.
3. Остались ли изменения в БД? — да, `ticketCount=1` — билет создан и остался.
4. Возникло ли частичное изменение? — да: `ticketCount=1`, но `seat.Status=Free`
   — билет существует на месте, которое формально всё ещё свободно.
5. Является ли это состояние корректным? — нет: место одновременно "свободно"
   и уже имеет билет; при повторном бронировании того же места клиент не
   увидит существующий билет и получит второй (см. тест
   `PartialFailureRun_SeatStaysReservableExactlyOnce`, где ровно это и
   происходит — после частичного сбоя место снова успешно бронируется,
   и `ticketCount` становится `2`).

**Эксперимент 4 — ROLLBACK отменяет обе операции**, включая уже выполненный
`INSERT` (`ticketCount=0` — не 1, как в эксперименте 2): без явной
транзакции откатить уже вставленную строку невозможно, с транзакцией это
ровно то, для чего она нужна.

## Таблица (Часть 4, Требование 3 — конкурентный доступ)

| Механизм | Клиент | Время отправки | Вход в критическую секцию | Выход | Результат |
| --- | --- | --- | --- | --- | --- |
| Без синхронизации | Client 1-5 | ~одновременно | — | — | 5 успехов из 5 (Race Condition, см. CONCURRENCY.md, ЛР №2) |
| lock | Thread 19 | 00:02:32.521 | 00:02:32.521 | 00:02:32.526 | SUCCESS |
| lock | Thread 8 | 00:02:32.526 | 00:02:32.526 | 00:02:32.527 | FAIL (уже занято) |
| lock | Thread 11 | 00:02:32.528 | 00:02:32.528 | 00:02:32.528 | FAIL (уже занято) |
| lock | Thread 15 | 00:02:32.528 | 00:02:32.528 | 00:02:32.529 | FAIL (уже занято) |
| lock | Thread 23 | 00:02:32.529 | 00:02:32.529 | 00:02:32.530 | FAIL (уже занято) |

## Таблица (Часть 4, Требование 4 — транзакция)

| Эксперимент | Транзакция | Операция 1 (Create Ticket) | Операция 2 (Update Seat) | Ошибка | Состояние БД |
| --- | --- | --- | --- | --- | --- |
| 1 | Нет | выполнена | выполнена | Нет | изменения сохранены (Reserved, ticketCount=1) |
| 2 | Нет | выполнена | не выполнена | Да | **частично изменено** (ticket создан, seat всё ещё Free) |
| 3 | Да | выполнена | выполнена | Нет | изменения сохранены — COMMIT (Reserved, ticketCount=1) |
| 4 | Да | выполнена | не выполнена | Да | **изменений нет** — ROLLBACK (Free, ticketCount=0) |

## Таблица сравнения (Часть 4, Требование 5)

| Критерий | Без транзакции | С транзакцией |
| --- | --- | --- |
| Несколько связанных операций | Выполняются независимо, каждая коммитится сама по себе | Выполняются как единый блок |
| Атомарность | Нет — Operation 1 и Operation 2 не связаны | Да — либо обе, либо ни одной |
| Возможность частичного изменения | Да (эксперимент 2: ticket есть, seat не изменён) | Нет |
| Использование COMMIT | Неявно, после каждой отдельной операции | Явно, один раз в конце блока |
| Использование ROLLBACK | Невозможно — Operation 1 уже закоммичена к моменту ошибки | Да — откатывает весь блок целиком |
| Сохранение изменений при успехе | Да | Да (через явный COMMIT) |
| Отмена изменений при ошибке | Нет — то, что успело выполниться, остаётся | Да — полностью |
| Согласованность результата | Не гарантирована | Гарантирована |

## Таблица итогового эксперимента (Часть 4, Требование 6)

| Часть | Условие | Ожидаемый результат | Полученный результат |
| --- | --- | --- | --- |
| Конкурентный доступ | 2+ клиента, Resource = 1 (место) | Один SUCCESS, остальные FAIL | Подтверждено: `PostgresReserveSeatConcurrencyTests` — 1 из 5 SUCCESS |
| lock | Несколько клиентов | Корректный результат (не более одного SUCCESS) | Подтверждено — реальный лог выше |
| Транзакция | Без ошибки | COMMIT | Подтверждено — Эксперимент 3, `WithTransaction_NoError_CommitsBothChanges` |
| Транзакция | С ошибкой | ROLLBACK | Подтверждено — Эксперимент 4, `WithTransaction_SimulatedError_RollsBackBothChanges` |
| БД | После COMMIT | Изменения сохранены | Подтверждено — `seat.Status=Reserved`, `ticketCount=1` |
| БД | После ROLLBACK | Изменения отменены | Подтверждено — `seat.Status=Free`, `ticketCount=0` |

## 19-21. Заполненные таблицы, unit-тесты, обработка ошибок

**Unit-тесты** (Требование 7 Часть 4), все в `tests/TicketSales.Tests/`:
- `PostgresReserveSeatConcurrencyTests.ConcurrentReserveSeat_OnSameSeat_OnlyOneSucceeds`
  — корректность общего ресурса под конкуренцией.
- `PostgresTransactionTests.WithoutTransaction_SimulatedError_LeavesTicketCreatedButSeatStillFree`
  — воспроизводит проблему без транзакции.
- `PostgresTransactionTests.WithTransaction_NoError_CommitsBothChanges` — COMMIT.
- `PostgresTransactionTests.WithTransaction_SimulatedError_RollsBackBothChanges` — ROLLBACK.
- `PostgresTransactionTests.GetSeatSnapshot_ForMissingSeat_ReturnsNotFound` — ошибка на
  отсутствующий ресурс.
- `PostgresTransactionTests.PartialFailureRun_SeatStaysReservableExactlyOnce` —
  место остаётся резервируемым ровно один раз даже после частичного сбоя.

**Обработка ошибок** (Требование 8 Часть 4): сервер не подключается к БД →
`Program.cs` делает пробное подключение при старте и завершается с понятным
сообщением, а не падает со стектрейсом (проверено вручную — `docker compose
stop postgres` → `Не удалось подключиться к базе данных: ...`). Любая другая
непредвиденная ошибка (в т.ч. Npgsql-исключения при разрыве соединения в
процессе работы) уже перехватывается общим `catch (Exception)` в
`OperationDispatcher` (не менялся с ЛР №2) и превращается в
`Response.Fail(..., ErrorCodes.InternalError, ...)`, а не в падение процесса.
Отсутствующий ресурс (место/билет) — `NotFoundException` → `NOT_FOUND`
(проверено тестом на `GetSeatSnapshot` с несуществующим `SeatId`). Попытка
занять уже занятое место — `ValidationException` → `VALIDATION_ERROR`
("Место уже занято").
