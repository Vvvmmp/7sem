# TicketSales — ЛР №8: контейнеризация с Docker и Docker Compose

Вариант 6, **продажа билетов**. Проект ЛР №7 (TCP-сервер, PostgreSQL Primary/Replica, Pub/Sub, кластер узлов)
упакован в Docker: свой образ сервера и клиента, БД — официальный образ `postgres:17-alpine`,
всё запускается одной командой через `compose.yaml`. Бизнес-логика предыдущих ЛР не менялась.

## Переключение между лабораторными

```bash
git checkout claude-tech-lab1   # ЛР №1 — клиент-сервер, TCP + JSON
git checkout claude-tech-lab2   # ЛР №2 — асинхронная обработка, Race Condition
git checkout claude-tech-lab3   # ЛР №3 — lock / SemaphoreSlim, синхронизация времени
git checkout claude-tech-lab4   # ЛР №4 — PostgreSQL, транзакции
git checkout claude-tech-lab5   # ЛР №5 — репликация, Failover, Recovery
git checkout claude-tech-lab6   # ЛР №6 — Pub/Sub
git checkout claude-tech-lab7   # ЛР №7 — выбор лидера и консенсус
git checkout claude-tech-lab8   # ЛР №8 — Docker, Docker Compose (этот README)
git checkout master
```

## Запуск

```bash
cp .env.example .env                       # пароль БД — только в .env (он в .gitignore)
docker compose up -d --build               # primary + replica + server
docker compose ps                          # состояние сервисов
docker logs -f ticketsales-server          # журнал сервера
dotnet run --project src/TicketSales.Client      # клиент на хосте → 127.0.0.1:5050
docker compose run --rm client                   # или клиент в контейнере → server:5050
dotnet run --project src/TicketSales.LoadTest -- 127.0.0.1 5050 50 40   # нагрузочный тест
dotnet test                                # все тесты (нужен запущенный compose)
docker compose down                        # остановить (данные остаются в volume)
```

---

## Что сделано

### 1. Компоненты и разделение на контейнеры

| Контейнер | Образ | Назначение | Порт (внутр. → хост) | Зависит от |
|---|---|---|---|---|
| `ticketsales-primary` | `postgres:17-alpine` | основная БД | 5432 → 5433 | — |
| `ticketsales-replica` | `postgres:17-alpine` | реплика (Failover из ЛР №5) | 5432 → 5434 | — |
| `ticketsales-server` | `ticketsales-server` (свой) | TCP-сервер: бронирование, покупка, Pub/Sub, кластер узлов | 5050 → 5050 | primary, replica |
| `client` (profile) | `ticketsales-client` (свой) | интерактивный консольный клиент | — | server |

Отдельные контейнеры — только для **самостоятельных процессов**. Pub/Sub-брокеры и 5 узлов кластера ЛР №7 —
классы внутри процесса сервера, поэтому отдельные контейнеры для них не создаются.
Порты БД опубликованы на хост только для локального запуска сервера и тестов; сервер в контейнере ходит к ним
по внутренней сети.

### 2. Dockerfile сервера

**Где:** [src/TicketSales.Server/Dockerfile](src/TicketSales.Server/Dockerfile), [.dockerignore](.dockerignore)

**Как работает:** многоэтапная сборка, контекст — корень решения (нужен `TicketSales.Shared`):

1. `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build` → `COPY` только `.csproj` → `RUN dotnet restore`
   (слой кешируется, пока не меняются зависимости) → `COPY` исходников → `RUN dotnet publish -c Release`.
2. `FROM mcr.microsoft.com/dotnet/runtime:10.0` — только runtime, без SDK и исходников;
   `RUN apt-get install libgssapi-krb5-2` (нужна Npgsql); `COPY --from=build`;
   `ENV APP_MODE=Docker`, `EXPOSE 5050` (только документирует порт), `ENTRYPOINT ["dotnet", "TicketSales.Server.dll"]`.

```bash
docker build -f src/TicketSales.Server/Dockerfile -t ticketsales-server .
docker images | grep ticketsales
# ticketsales-server:latest   330MB
```

Клиент собирается так же: [src/TicketSales.Client/Dockerfile](src/TicketSales.Client/Dockerfile)
(`ENV TICKETSALES_HOST=server` — адрес сервера по имени сервиса).

### 3. Конфигурация через переменные окружения

**Где:** [Configuration/ServerSettings.cs](src/TicketSales.Server/Configuration/ServerSettings.cs),
[Configuration/ConfigurationException.cs](src/TicketSales.Server/Configuration/ConfigurationException.cs)

**Как работает:** порядок — значения по умолчанию (локальный запуск, `localhost:5433/5434`) →
переменные окружения → аргументы командной строки (как в ЛР 1–7). Строки подключения собираются через
`NpgsqlConnectionStringBuilder`. Неверное значение → `ConfigurationException` с именем параметра, код выхода `2`.
В журнал пишется конфигурация **без пароля**.

| Переменная | Пример в compose | Смысл |
|---|---|---|
| `APP_MODE` | `Docker` | режим работы (в журнале) |
| `TICKETSALES_PORT` | `5050` | порт сервера |
| `DB_PRIMARY_HOST` / `DB_REPLICA_HOST` | `postgres-primary` / `postgres-replica` | **имя сервиса**, не `localhost` |
| `DB_PRIMARY_PORT` / `DB_REPLICA_PORT` | `5432` | **внутренний** порт контейнера БД |
| `DB_NAME`, `DB_USER`, `DB_PASSWORD` | из `.env` | учётные данные (пароль не в git и не в образе) |
| `DB_CONNECT_ATTEMPTS`, `DB_RETRY_DELAY_MS` | `15`, `2000` | ожидание готовности БД |

Клиент берёт адрес по умолчанию из `TICKETSALES_HOST` / `TICKETSALES_PORT`
([Client/Program.cs](src/TicketSales.Client/Program.cs)).

### 4. Ожидание готовности БД и обработка ошибок запуска

**Где:** [Configuration/DatabaseStartup.cs](src/TicketSales.Server/Configuration/DatabaseStartup.cs),
[Server/Program.cs](src/TicketSales.Server/Program.cs)

**Как работает:**
- `depends_on: condition: service_healthy` + `healthcheck: pg_isready` в compose — сервер стартует после БД.
  Но готовность всё равно не гарантирована, поэтому `InitializeWithRetryAsync` повторяет `ReplicationManager.Initialize()`
  N раз с задержкой; после исчерпания — понятное сообщение и код выхода `1`.
- Занятый порт: `SocketException` при создании `TcpServer` → «Не удалось открыть порт … укажите другой (TICKETSALES_PORT)», код `1`.
- `restart: on-failure` — при ошибке старта Docker перезапускает контейнер; ручной `docker stop` — не перезапускает.
- `PosixSignalRegistration(SIGTERM)` — `docker stop` завершает сервер штатно (`Exited (0)`, «Сервер остановлен» в журнале).

### 5. Docker Network и compose.yaml

**Где:** [compose.yaml](compose.yaml) (заменил `docker-compose.yml`), [.env.example](.env.example)

**Как работает:** пользовательская сеть `ticketsales-net` (bridge) — встроенный DNS Docker разрешает имена сервисов,
поэтому сервер подключается к `postgres-primary:5432`. `localhost` внутри контейнера — это сам контейнер.
Тома `ticketsales-primary-data` / `ticketsales-replica-data` хранят данные БД вне контейнеров.
`${POSTGRES_PASSWORD:?…}` — без `.env` compose не запустится и объяснит причину.

```
$ docker network inspect ticketsales-net
ticketsales-primary 172.20.0.3/16
ticketsales-replica 172.20.0.2/16
ticketsales-server  172.20.0.4/16

$ docker compose ps
ticketsales-primary  postgres:17-alpine  Up (healthy)  0.0.0.0:5433->5432/tcp
ticketsales-replica  postgres:17-alpine  Up (healthy)  0.0.0.0:5434->5432/tcp
ticketsales-server   ticketsales-server  Up            0.0.0.0:5050->5050/tcp
```

Журнал запуска сервера в контейнере:

```
[Server] Конфигурация → Mode=Docker, Port=5050, Primary=[Host=postgres-primary;Port=5432;Database=ticketsales;Username=ticketsales], Replica=[Host=postgres-replica;Port=5432;...], DbConnectAttempts=15, DbRetryDelay=2000ms
[Server] Primary → AVAILABLE
[Server] Replica → AVAILABLE
[Server] Начальное состояние → Primary = Replica
[Server] Startup → БД доступна (попытка 1/15)
[Server] Node 5 elected as LEADER (Term 1)
Сервер продажи билетов запущен на порту 5050 (режим Docker)
```

### 6. Переподключение клиента

**Где:** [Client/NetworkClient.cs](src/TicketSales.Client/NetworkClient.cs) (`ReconnectAsync`), [Client/Program.cs](src/TicketSales.Client/Program.cs) (`TryReconnect`)

**Как работает:** при обрыве (`NetworkException`) клиент выводит ошибку и сразу пытается переподключиться к тому же
адресу. Сервер ещё недоступен → «Сервер … недоступен. Повторите операцию позже»; следующая операция снова
попробует подключиться. Запрос без соединения теперь даёт `NetworkException`, а не `InvalidOperationException`.

---

## Эксперименты

### Основные операции через compose (бронирование → покупка → отмена)

```
ReserveSeat     -> OK | Primary | {"TicketId": 13, "Seat": {"Id": 15, "Row": "1", "Number": 5, "Status": "Reserved"}}
BuyTicket       -> OK | Primary | {"TicketId": 13, "PaymentId": 2, "Amount": 800, "Status": "Completed"}
GetSeatSnapshot -> OK | Primary | {"Status": "Sold", "TicketCount": 1}
CancelTicket    -> OK | Primary | {"TicketId": 13, "Status": "Cancelled"}
GetSeatSnapshot -> OK | Primary | {"Status": "Free", "TicketCount": 1}
```

Журнал сервера (`docker logs ticketsales-server`): запись реплицируется на контейнер Replica и публикуется событие —
БД, репликация и Pub/Sub работают через сеть Docker:

```
[Client 4] Request received: ReserveSeat
[Client 4] Replication → SUCCESS
[Client 4] Publisher → PUBLISH SeatReserved(EntityId=15, Client 4)
[Client 5] Request received: BuyTicket
[Client 5] Replication → SUCCESS
[Client 5] Publisher → PUBLISH TicketPurchased(EntityId=13, Client 5)
```

### Остановка и повторный запуск контейнеров

| Действие | Реакция приложения (журнал сервера / ответ клиенту) |
|---|---|
| `docker compose stop postgres-primary` | `Primary → UNAVAILABLE`, `Failover → START`, `Request redirected to Replica`; чтение и бронирование выполняются на **Replica** |
| `docker compose stop postgres-replica` (обе БД остановлены) | `DATA_UNAVAILABLE: Оба экземпляра данных (Primary и Replica) недоступны. Повторите запрос позже` — сервер не падает |
| `docker compose start postgres-primary postgres-replica` | запросы снова выполняются; `SynchronizeInstances`: `Replica → Primary` (Primary догоняет записи, сделанные во время отказа) |
| `docker compose stop server` | `SIGTERM → остановка сервера`, `Сервер остановлен`, `Exited (0)`; клиент: `Connection refused` / «Сервер недоступен» |
| `docker compose start server` | старт за ~1 с, клиент переподключается, бронь из шага 1 на месте (`GetSeatSnapshot` → `Reserved`) |
| `docker compose down` → `up -d` | контейнеры и сеть пересозданы, данные сохранились в volume |

Вывод: Docker перезапускает **контейнер**, но не восстанавливает данные и не переключает запросы — это делает
приложение (Failover/Recovery из ЛР №5), а сохранность обеспечивают тома.

### Обработка ошибок

| Ситуация | Результат |
|---|---|
| нет `.env` | `docker compose config`: `required variable POSTGRES_PASSWORD is missing a value: Скопируйте .env.example в .env…` |
| `TICKETSALES_PORT=abc` | `Ошибка конфигурации: Некорректный параметр TICKETSALES_PORT='abc': ожидается целое число от 1 до 65535`, exit 2 |
| порт хоста занят | `Bind for 0.0.0.0:5050 failed: port is already allocated` (Docker не создаёт контейнер) |
| БД нет в сети | `Startup → БД не готова (попытка 1/2)… Повтор через 500 ms` → `База данных недоступна после 2 попыток`, exit 1 |
| неизвестная операция / пустые поля / нет билета | `UNKNOWN_OPERATION` / `VALIDATION_ERROR` / `NOT_FOUND` |
| сборка образа не удалась | `docker build` останавливается на шаге `RUN dotnet restore/publish` с ошибкой компиляции, старый образ не меняется |

### Нагрузочное тестирование

**Где:** [src/TicketSales.LoadTest/Program.cs](src/TicketSales.LoadTest/Program.cs) — N параллельных TCP-клиентов,
смешанная нагрузка (GetEvents, GetSeatSnapshot, GetServerTime, каждые 10 запросов — ReserveSeat).
«Отказ» — ожидаемый отказ бизнес-логики (место занято), «Ошибки» — сбои сервера/сети.

```
Нагрузочный тест: 127.0.0.1:5050, клиентов 50, запросов на клиента 40
Операция         Запросов     OK  Отказ  Ошибки   avg,ms     p50     p95     p99     max
GetEvents            1200   1200      0       0      6,0     1,9    11,8    66,0    83,7
GetSeatSnapshot       400    400      0       0      4,2     1,5    15,9    17,6    21,4
GetServerTime         200    200      0       0      2,1     0,6     8,0     8,6     9,0
ReserveSeat           200      9    191       0    117,9   121,4   167,6   185,6   191,1
ИТОГО                2000   1809    191       0     16,4     1,8   121,4   151,1   191,1
Время: 0,73 s, пропускная способность: 2727 запросов/с
```

ReserveSeat медленнее: запись идёт под `WriteGate` с репликацией на Replica и публикацией события.
Свободных мест было 9 — ровно 9 успешных бронирований, двойных продаж нет.

### Тесты

`dotnet test` — **95 из 95** пройдено (новые — 18). Только тесты контейнеров: `dotnet test --filter Category=Docker`.

| Файл | Что проверяет |
|---|---|
| [ServerSettingsTests.cs](tests/TicketSales.Tests/ServerSettingsTests.cs) (unit) | значения по умолчанию; env → имена сервисов вместо `localhost`; аргументы важнее env; неверный порт/параметр → понятная ошибка; пароль не попадает в журнал |
| [DatabaseStartupTests.cs](tests/TicketSales.Tests/DatabaseStartupTests.cs) | БД недоступна → ошибка после N попыток; неизвестный хост; БД «поднимается» во время ожидания → старт успешен |
| [ServerRestartTests.cs](tests/TicketSales.Tests/ServerRestartTests.cs) | перезапуск сервера поверх тех же БД: купленный билет сохранился, операции продолжаются; остановленный сервер → `NetworkException` / `ServerUnavailableException` |
| [DockerComposeIntegrationTests.cs](tests/TicketSales.Tests/DockerComposeIntegrationTests.cs) (интеграционные, `Category=Docker`) | бронь → покупка → отмена через контейнер; невалидные запросы; `docker stop` обеих БД → `DATA_UNAVAILABLE`, `start` → работа восстановлена; `docker stop/start ticketsales-server` → клиент переподключается, данные на месте; 20 клиентов × 25 запросов без ошибок и гонка 10 клиентов за одно место — выигрывает ровно один |

---

## Сравнение: без Docker и с Docker

| Критерий | Без Docker (`dotnet run`) | Docker + Compose |
|---|---|---|
| Последовательность запуска | вручную: БД → сервер → клиент, в разных терминалах | `docker compose up -d --build`, порядок задаёт `depends_on` + healthcheck |
| Окружение | нужен .NET 10 SDK, PostgreSQL / Docker для БД | нужен только Docker; SDK и runtime внутри образов |
| Конфигурация | аргументы командной строки, значения по умолчанию | переменные окружения в `compose.yaml` и `.env` |
| Сеть | всё на `localhost`, порты 5433/5434 | сеть `ticketsales-net`, имена сервисов, внутренний порт 5432 |
| Повторный запуск | снова запускать каждый процесс | `docker compose up -d` / `restart`, контейнеры пересоздаются по описанию |
| Диагностика | вывод в своём терминале, отладчик IDE | `docker compose ps`, `docker logs -f`, коды выхода, healthcheck |
| Данные | в БД на хосте/в контейнерах из прошлых ЛР | именованные тома, переживают `down`/`up` и пересоздание контейнеров |

---

## Ответы на контрольные вопросы

1. **Docker** — платформа для сборки образов и запуска приложений в изолированных контейнерах.
2. **Контейнеризация** — упаковка приложения с зависимостями в изолированную среду, работающую на ядре хоста.
3. **Контейнер vs ВМ**: ВМ эмулирует оборудование и несёт свою ОС; контейнер — изолированный процесс на общем ядре, поэтому легче и стартует за секунды.
4. **Docker Image** — неизменяемый шаблон из слоёв: файловая система приложения + команда запуска.
5. **Docker Container** — запущенный экземпляр образа со своим состоянием и изменяемым слоем.
6. **Dockerfile** — инструкции сборки образа (`FROM`, `WORKDIR`, `COPY`, `RUN`, `EXPOSE`, `ENTRYPOINT`).
7. **ENTRYPOINT** задаёт главную команду контейнера; аргументы `docker run` добавляются к ней.
8. **Docker Engine** — демон и API, управляющие образами, контейнерами, сетями и томами.
9. **Docker Desktop** — приложение для macOS/Windows: Engine в лёгкой ВМ Linux, CLI, Compose, GUI.
10. **Docker Network** — виртуальная сеть, через которую контейнеры обмениваются данными.
11. В пользовательской сети есть **встроенный DNS**, который разрешает имя сервиса/контейнера в его IP.
12. У каждого контейнера своё сетевое пространство имён: **localhost** — это его собственный loopback.
13. **Docker Compose** — описание многоконтейнерного приложения в `compose.yaml` и запуск одной командой.
14. **Environment Variables** — параметры, задаваемые снаружи: один образ работает в разных окружениях без изменения кода.
15. **Docker Volume** — хранилище вне жизненного цикла контейнера: данные БД переживают удаление контейнера.
16. **EXPOSE** только документирует порт; публикует его на хост `-p` / `ports:`.
17. Данные в изменяемом слое удаляются вместе с контейнером, а перезапуск не восстанавливает потерянные запросы — нужны тома, репликация и логика приложения.
18. Docker запускает контейнеры на одном хосте; **Kubernetes** оркестрирует их на кластере (масштабирование, самовосстановление, балансировка) и сам работает с контейнерными образами — поэтому сначала изучается контейнеризация.
