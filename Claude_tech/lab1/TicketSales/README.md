# TicketSales — Лабораторная работа №1

Вариант 6: продажа билетов (Customer, Event, Ticket, Seat, Payment).

## Архитектура

- `src/TicketSales.Shared` — модели предметной области, протокол обмена сообщениями (Request/Response), DTO операций. Используется и сервером, и клиентом.
- `src/TicketSales.Server` — TCP-сервер. Слои: `Networking` (приём соединений и сессия клиента), `Dispatching` (маршрутизация операций по имени, обработчики), `Domain` (бизнес-логика и исключения), `Storage` (интерфейсы репозиториев + in-memory реализация, легко заменить на БД в следующих лабораторных).
- `src/TicketSales.Client` — консольный клиент с меню операций.
- `tests/TicketSales.Tests` — 4 теста: успешный сценарий, неизвестная операция, некорректные данные, недоступный сервер.

## Протокол

Одна JSON-строка на сообщение (newline-delimited), поверх TCP.

Id событий, мест, билетов, платежей и покупателей — обычные `int`, назначаются сервером автоинкрементом начиная с 1 (см. `Server/Storage/InMemory/InMemoryRepository.cs`). Событие «Концерт группы Horizon» из SeedData всегда получает Id=1, первое его место — Id=1, второе событие — Id=2 и т.д.

Запрос:
```json
{"RequestId":"...","Operation":"ReserveSeat","Payload":{"EventId":1,"SeatId":1,"CustomerFullName":"...","CustomerEmail":"..."}}
```

Ответ:
```json
{"RequestId":"...","Success":true,"Data":{...},"Error":null}
```

Операции: `GetEvents`, `ReserveSeat`, `BuyTicket`, `CancelTicket`.

Коды ошибок: `UNKNOWN_OPERATION`, `VALIDATION_ERROR`, `NOT_FOUND`, `INTERNAL_ERROR`.

## Запуск

```bash
dotnet run --project src/TicketSales.Server -- 5050
dotnet run --project src/TicketSales.Client
```

## Тесты

```bash
dotnet test
```

## Git

```bash
git init
git add .
git commit -m "Лабораторная работа №1: клиент-серверное приложение продажи билетов"
```

## Масштабирование в следующих лабораторных

- Чтобы добавить операцию — добавить DTO в `Shared/Dto`, обработчик в `Server/Dispatching/Handlers`, зарегистрировать в `Program.cs`.
- Чтобы подключить БД — реализовать интерфейсы `Storage/I*Repository` поверх EF Core вместо `Storage/InMemory/*`, оставив `Domain`, `Dispatching` без изменений.
- Транспорт (TCP) изолирован в `Server/Networking` и `Client/NetworkClient` — при переходе на REST меняются только эти слои.
