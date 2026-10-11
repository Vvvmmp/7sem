# TicketSales — ЛР №5: репликация данных и отказоустойчивость

Вариант 6 — **продажа билетов**. Реплицируемый ресурс — **состояние места** (`seats.status`: `Free` / `Reserved` / `Sold`),
пример операции — **бронирование места** (`ReserveSeat`).

Проект — продолжение ЛР №4 (PostgreSQL + lock + транзакции). В ЛР №5 данные хранятся в двух независимых
экземплярах PostgreSQL — **Primary** и **Replica**; сервер реплицирует изменения, обнаруживает отказ Primary,
переключается на Replica (Failover) и после восстановления синхронизирует копии (Recovery):

```
Primary → Replica → Failover → Recovery
```

## Переключение между лабораторными

Каждая ЛР — отдельный коммит с тегом; в дереве каждого тега лежит только то, что нужно для этой ЛР
(демо-проекты и отчёты прошлых ЛР остались в их коммитах).

```bash
git checkout claude-tech-lab1   # ЛР №1 — клиент-сервер, TCP + JSON
git checkout claude-tech-lab2   # ЛР №2 — асинхронная обработка, Race Condition
git checkout claude-tech-lab3   # ЛР №3 — lock / SemaphoreSlim, синхронизация времени
git checkout claude-tech-lab4   # ЛР №4 — PostgreSQL, транзакции
git checkout claude-tech-lab5   # ЛР №5 — репликация, Failover, Recovery (этот README)
git checkout master             # вернуться к последнему состоянию
```

## Запуск

```bash
docker compose up -d                                # Primary: localhost:5433, Replica: localhost:5434
dotnet run --project src/TicketSales.Server -- 5050 # сервер
dotnet run --project src/TicketSales.Client         # клиент (можно запустить несколько)
dotnet run --project src/TicketSales.ReplicationDemo # полный цикл одной командой (СБРАСЫВАЕТ обе БД)
dotnet test                                         # тесты (нужны оба контейнера)
```

Аргументы сервера: `[порт] [задержка GetServerTime, мс] [строка подключения Primary] [строка подключения Replica]`.

Меню клиента (пункты ЛР №5 — 7, 8, 9):

```
1. Список событий                       5. Показать локальное время
2. Забронировать место                  6. Синхронизировать время с сервером
3. Купить билет                         7. Состояние Primary / Replica
4. Отменить билет                       8. Смоделировать отказ / восстановление экземпляра
                                        9. Синхронизировать Primary и Replica
0. Выход
```

После каждой операции клиент показывает, какой экземпляр её обработал: `[Экземпляр данных: Replica]`.

---

# Отчёт для защиты

## 1. Архитектура приложения

```
 Client 1 ─┐                       ┌────────────────── Server ──────────────────┐
 Client 2 ─┼── TCP (JSON-строки) ──▶ TcpServer → ClientSession → OperationDispatcher
 Client N ─┘                       │                 │                          │
                                   │                 ▼                          │
                                   │      ReplicatedTicketSalesService          │
                                   │                 │                          │
                                   │                 ▼                          │
                                   │        ReplicationManager                  │
                                   │   (CHECK, Failover, Replication, Sync)     │
                                   └──────────┬─────────────────┬───────────────┘
                                              │                 │
                                   DataInstance "Primary"   DataInstance "Replica"
                                   TicketSalesServicePostgres (lock + транзакции ЛР №4)
                                              │                 │
                                   PostgreSQL :5433  ──репликация──▶  PostgreSQL :5434
                                   ticketsales-primary          ticketsales-replica
```

Клиент никогда не обращается к БД напрямую: всё идёт по пути Client → Server → Primary/Replica.

| Компонент | Файл | Роль |
| --- | --- | --- |
| `ClientSession` | [Networking/ClientSession.cs](src/TicketSales.Server/Networking/ClientSession.cs) | присваивает клиенту id (`Client N`), пишет `Request received` / `Request → SUCCESS`, возвращает клиенту экземпляр данных |
| `RequestContext` | [Networking/RequestContext.cs](src/TicketSales.Server/Networking/RequestContext.cs) | id клиента и использованный экземпляр для текущего запроса (`AsyncLocal`) |
| `ReplicatedTicketSalesService` | [Domain/Services/ReplicatedTicketSalesService.cs](src/TicketSales.Server/Domain/Services/ReplicatedTicketSalesService.cs) | чтение — на выбранном экземпляре; изменение — на выбранном экземпляре + репликация на второй |
| `ReplicationManager` | [Replication/ReplicationManager.cs](src/TicketSales.Server/Replication/ReplicationManager.cs) | состояние экземпляров, Failover, репликация, синхронизация, статус |
| `DataInstance` | [Replication/DataInstance.cs](src/TicketSales.Server/Replication/DataInstance.cs) | один экземпляр: своя БД, репозитории и `TicketSalesServicePostgres`; проверка доступности |
| `DataCopier` | [Replication/DataCopier.cs](src/TicketSales.Server/Replication/DataCopier.cs) | построчная репликация, полное копирование, сравнение копий |
| `ReplicationLog` | [Replication/ReplicationLog.cs](src/TicketSales.Server/Replication/ReplicationLog.cs) | журнал `[время] [клиент] событие` |
| `TicketSalesServicePostgres` | [Domain/Services/TicketSalesServicePostgres.cs](src/TicketSales.Server/Domain/Services/TicketSalesServicePostgres.cs) | бизнес-логика ЛР №4 (lock + транзакции), без изменений |

Операции протокола ЛР №5:

| Операция | Назначение |
| --- | --- |
| `GetReplicationStatus { SeatId? }` | состояние Primary/Replica, активный экземпляр, сравнение копий, статус места на обоих |
| `SetInstanceAvailability { Instance, Available }` | смоделировать отказ / восстановление экземпляра |
| `SynchronizeInstances` | синхронизация после восстановления |

## 2. Primary

Основной экземпляр — контейнер `ticketsales-primary` (`postgres:17-alpine`, порт **5433**, том `ticketsales-primary-data`).
В штатном режиме все чтения и изменения выполняются на нём. При первом запуске сервер создаёт на Primary схему и
начальные данные (`PostgresSchemaInitializer.EnsureCreatedAndSeeded`).

## 3. Replica

Резервный экземпляр — отдельный контейнер `ticketsales-replica` (порт **5434**, свой том `ticketsales-replica-data`).
Это независимая БД: сервер создаёт на ней только схему (`PostgresSchemaInitializer.EnsureCreated`), а данные Replica
получает исключительно копированием с Primary. Используется как резервная копия и как экземпляр для Failover.

## 4. Структура хранилища данных

Одинаковая схема на обоих экземплярах:

| Таблица | Поля | Изменяется операциями |
| --- | --- | --- |
| `events` | id, title, venue, starts_at, base_price | — (справочник) |
| `seats` | id, event_id → events, row, number, **status**, UNIQUE(event_id, row, number) | ReserveSeat, BuyTicket, CancelTicket |
| `customers` | id, full_name, email UNIQUE | ReserveSeat (новый покупатель) |
| `tickets` | id, event_id, seat_id → seats, customer_id → customers, status, created_at | ReserveSeat, BuyTicket, CancelTicket |
| `payments` | id, ticket_id → tickets, amount, status, created_at | BuyTicket |

**Что реплицируется.** Главный ресурс — `seats.status` (состояние места: изменяется в ходе работы, имеет понятное
состояние, легко сравнивается, это общий ресурс ЛР №2–4). Вместе с ним передаются связанные строки, без которых
состояние места не имеет смысла: билет, покупатель и платежи.

**Как реплицируется.** После успешного изменения на активном экземпляре `DataCopier.CopyTicketRows` копирует на
второй экземпляр все строки, связанные с билетом (`customers` → `seats` → `tickets` → `payments`, порядок внешних
ключей), через `INSERT … ON CONFLICT (id) DO UPDATE` в одной транзакции, затем сдвигает счётчики `SERIAL`
(`setval`), чтобы следующий `INSERT` уже на втором экземпляре (после Failover) не получил занятый id.

**Как сравниваются копии.** Для каждой таблицы — `COUNT(*)` и `md5(string_agg(строка::text ORDER BY id))`;
дополнительно построчно сравниваются статусы мест, чтобы расхождение было видно в терминах предметной области
(`место 3: Primary = Free, Replica = Sold`).

## 5. Начальное совпадение данных

При запуске `ReplicationManager.Initialize` проверяет оба экземпляра, сравнивает копии и, если они различаются,
копирует Primary → Replica. В демо обе БД засеваются независимо (у событий разное `starts_at = now() + …`),
поэтому сервер находит расхождение и выравнивает его:

```
[17:55:08.753] [Server] Startup → проверка экземпляров
[17:55:08.773] [Server] Primary → AVAILABLE
[17:55:08.774] [Server] Replica → AVAILABLE
[17:55:08.787] [Server] Начальное состояние → расхождений: 1, копирование Primary → Replica
[17:55:08.817] [Server] Начальное состояние → Primary = Replica
```

Проверка (Часть 1, Требование 7):

| Значение | Primary | Replica | Совпадает |
| --- | --- | --- | --- |
| Место 1 | Free | Free | да |
| Место 2 | Free | Free | да |
| Место 3 | Free | Free | да |
| Свободных мест | 18 | 18 | да |
| Билетов | 0 | 0 | да |
| Хэши всех 5 таблиц | — | — | совпадают (`Consistent = true`) |

## 6–8. Работающая репликация: изменение в Primary и передача в Replica

Клиент вызывает операцию → сервер проверяет Primary → выполняет изменение на Primary (lock + транзакция ЛР №4) →
реплицирует связанные строки на Replica → проверяет копии. Код: `ReplicatedTicketSalesService.WriteAsync`
и `ReplicationManager.ReplicateTicket`.

Изменение и репликация выполняются под одним шлюзом `WriteGate` (`SemaphoreSlim(1,1)`), поэтому изменения
попадают в Replica в том же порядке, что и в Primary, и не пересекаются с синхронизацией. Чтения шлюз не берут —
параллельная обработка запросов сохраняется.

## 9. Журнал репликации

Изменение 1 (реальный вывод `TicketSales.ReplicationDemo`):

```
[17:55:08.888] [Client 1] Request received: ReserveSeat (requestId=0e8eb8d0-…)
[17:55:08.890] [Client 1] Primary → CHECK
[17:55:08.890] [Client 1] Primary → AVAILABLE
[17:55:08.890] [Client 1] Primary → UPDATE ReserveSeat seat=1
[17:55:08.898] [Client 1] Primary → SUCCESS
[17:55:08.898] [Client 1] Replication → START (Primary → Replica, ticketId=1)
[17:55:08.904] [Client 1] Replica → UPSERT (customers: 1, seats: 1, tickets: 1, payments: 0)
[17:55:08.904] [Client 1] Replication → SUCCESS
[17:55:08.905] [Client 1] Request → SUCCESS (экземпляр: Primary)
```

Изменение 2 (другой клиент, покупка — реплицируется и платёж):

```
[17:55:08.919] [Client 2] Request received: BuyTicket (requestId=304766a4-…)
[17:55:08.919] [Client 2] Primary → CHECK
[17:55:08.920] [Client 2] Primary → AVAILABLE
[17:55:08.920] [Client 2] Primary → UPDATE BuyTicket ticket=1
[17:55:08.925] [Client 2] Primary → SUCCESS
[17:55:08.925] [Client 2] Replication → START (Primary → Replica, ticketId=1)
[17:55:08.930] [Client 2] Replica → UPSERT (customers: 1, seats: 1, tickets: 1, payments: 1)
[17:55:08.930] [Client 2] Replication → SUCCESS
[17:55:08.934] [Client 2] Request → SUCCESS (экземпляр: Primary)
```

В журнале есть время, идентификатор клиента, операция, экземпляр данных, результат, событие репликации и
состояние экземпляра.

### Таблица результатов репликации (Часть 2, Требование 2)

Значение — статус изменяемого места на каждом экземпляре; после каждого изменения дополнительно проверялось
совпадение хэшей всех таблиц.

| № | Операция | Primary до | Primary после | Replica до | Replica после | Репликация | Результат |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Client 1: ReserveSeat (место 1) | Free | Reserved | Free | Reserved | SUCCESS | Primary = Replica |
| 2 | Client 2: BuyTicket (билет 1, место 1) | Reserved | Sold | Reserved | Sold | SUCCESS | Primary = Replica |
| 3 | Client 1: ReserveSeat (место 2) | Free | Reserved | Free | Reserved | SUCCESS | Primary = Replica |
| 4 | Client 1: ReserveSeat (место 4), контроль после восстановления | Free | Reserved | Free | Reserved | SUCCESS | Primary = Replica |

## 10. Штатное состояние системы

Сервер, Client 1, Client 2, Primary и Replica запущены:

```
Primary: AVAILABLE, Replica: AVAILABLE, активный: Primary, согласованность: Primary = Replica
```

## 11. Искусственно созданный отказ Primary

Реализованы оба способа:

1. **Реальная остановка экземпляра** — `docker stop ticketsales-primary`. Сервер получает настоящую ошибку
   подключения. Восстановление — `docker start ticketsales-primary`.
2. **Имитация ошибки подключения в программе** — пункт меню клиента 8 (`SetInstanceAvailability`,
   `Available = false`): `DataInstance.SimulatedFailure = true`, и экземпляр отвечает на проверку как недоступный,
   хотя БД работает. Удобно для демонстрации и используется в тестах.

```
[17:55:08.974] [Client 1] Primary → FAILURE (смоделирован отказ, экземпляр UNAVAILABLE)
[17:55:08.974] [Client 1] Status → Primary UNAVAILABLE, Replica AVAILABLE, активный: Replica, согласованность: не проверить
```

## 12. Обнаружение отказа

Перед каждым запросом `ReplicationManager.ResolveForRequest` проверяет Primary: `DataInstance.CheckState` открывает
соединение и выполняет `SELECT 1` (таймаут подключения 2 с). Любая ошибка → `UNAVAILABLE`. После остановки или
перезапуска контейнера в пуле Npgsql остаются «мёртвые» соединения, поэтому при ошибке пул очищается и проверка
повторяется один раз — иначе восстановленный Primary мог бы ложно считаться недоступным. Сервер при отказе не
падает: отказ — обычный результат проверки, а не необработанное исключение.

## 13. Реализация Failover

`ReplicationManager.ResolveForRequest`:

1. Primary доступен и актуален → запрос выполняется на Primary.
2. Иначе `Failover → START`: проверяется Replica; если она доступна и актуальна → `Request redirected to Replica`.
3. Иначе `Failover → FAIL` и клиент получает понятную ошибку `DATA_UNAVAILABLE`.

Изменение, выполненное на Replica, тоже реплицируется на Primary, но Primary недоступен — поэтому он помечается
как **требующий синхронизации** (`PrimaryNeedsSync`) и не используется, пока синхронизация не выполнена.

## 14. Обработка запроса через Replica

Client 1 → Update после отказа Primary (программная модель отказа):

```
[17:55:08.979] [Client 1] Request received: ReserveSeat (requestId=9c1f6cd6-…)
[17:55:08.979] [Client 1] Primary → CHECK
[17:55:08.979] [Client 1] Primary → UNAVAILABLE
[17:55:08.979] [Client 1] Failover → START
[17:55:08.979] [Client 1] Replica → CHECK
[17:55:08.979] [Client 1] Replica → AVAILABLE
[17:55:08.979] [Client 1] Request redirected to Replica
[17:55:08.980] [Client 1] Replica → UPDATE ReserveSeat seat=3
[17:55:08.983] [Client 1] Replica → SUCCESS
[17:55:08.983] [Client 1] Replication → START (Replica → Primary, ticketId=3)
[17:55:08.983] [Client 1] Primary → UNAVAILABLE
[17:55:08.983] [Client 1] Replication → FAIL, Primary помечен как требующий синхронизации
[17:55:08.983] [Client 1] Request → SUCCESS (экземпляр: Replica)
```

Клиент при этом видит:

```
[Экземпляр данных: Replica]
Место забронировано. TicketId = 3
```

## 15. Журнал переключения

Реальный отказ — `docker stop ticketsales-primary`, затем запросы интерактивного клиента:

```
[17:54:05.639] [Client 2] Request received: GetEvents (requestId=296ddd86-…)
[17:54:05.641] [Client 2] Primary → CHECK
[17:54:05.645] [Client 2] Primary → UNAVAILABLE
[17:54:05.645] [Client 2] Failover → START
[17:54:05.645] [Client 2] Replica → CHECK
[17:54:05.646] [Client 2] Replica → AVAILABLE
[17:54:05.646] [Client 2] Request redirected to Replica
[17:54:05.646] [Client 2] Replica → READ events
[17:54:05.649] [Client 2] Replica → SUCCESS
[17:54:05.653] [Client 2] Request → SUCCESS (экземпляр: Replica)
[17:54:05.671] [Client 2] Request received: ReserveSeat (requestId=82afe6bc-…)
[17:54:05.671] [Client 2] Primary → CHECK
[17:54:05.673] [Client 2] Primary → UNAVAILABLE
[17:54:05.673] [Client 2] Failover → START
[17:54:05.673] [Client 2] Replica → CHECK
[17:54:05.674] [Client 2] Replica → AVAILABLE
[17:54:05.674] [Client 2] Request redirected to Replica
[17:54:05.674] [Client 2] Replica → UPDATE ReserveSeat seat=2
[17:54:05.678] [Client 2] Replica → SUCCESS
[17:54:05.678] [Client 2] Replication → START (Replica → Primary, ticketId=2)
[17:54:05.679] [Client 2] Primary → UNAVAILABLE
[17:54:05.679] [Client 2] Replication → FAIL, Primary помечен как требующий синхронизации
[17:54:05.680] [Client 2] Request → SUCCESS (экземпляр: Replica)
```

### Таблица результатов отказа (Часть 3, Требование 5)

| № | Клиент | Операция | Primary | Replica | Результат | Использованный экземпляр |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Client 1 | Read (GetEvents) — до отказа | AVAILABLE | AVAILABLE | SUCCESS | Primary |
| 2 | Client 2 | Read (GetSeatSnapshot, место 1) | UNAVAILABLE | AVAILABLE | SUCCESS | Replica |
| 3 | Client 1 | Update (ReserveSeat, место 3) | UNAVAILABLE | AVAILABLE | SUCCESS | Replica |
| 4 | Client 2 | Read (GetEvents) | UNAVAILABLE | AVAILABLE | SUCCESS | Replica |
| 5 | Client 2 | Update (BuyTicket, билет 3) | UNAVAILABLE | AVAILABLE | SUCCESS | Replica |

## 16. Восстановление Primary

Пункт меню 8 → `Primary`, `2 — восстановить` (или `docker start ticketsales-primary`):

```
[17:55:08.989] [Client 1] Primary → RECOVERY (имитация отказа снята)
[17:55:08.994] [Client 1] Status → Primary AVAILABLE, Replica AVAILABLE, активный: Replica, согласованность: ЕСТЬ РАСХОЖДЕНИЯ
```

Оба экземпляра снова `AVAILABLE`, но **активной остаётся Replica**: Primary содержит устаревшие данные, и
обслуживать с него запросы до синхронизации нельзя — клиент увидел бы, например, проданное место 3 свободным.
Запрос в этот момент:

```
[17:54:09.939] [Client 3] Primary → CHECK
[17:54:09.939] [Client 3] Primary → AVAILABLE
[17:54:09.939] [Client 3] Primary → данные устарели после отказа, требуется синхронизация
[17:54:09.939] [Client 3] Failover → START
[17:54:09.939] [Client 3] Replica → CHECK
[17:54:09.940] [Client 3] Replica → AVAILABLE
[17:54:09.940] [Client 3] Request redirected to Replica
```

## 17. Проверка состояния данных после восстановления

```
Primary: AVAILABLE (требует синхронизации), Replica: AVAILABLE, активный: Replica, согласованность: ЕСТЬ РАСХОЖДЕНИЯ
  - seats: Primary 18 строк, Replica 18 строк — содержимое отличается
  - customers: Primary 2 строк, Replica 3 строк — содержимое отличается
  - tickets: Primary 2 строк, Replica 3 строк — содержимое отличается
  - payments: Primary 1 строк, Replica 2 строк — содержимое отличается
  - место 3: Primary = Free, Replica = Sold
```

Анализ расхождения (Часть 4, Требование 1):

| Вопрос | Ответ |
| --- | --- |
| 1. Какие операции выполнялись во время отказа? | Client 1 — ReserveSeat (место 3), Client 2 — BuyTicket (билет 3); плюс два чтения |
| 2. На каком экземпляре? | На Replica (Primary был UNAVAILABLE) |
| 3. Какие данные отличаются? | `seats` (место 3: Free / Sold), `customers` (2 / 3), `tickets` (2 / 3), `payments` (1 / 2) |
| 4. Какая копия актуальна? | Replica — только она принимала изменения во время отказа (сервер это знает по флагу `PrimaryNeedsSync`) |
| 5. Как восстановить согласованность? | Полное копирование Replica → Primary и повторное сравнение копий |

## 18. Синхронизация

Пункт меню 9 (`SynchronizeInstances`) → `ReplicationManager.SynchronizeAsync`:

1. Берёт `WriteGate` — во время синхронизации изменения не выполняются.
2. Проверяет, что оба экземпляра доступны (иначе ошибка `SYNCHRONIZATION_ERROR`).
3. Сравнивает копии и пишет расхождения в журнал.
4. Выбирает направление: если Primary помечен как устаревший — **Replica → Primary**; если устарела Replica
   (она была недоступна при изменении) — **Primary → Replica**.
5. `DataCopier.CopyAll`: в одной транзакции на целевом экземпляре `TRUNCATE` всех таблиц, копирование всех строк,
   сдвиг счётчиков id. Ошибка в середине → откат, целевой экземпляр не остаётся «наполовину скопированным».
6. Повторно сравнивает копии; только при полном совпадении снимает флаги, и Primary снова становится активным.

```
[17:55:09.000] [Client 1] Synchronization → START
[17:55:09.000] [Client 1] Primary → AVAILABLE
[17:55:09.000] [Client 1] Replica → AVAILABLE
[17:55:09.003] [Client 1] Расхождение → seats: Primary 18 строк, Replica 18 строк — содержимое отличается
[17:55:09.003] [Client 1] Расхождение → customers: Primary 2 строк, Replica 3 строк — содержимое отличается
[17:55:09.003] [Client 1] Расхождение → tickets: Primary 2 строк, Replica 3 строк — содержимое отличается
[17:55:09.003] [Client 1] Расхождение → payments: Primary 1 строк, Replica 2 строк — содержимое отличается
[17:55:09.003] [Client 1] Расхождение → место 3: Primary = Free, Replica = Sold
[17:55:09.003] [Client 1] Synchronization → копирование Replica → Primary
[17:55:09.022] [Client 1] Synchronization → SUCCESS, Primary = Replica
```

## 19. Итоговое совпадение данных

Контрольная операция после синхронизации снова идёт через Primary и реплицируется:

```
[17:55:09.034] [Client 1] Primary → CHECK
[17:55:09.035] [Client 1] Primary → AVAILABLE
[17:55:09.035] [Client 1] Primary → UPDATE ReserveSeat seat=4
[17:55:09.038] [Client 1] Primary → SUCCESS
[17:55:09.038] [Client 1] Replication → START (Primary → Replica, ticketId=4)
[17:55:09.042] [Client 1] Replica → UPSERT (customers: 1, seats: 1, tickets: 1, payments: 0)
[17:55:09.042] [Client 1] Replication → SUCCESS
[17:55:09.047] [Client 1] Status → Primary AVAILABLE, Replica AVAILABLE, активный: Primary, согласованность: Primary = Replica
```

Билет получил id 4 без конфликта первичного ключа — счётчики id после копирования выровнены.

## 20. Заполненные таблицы результатов

Таблицы репликации и отказа — в разделах 9 и 15. Восстановление (Часть 4):

| Этап | Primary | Replica | Место 3 Primary | Место 3 Replica | Билетов Primary / Replica | Согласованность |
| --- | --- | --- | --- | --- | --- | --- |
| После восстановления Primary | AVAILABLE | AVAILABLE | Free | Sold | 2 / 3 | ЕСТЬ РАСХОЖДЕНИЯ |
| После синхронизации Replica → Primary | AVAILABLE | AVAILABLE | Sold | Sold | 3 / 3 | Primary = Replica |
| После контрольной операции | AVAILABLE | AVAILABLE | Sold | Sold | 4 / 4 | Primary = Replica |

Итоговый цикл отказоустойчивости (Часть 4, Требование 3):

| № | Этап | Результат |
| --- | --- | --- |
| 1 | Primary AVAILABLE | да |
| 2 | Replica AVAILABLE | да, начальное состояние Primary = Replica |
| 3 | Replication | 3 изменения, все SUCCESS, после каждого Primary = Replica |
| 4 | Primary FAILURE | программно и через `docker stop` |
| 5 | Failover | `Failover → START` → `Request redirected to Replica` |
| 6 | Работа через Replica | 2 чтения и 2 изменения от двух клиентов — SUCCESS |
| 7 | Primary RECOVERY | AVAILABLE, расхождение обнаружено, Primary не используется до синхронизации |
| 8 | Synchronization | Replica → Primary, SUCCESS |
| 9 | Primary = Replica | да; контрольная операция через Primary реплицирована |

Воспроизведение: `dotnet run --project src/TicketSales.ReplicationDemo` — выполняет весь цикл двумя клиентами через
TCP и печатает эти же таблицы.

## 21. Unit-тесты

`dotnet test` — **28 тестов**, все проходят. Тесты работают с реальными Primary и Replica (docker compose);
`ReplicatedTestServer` для каждого теста сбрасывает обе БД и поднимает сервер на свободном порту.

Тесты ЛР №5 — [tests/TicketSales.Tests/ReplicationTests.cs](tests/TicketSales.Tests/ReplicationTests.cs):

| Требование | Тест |
| --- | --- |
| корректное начальное состояние Primary и Replica | `InitialState_PrimaryEqualsReplica` |
| совпадение данных после репликации | `ReserveSeat_IsExecutedOnPrimary_AndReplicatedToReplica` |
| корректная обработка изменения данных | `ThreeSequentialChanges_CopiesStayEqualAfterEachOne` |
| определение недоступного Primary, выбор Replica | `PrimaryFailure_IsDetected_AndReplicaIsSelected` |
| успешный запрос после Failover | `AfterFailover_ReadAndUpdateAreServedByReplica` |
| восстановление Primary | `PrimaryRecovery_DivergenceIsDetected_AndReplicaKeepsServingUntilSync` |
| синхронизация и совпадение после неё | `Synchronize_AfterRecovery_CopiesReplicaToPrimary_AndControlUpdateIsReplicated` |
| оба экземпляра недоступны | `BothInstancesUnavailable_ClientGetsClearError_AndServerKeepsWorking` |
| Replica недоступна / ошибка репликации | `ReplicaUnavailable_UpdateSucceedsOnPrimary_ReplicationFails_ThenSyncRestoresReplica` |
| невозможность Failover | `Failover_ToStaleReplica_IsRefused` |
| невозможность синхронизации | `Synchronize_WhenPrimaryUnavailable_ReturnsSynchronizationError` |
| некорректный запрос | `SetInstanceAvailability_UnknownInstance_ReturnsValidationError` |

Остальные тесты (функциональность ЛР №1–4, теперь идут через Primary/Replica): `SuccessfulScenarioTests`,
`InvalidDataTests`, `UnknownOperationTests`, `ServerUnavailableTests`, `GetServerTimeTests`,
`PostgresReserveSeatConcurrencyTests`, `PostgresBuyTicketConcurrencyTests`, `PostgresTransactionTests`.

Тесты проверены на «поломку»: если отключить репликацию, падают 7 тестов; если убрать сдвиг счётчиков id после
копирования — падают 2 теста Failover/синхронизации.

## 22. Обработка ошибок

| Ситуация | Поведение |
| --- | --- |
| Сервер недоступен | клиент: `ServerUnavailableException` → «Не удалось подключиться: Сервер … недоступен» |
| Primary недоступен | Failover на Replica, запрос выполняется; клиент видит `[Экземпляр данных: Replica]` |
| Replica недоступна | изменения выполняются на Primary; `Replication → FAIL`, Replica помечается как требующая синхронизации и не используется для Failover до неё |
| Оба экземпляра недоступны | `DATA_UNAVAILABLE`: «Оба экземпляра данных (Primary и Replica) недоступны. Повторите запрос позже»; сервер продолжает работать |
| Ошибка подключения к Primary / Replica | `CheckState` возвращает UNAVAILABLE (с очисткой пула и повторной попыткой), исключение не выходит наружу |
| Ошибка чтения / изменения данных | экземпляр отказал уже во время запроса → `DATA_UNAVAILABLE` «Ошибка доступа к данным на экземпляре …»; следующий запрос обнаружит отказ и выполнит Failover. Транзакции ЛР №4 откатывают незавершённое изменение |
| Ошибка репликации | операция клиента не отменяется (она уже зафиксирована); второй экземпляр помечается как требующий синхронизации |
| Нарушение согласованности | обнаруживается сравнением хэшей таблиц (статус, восстановление, запуск, синхронизация); если после синхронизации копии всё ещё различаются — `SYNCHRONIZATION_ERROR` |
| Невозможность Failover | Primary недоступен, а Replica устарела → `DATA_UNAVAILABLE` «Невозможно выполнить Failover: … Replica не синхронизирована» (устаревшие данные клиенту не отдаются) |
| Невозможность синхронизации | один из экземпляров недоступен или ошибка копирования → `SYNCHRONIZATION_ERROR`, транзакция копирования откатывается |
| Некорректный запрос клиента | `VALIDATION_ERROR` (неверный JSON, пустые поля, неизвестный экземпляр), `UNKNOWN_OPERATION`, `NOT_FOUND` |

## Ограничения учебной модели

- Репликация синхронная и прикладная (сервер сам копирует строки после изменения), а не встроенная потоковая
  репликация PostgreSQL — так в коде видны все этапы Primary → Replica → Failover → Recovery.
- Флаги «требует синхронизации» хранятся в памяти сервера. Если сервер перезапустить во время отказа, при старте
  он считает актуальным Primary и выравнивает Replica по нему.
- Синхронизация запускается командой клиента (пункт 9), а не автоматически: так этапы «восстановление → проверка
  расхождений → синхронизация» видны по отдельности, как требует ЛР.
