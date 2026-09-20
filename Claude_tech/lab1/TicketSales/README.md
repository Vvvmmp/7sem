## Запуск

```bash
dotnet run --project src/TicketSales.Server -- 5050
dotnet run --project src/TicketSales.Client
```

## Тесты

```bash
dotnet test
```

## Race Condition (устранена в ЛР №3, см. SYNCHRONIZATION.md)

```bash
dotnet run --project src/TicketSales.RaceDemo        # lock, TicketSalesService
dotnet run --project src/TicketSales.SemaphoreDemo   # SemaphoreSlim, TicketSalesServiceSemaphore
```

## Синхронизация времени

```bash
dotnet run --project src/TicketSales.Server -- 5050 1000   # необязательный 2-й аргумент — искусственная задержка GetServerTime, мс
dotnet run --project src/TicketSales.Client
```

Клиент при старте спрашивает смещение своих часов в секундах (пункты меню
"Показать локальное время" и "Синхронизировать время с сервером").


