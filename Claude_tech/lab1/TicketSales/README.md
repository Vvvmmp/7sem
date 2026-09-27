## База данных (ЛР №4)

```bash
docker compose up -d          # поднимает PostgreSQL 17 на localhost:5433
```

Схема и начальные данные создаются автоматически при первом запуске
сервера (`PostgresSchemaInitializer.EnsureCreatedAndSeeded`). Подробности,
структура БД, эксперименты с lock и транзакциями — [DATABASE.md](DATABASE.md).

## Запуск

```bash
docker compose up -d
dotnet run --project src/TicketSales.Server -- 5050
dotnet run --project src/TicketSales.Client
```

Необязательный 3-й аргумент сервера — строка подключения к БД (по
умолчанию `Host=localhost;Port=5433;Database=ticketsales;Username=ticketsales;Password=ticketsales`).

## Тесты

```bash
docker compose up -d          # тесты Postgres-набора требуют поднятой БД
dotnet test
```

## Race Condition

```bash
dotnet run --project src/TicketSales.RaceDemo        # lock, TicketSalesService
dotnet run --project src/TicketSales.SemaphoreDemo   # SemaphoreSlim, TicketSalesServiceSemaphore
```

## Синхронизация времени

```bash
dotnet run --project src/TicketSales.Server -- 5050 1000   # необязательный 2-й аргумент
dotnet run --project src/TicketSales.Client
```

## Демонстрация транзакций (ЛР №4, Часть 3)

```bash
docker compose up -d
dotnet run --project src/TicketSales.TransactionDemo
```

Сбрасывает БД и прогоняет 4 эксперимента (без транзакции / с транзакцией
× без ошибки / с искусственной ошибкой между операциями), показывая
COMMIT и ROLLBACK. Реальный вывод — в [DATABASE.md](DATABASE.md).



