# TicketSales — ЛР №7: выбор лидера и консенсус в распределённой системе

Вариант 6, **продажа билетов**. Значение, о котором договариваются узлы, — **ReservationStatus** билета
(`Purchased` / `Cancelled`).

Проект продолжает ЛР №6: клиент-сервер по TCP, PostgreSQL Primary/Replica и Pub/Sub. В сервер добавлен кластер
из пяти узлов. Узлы выбирают лидера, обнаруживают отказ через heartbeat, повторно выбирают лидера и голосуют
за значение, которое предлагает лидер. Решение принимается только большинством (Majority = 3 из 5).

Полные Paxos, Raft и Zab не реализуются. Реализована простая модель из ТЗ:
**выбор лидера → обнаружение отказа → повторный выбор → голосование → консенсус**.

## Переключение между лабораторными

Каждая ЛР — отдельный коммит с тегом. В дереве каждого тега лежит только то, что нужно для этой ЛР.

```bash
git checkout claude-tech-lab1   # ЛР №1 — клиент-сервер, TCP + JSON
git checkout claude-tech-lab2   # ЛР №2 — асинхронная обработка, Race Condition
git checkout claude-tech-lab3   # ЛР №3 — lock / SemaphoreSlim, синхронизация времени
git checkout claude-tech-lab4   # ЛР №4 — PostgreSQL, транзакции
git checkout claude-tech-lab5   # ЛР №5 — репликация, Failover, Recovery
git checkout claude-tech-lab6   # ЛР №6 — Pub/Sub
git checkout claude-tech-lab7   # ЛР №7 — выбор лидера и консенсус (этот README)
git checkout master             # вернуться к последнему состоянию
```

## Запуск

```bash
docker compose up -d                                 # PostgreSQL Primary :5433, Replica :5434
dotnet run --project src/TicketSales.Server -- 5050  # сервер: 5 узлов, выборы при старте, heartbeat каждые 3 с
dotnet run --project src/TicketSales.Client          # клиент (можно запустить несколько)
dotnet run --project src/TicketSales.ConsensusDemo   # все эксперименты одной командой (БД не нужна)
dotnet test                                          # тесты (интеграционные требуют docker compose)
```

Меню клиента (пункты ЛР №7 — 7–11):

```
1. Список событий           7. Состояние кластера (узлы, лидер, Majority, журнал)
2. Забронировать место      8. Отказ / восстановление узла, голос NO
3. Купить билет             9. Проверка heartbeat (обнаружение отказа)
4. Отменить билет           10. Запустить выбор лидера
5. Локальное время          11. Предложить ReservationStatus билета (голосование узлов)
6. Синхронизировать время   0. Выход
```

Сценарий для защиты через клиент: `2` (забронировать место, получить TicketId) → `7` (Node 5 — LEADER) →
`8` (Node 5 → FAILED) → `9` (heartbeat: Node 5 NO RESPONSE, Node 4 — LEADER) → `11` (TicketId, Purchased:
CONSENSUS ACHIEVED, билет куплен) → `8` (Node 4 и Node 3 → FAILED) → `11` (CONSENSUS NOT ACHIEVED, билет не изменён).

Все журналы ниже — реальный вывод `TicketSales.ConsensusDemo`.

---

# Отчёт для защиты

## 1. Архитектура приложения

**Что.** К архитектуре ЛР №6 добавлен кластер узлов, который согласует изменение ReservationStatus:

```
Client 1..N ──TCP/JSON──▶ Server (ClientSession → OperationDispatcher)
                              │
                              ├─ ProposeReservationStatus ─▶ NodeCluster.ProposeAsync
                              │                                 │ Leader → PROPOSE → Followers → YES / NO / NO RESPONSE
                              │                                 │ YES ≥ Majority ?
                              │                                 ▼
                              │                      ACHIEVED → BuyTicket / CancelTicket (Primary/Replica, ЛР №5)
                              │                      NOT ACHIEVED → ошибка CONSENSUS_NOT_ACHIEVED, данные не меняются
                              │
                              └─ фоновый heartbeat (3 с): отказ лидера → повторные выборы

NodeCluster: Node 1 · Node 2 · Node 3 · Node 4 · Node 5  ⇄  ClusterNetwork (сообщения между узлами)
```

**Где в коде.**

| Компонент | Файл |
| --- | --- |
| Узел | [Cluster/ClusterNode.cs](src/TicketSales.Server/Cluster/ClusterNode.cs) |
| Кластер: выборы, heartbeat, голосование | [Cluster/NodeCluster.cs](src/TicketSales.Server/Cluster/NodeCluster.cs) |
| Сообщения, предложение | [Cluster/ClusterMessage.cs](src/TicketSales.Server/Cluster/ClusterMessage.cs) |
| Обмен сообщениями | [Cluster/ClusterNetwork.cs](src/TicketSales.Server/Cluster/ClusterNetwork.cs) |
| Журнал | [Cluster/ClusterLog.cs](src/TicketSales.Server/Cluster/ClusterLog.cs) |
| AVAILABLE / FAILED, LEADER / FOLLOWER | [Shared/Models/NodeState.cs](src/TicketSales.Shared/Models/NodeState.cs) |
| Операции протокола | [Handlers/ClusterHandlers.cs](src/TicketSales.Server/Dispatching/Handlers/ClusterHandlers.cs) |
| DTO | [Shared/Dto/ClusterDtos.cs](src/TicketSales.Shared/Dto/ClusterDtos.cs) |
| Запуск кластера в сервере | [Server/Program.cs:47](src/TicketSales.Server/Program.cs#L47) |
| Демо экспериментов | [ConsensusDemo/Program.cs](src/TicketSales.ConsensusDemo/Program.cs) |

**Как работает.** Узлы живут внутри процесса сервера и общаются только сообщениями через `ClusterNetwork`.
Прямых вызовов чужого состояния нет. Новые операции протокола:

- `GetClusterStatus` — узлы, лидер, Term, Majority, последние 20 строк журнала;
- `SetNodeState { NodeId, Available, VotesNo }` — отказ / восстановление узла, модель несогласия;
- `CheckHeartbeat` — ручная проверка доступности;
- `StartElection` — ручной запуск выборов;
- `ProposeReservationStatus { TicketId, Status }` — операция клиента, которая приводит к предложению изменения.

Операции ЛР №1–6 работают как раньше.

## 2. 5 узлов

**Что.** Кластер из пяти узлов: Node 1 … Node 5.

**Где.** [NodeCluster.cs:40](src/TicketSales.Server/Cluster/NodeCluster.cs#L40) `NodeCluster.Create(size = 5)`,
конструктор узла — [ClusterNode.cs:24](src/TicketSales.Server/Cluster/ClusterNode.cs#L24).

**Как работает.** Каждый узел имеет всё, что требует ТЗ:

| Требование | Реализация в `ClusterNode` |
| --- | --- |
| уникальный идентификатор | `Id` |
| состояние | `Status` (AVAILABLE / FAILED) |
| роль | `Role` (LEADER / FOLLOWER), `LeaderId`, `Term` |
| информация о других узлах | `Peers` и `KnownStatusOf(peer)` — последний известный статус каждого соседа |
| отправка сообщений | `SendAsync`, `SendHeartbeatAsync` |
| приём сообщений | `Receive` |
| изменение состояния | `Fail`, `Recover`, `ApplyCoordinator`, `Vote`, `Commit` |

## 3. Уникальные ID узлов

**Что.** ID — положительное целое число, повтор запрещён.

**Где.** [ClusterNode.cs:24](src/TicketSales.Server/Cluster/ClusterNode.cs#L24) (ID > 0),
[ClusterNetwork.cs:23](src/TicketSales.Server/Cluster/ClusterNetwork.cs#L23) `Register`.

**Как работает.** При создании кластера каждый узел регистрируется в `ClusterNetwork`. Словарь
`Id → узел` не принимает второй узел с тем же ID: `ArgumentException("Node 2 уже существует...")`.
ID используется и как адрес сообщения, и как критерий выбора лидера.

## 4. Состояния AVAILABLE / FAILED

**Что.** `enum NodeStatus { Available, Failed }`.

**Где.** [NodeState.cs](src/TicketSales.Shared/Models/NodeState.cs), переходы —
[ClusterNode.cs:66](src/TicketSales.Server/Cluster/ClusterNode.cs#L66) `Fail()`,
[ClusterNode.cs:75](src/TicketSales.Server/Cluster/ClusterNode.cs#L75) `Recover()`,
моделирование — [NodeCluster.cs:224](src/TicketSales.Server/Cluster/NodeCluster.cs#L224) `SetNodeStateAsync`.

**Как работает.** Отказ моделируется прекращением ответов. Узел FAILED:
- не отвечает на сообщения: `ClusterNetwork` ждёт таймаут и возвращает отправителю `NO RESPONSE`;
- не обрабатывает сообщения, даже если они пришли напрямую: `Receive` возвращает `Error`;
- не может голосовать: `Vote` бросает исключение.

После `Recover()` узел снова AVAILABLE, но становится FOLLOWER и не знает лидера до новых выборов.

## 5. Роли Leader / Follower

**Что.** `enum NodeRole { Follower, Leader }`.

**Где.** [NodeState.cs](src/TicketSales.Shared/Models/NodeState.cs), роль назначается в
[ClusterNode.cs:132](src/TicketSales.Server/Cluster/ClusterNode.cs#L132) `ApplyCoordinator`.

**Как работает.** Роль меняется только сообщением `COORDINATOR` с результатом выборов: узел с `Id == LeaderId`
становится LEADER, остальные — FOLLOWER.

- **Leader** координирует выборы после отказа, предлагает значение, рассылает PROPOSE, собирает и считает голоса,
  определяет большинство, объявляет результат и рассылает COMMIT.
- **Follower** получает сообщения, отвечает на heartbeat (`ALIVE`), участвует в выборах (отвечает на `ELECTION`),
  принимает предложение, отдаёт голос и сообщает своё состояние (`GetClusterStatus`).

Узел FAILED в статусе показывается без роли («—»), а лидер ищется только среди доступных узлов
([NodeCluster.cs:54](src/TicketSales.Server/Cluster/NodeCluster.cs#L54)).

## 6. Механизм выбора лидера

**Что.** Выборы по сообщениям ELECTION / ALIVE / COORDINATOR (упрощённый Bully-алгоритм).

**Где.** [NodeCluster.cs:67](src/TicketSales.Server/Cluster/NodeCluster.cs#L67) `ElectAsync`.

**Как работает.**
1. Инициатор (доступный узел, обнаруживший проблему) пишет `Election started`.
2. Инициатор параллельно отправляет `ELECTION` всем остальным узлам.
3. Ответившие `ALIVE` — кандидаты (`Node N AVAILABLE`). Не ответившие исключаются (`Node 5 excluded`).
4. Лидер — кандидат с максимальным ID. Новый `Term` = максимальный известный Term + 1.
5. Инициатор применяет результат у себя и рассылает `COORDINATOR(LeaderId, Term)` всем доступным узлам.
6. В журнал пишутся `Node N elected as LEADER`, роли всех узлов и проверка `LEADER среди доступных узлов = 1`.

Выборы выполняются под `SemaphoreSlim` ([NodeCluster.cs:69](src/TicketSales.Server/Cluster/NodeCluster.cs#L69)),
поэтому одновременные запуски выполняются по очереди и не создают двух лидеров. Сообщение COORDINATOR со
старым Term узел отклоняет. Это гарантирует, что одновременно существует только один LEADER.

```
[23:20:42.244] [Server] Election started (инициатор Node 1)
[23:20:42.252] [Server] Node 1 AVAILABLE
[23:20:42.252] [Server] Node 2 AVAILABLE
[23:20:42.252] [Server] Node 3 AVAILABLE
[23:20:42.252] [Server] Node 4 AVAILABLE
[23:20:42.252] [Server] Node 5 AVAILABLE
[23:20:42.256] [Server] Node 5 elected as LEADER (Term 1)
[23:20:42.256] [Server] Node 1 → AVAILABLE, FOLLOWER
[23:20:42.256] [Server] Node 2 → AVAILABLE, FOLLOWER
[23:20:42.256] [Server] Node 3 → AVAILABLE, FOLLOWER
[23:20:42.256] [Server] Node 4 → AVAILABLE, FOLLOWER
[23:20:42.256] [Server] Node 5 → AVAILABLE, LEADER
[23:20:42.256] [Server] Проверка: LEADER среди доступных узлов = 1
```

## 7. Правило выбора лидера

**Что.** Лидером становится **доступный узел с наибольшим ID**.

**Где.** [NodeCluster.cs:111](src/TicketSales.Server/Cluster/NodeCluster.cs#L111) `var leaderId = candidates.Max();`

**Как работает.** В `candidates` попадают только инициатор и узлы, ответившие `ALIVE`. Поэтому недоступный
узел выбрать нельзя, даже если у него максимальный ID. При 5/5 лидер — Node 5, без Node 5 — Node 4,
без Node 4 и 5 — Node 3, без Node 3–5 — Node 2. Когда узел с большим ID восстанавливается, запускаются новые
выборы, и он снова становится лидером.

## 8. Heartbeat

**Что.** Сообщение `HEARTBEAT` и ответ `ALIVE`.

**Где.** [ClusterNode.cs:89](src/TicketSales.Server/Cluster/ClusterNode.cs#L89) `SendHeartbeatAsync`,
ответ — [ClusterNode.cs:218](src/TicketSales.Server/Cluster/ClusterNode.cs#L218),
периодическая проверка — [NodeCluster.cs:200](src/TicketSales.Server/Cluster/NodeCluster.cs#L200) `StartMonitoring`.

**Как работает.** Узел отправляет `HEARTBEAT` и ждёт ответ. `ALIVE` означает, что сосед доступен. Если узел FAILED,
`ClusterNetwork` ждёт `Timeout` (1 с в сервере, 0,5 с в демо, 20 мс в тестах) и сообщает `NO RESPONSE`
([ClusterNetwork.cs:38](src/TicketSales.Server/Cluster/ClusterNetwork.cs#L38)).

Сервер запускает проверку каждые 3 секунды ([Program.cs:64](src/TicketSales.Server/Program.cs#L64)) и в этом режиме
пишет в журнал только изменения. Пункт меню 9 (`CheckHeartbeat`) выполняет проверку сразу и пишет в журнал каждое сообщение.

## 9. Обнаружение отказа

**Что.** Простой failure detector: нет ответа на heartbeat за таймаут — узел FAILED.

**Где.** [NodeCluster.cs:150](src/TicketSales.Server/Cluster/NodeCluster.cs#L150) `DetectFailuresAsync`.

**Как работает.**
1. Follower с наименьшим ID (Node 1) отправляет `HEARTBEAT` лидеру.
2. Если ответа нет, в журнал пишутся `HEARTBEAT TIMEOUT` и `FAILED`, затем запускаются новые выборы.
3. Если лидер жив, он сам отправляет heartbeat всем followers.
4. Если follower перестал отвечать, пишется `Node N → HEARTBEAT TIMEOUT` / `FAILED`. Если снова отвечает — `AVAILABLE`.

Каждый узел хранит последний известный статус соседей (`KnownStatusOf`).

## 10. Отказ текущего лидера

**Что.** Моделирование: `SetNodeState { NodeId = 5, Available = false }` (меню 8).

**Где.** [NodeCluster.cs:224](src/TicketSales.Server/Cluster/NodeCluster.cs#L224).

**Как работает.** Node 5 перестаёт отвечать, но остальные узлы этого ещё не знают: Node 5 остаётся для них лидером.
Отказ обнаруживается только через heartbeat (раздел 9). После обнаружения кластер выбирает нового лидера. Если клиент
предложит значение до проверки, `ProposeAsync` сначала сам выполнит проверку и перевыборы
([NodeCluster.cs:346](src/TicketSales.Server/Cluster/NodeCluster.cs#L346) `EnsureLeaderAsync`). Сервер при этом не падает.

## 11. Повторный выбор лидера

**Что.** После обнаружения отказа запускаются новые выборы (раздел 6), инициатор — узел, обнаруживший отказ.

**Где.** [NodeCluster.cs:168](src/TicketSales.Server/Cluster/NodeCluster.cs#L168).

**Как работает.** До отказа: Node 1–4 — FOLLOWER, Node 5 — LEADER. После отказа: Node 1–3 — FOLLOWER,
Node 4 — LEADER, Node 5 — FAILED.

```
[23:20:42.265] [Server] Node 5 → FAILED (смоделирован отказ, роль была LEADER)
[23:20:42.266] [Server] Node 1 → HEARTBEAT → Node 5
[23:20:42.768] [Server] Node 5 → NO RESPONSE
[23:20:42.768] [Server] Node 5 → HEARTBEAT TIMEOUT
[23:20:42.768] [Server] Node 5 → FAILED (LEADER недоступен)
[23:20:42.768] [Server] New election started (инициатор Node 1)
[23:20:43.269] [Server] Node 1 AVAILABLE
[23:20:43.269] [Server] Node 2 AVAILABLE
[23:20:43.269] [Server] Node 3 AVAILABLE
[23:20:43.269] [Server] Node 4 AVAILABLE
[23:20:43.269] [Server] Node 5 excluded (FAILED, нет ответа)
[23:20:43.269] [Server] Node 4 elected as LEADER (Term 2)
[23:20:43.269] [Server] Node 1 → AVAILABLE, FOLLOWER
[23:20:43.269] [Server] Node 2 → AVAILABLE, FOLLOWER
[23:20:43.269] [Server] Node 3 → AVAILABLE, FOLLOWER
[23:20:43.269] [Server] Node 4 → AVAILABLE, LEADER
[23:20:43.270] [Server] Node 5 → FAILED
[23:20:43.270] [Server] Проверка: LEADER среди доступных узлов = 1
```

## 12. Предложение значения

**Что.** `Proposal(Version, TicketId, Status)` — новый ReservationStatus билета.

**Где.** [ClusterMessage.cs:20](src/TicketSales.Server/Cluster/ClusterMessage.cs#L20),
[NodeCluster.cs:251](src/TicketSales.Server/Cluster/NodeCluster.cs#L251) `ProposeAsync`,
операция клиента — [ClusterHandlers.cs:87](src/TicketSales.Server/Dispatching/Handlers/ClusterHandlers.cs#L87).

**Как работает.** Клиент выбирает пункт 11, вводит TicketId и статус. Сервер передаёт предложение лидеру.
- `Version` = последняя принятая лидером версия + 1, аналог ConfigurationVersion из ТЗ.
- Лидер пишет `Leader Node N → PROPOSE Version=…, Ticket …: ReservationStatus=…`.
- Затем параллельно (`Task.WhenAll`) отправляет `PROPOSE` каждому остальному узлу: `Leader Node 5 → Node 1: PROPOSE` и т. д.

Предложения выполняются под отдельным `SemaphoreSlim`, поэтому одновременные запросы клиентов получают разные версии.

## 13. Голосование узлов

**Что.** Ответ `VOTE` с `YES` или `NO` и причиной.

**Где.** [ClusterNode.cs:153](src/TicketSales.Server/Cluster/ClusterNode.cs#L153) `Vote`,
приём PROPOSE — [ClusterNode.cs:227](src/TicketSales.Server/Cluster/ClusterNode.cs#L227).

**Как работает.** Узел голосует **YES**, если одновременно:
- он не в режиме несогласия (`VotesNo`, меню 8 → 3);
- версия новее уже принятой (защита от повторного или устаревшего предложения);
- статус — допустимый целевой ReservationStatus: `Purchased` или `Cancelled`. Вернуть билет в `Reserved` нельзя.

Иначе узел голосует **NO** и указывает причину. Узел принимает PROPOSE только от своего текущего лидера с актуальным Term.
Лидер тоже голосует ([NodeCluster.cs:274](src/TicketSales.Server/Cluster/NodeCluster.cs#L274)).
Недоступный узел не отвечает, его результат — `NO RESPONSE`.

```
[23:20:43.778] [Server] Node 3 → голосование: всегда NO
[23:20:43.778] [Server] Leader Node 4 → PROPOSE Version=3, Ticket 3: ReservationStatus=Cancelled
[23:20:43.778] [Server] Leader Node 4 → Node 1: PROPOSE
[23:20:43.778] [Server] Leader Node 4 → Node 2: PROPOSE
[23:20:43.778] [Server] Leader Node 4 → Node 3: PROPOSE
[23:20:43.778] [Server] Leader Node 4 → Node 5: PROPOSE
[23:20:44.280] [Server] Node 1 → YES
[23:20:44.281] [Server] Node 2 → YES
[23:20:44.281] [Server] Node 3 → NO (узел не согласен)
[23:20:44.281] [Server] Node 4 → YES
[23:20:44.281] [Server] Node 5 → NO RESPONSE
[23:20:44.281] [Server] YES = 3, NO = 1, NO RESPONSE = 1
[23:20:44.281] [Server] Majority = 3 (из 5)
[23:20:44.282] [Server] CONSENSUS ACHIEVED: Version=3, Ticket 3: ReservationStatus=Cancelled принято
```

## 14. Подсчёт голосов

**Что.** Лидер собирает ответы всех узлов и считает YES, NO и NO RESPONSE.

**Где.** [NodeCluster.cs:297](src/TicketSales.Server/Cluster/NodeCluster.cs#L297).

**Как работает.**
- Ответы собираются через `Task.WhenAll`, затем пишутся в журнал в порядке ID.
- `YES` и `NO` — ответы `VOTE`.
- `NO RESPONSE` — узел не ответил за таймаут; сюда же попадает некорректный ответ (`INVALID`).
- Каждый узел учитывается ровно один раз, потому что ключ словаря `Votes` — ID узла.
- Результат возвращается клиенту в `ConsensusResultDto`: голоса по узлам, YES, NO, NO RESPONSE, Majority, Achieved.

## 15. Определение большинства

**Что.** `Majority = N / 2 + 1`, где N — **общее** число узлов кластера, а не число доступных.

**Где.** [NodeCluster.cs:47](src/TicketSales.Server/Cluster/NodeCluster.cs#L47),
проверка — [NodeCluster.cs:300](src/TicketSales.Server/Cluster/NodeCluster.cs#L300) `Achieved = Yes >= Majority`.

**Как работает.** Для 5 узлов Majority = 3, для 4 — тоже 3. Считать большинство от доступных узлов нельзя: тогда 2 узла
из 5 «договорились» бы сами с собой. Если доступно меньше Majority, выборы всё равно выбирают лидера по правилу ТЗ,
но в журнал пишется `Доступно 2/5 < Majority 3: лидер выбран, но консенсус невозможен`.

| Доступные узлы | Majority | Консенсус |
| --- | --- | --- |
| 5 | 3 | возможен |
| 4 | 3 | возможен |
| 3 | 3 | возможен |
| 2 | 3 | невозможен |
| 1 | 3 | невозможен |

## 16. Успешное достижение консенсуса

**Что.** `YES ≥ Majority` → `CONSENSUS ACHIEVED`, значение принимается и применяется.

**Где.** [NodeCluster.cs:311](src/TicketSales.Server/Cluster/NodeCluster.cs#L311) (COMMIT),
применение к данным — [ClusterHandlers.cs:115](src/TicketSales.Server/Dispatching/Handlers/ClusterHandlers.cs#L115).

**Как работает.**
1. Лидер фиксирует значение у себя и рассылает `COMMIT` ответившим узлам. Каждый узел сохраняет его в `Committed`.
2. Только после этого сервер выполняет операцию предметной области: `Purchased` → `BuyTicket`, `Cancelled` → `CancelTicket`.
3. Операция проходит через Primary/Replica из ЛР №5.

Эксперимент 1 (5/5):

```
[23:20:42.259] [Server] Leader Node 5 → PROPOSE Version=1, Ticket 1: ReservationStatus=Purchased
[23:20:42.260] [Server] Leader Node 5 → Node 1: PROPOSE
[23:20:42.261] [Server] Leader Node 5 → Node 2: PROPOSE
[23:20:42.261] [Server] Leader Node 5 → Node 3: PROPOSE
[23:20:42.261] [Server] Leader Node 5 → Node 4: PROPOSE
[23:20:42.263] [Server] Node 1 → YES
[23:20:42.263] [Server] Node 2 → YES
[23:20:42.263] [Server] Node 3 → YES
[23:20:42.263] [Server] Node 4 → YES
[23:20:42.263] [Server] Node 5 → YES
[23:20:42.263] [Server] YES = 5, NO = 0, NO RESPONSE = 0
[23:20:42.263] [Server] Majority = 3 (из 5)
[23:20:42.264] [Server] CONSENSUS ACHIEVED: Version=1, Ticket 1: ReservationStatus=Purchased принято
```

Эксперимент 2 (Node 5 FAILED, 4/5, лидер Node 4 после повторных выборов из раздела 11):

```
[23:20:43.270] [Server] Leader Node 4 → PROPOSE Version=2, Ticket 2: ReservationStatus=Purchased
[23:20:43.270] [Server] Leader Node 4 → Node 1: PROPOSE
[23:20:43.270] [Server] Leader Node 4 → Node 2: PROPOSE
[23:20:43.270] [Server] Leader Node 4 → Node 3: PROPOSE
[23:20:43.270] [Server] Leader Node 4 → Node 5: PROPOSE
[23:20:43.776] [Server] Node 1 → YES
[23:20:43.777] [Server] Node 2 → YES
[23:20:43.777] [Server] Node 3 → YES
[23:20:43.777] [Server] Node 4 → YES
[23:20:43.777] [Server] Node 5 → NO RESPONSE
[23:20:43.777] [Server] YES = 4, NO = 0, NO RESPONSE = 1
[23:20:43.777] [Server] Majority = 3 (из 5)
[23:20:43.777] [Server] CONSENSUS ACHIEVED: Version=2, Ticket 2: ReservationStatus=Purchased принято
```

Эксперимент 3 (Node 4, Node 5 FAILED, 3/5):

```
[23:20:44.282] [Server] Node 4 → FAILED (смоделирован отказ, роль была LEADER)
[23:20:44.282] [Server] Node 1 → HEARTBEAT → Node 4
[23:20:44.786] [Server] Node 4 → NO RESPONSE
[23:20:44.786] [Server] Node 4 → HEARTBEAT TIMEOUT
[23:20:44.786] [Server] Node 4 → FAILED (LEADER недоступен)
[23:20:44.787] [Server] New election started (инициатор Node 1)
[23:20:45.289] [Server] Node 1 AVAILABLE
[23:20:45.289] [Server] Node 2 AVAILABLE
[23:20:45.289] [Server] Node 3 AVAILABLE
[23:20:45.289] [Server] Node 4 excluded (FAILED, нет ответа)
[23:20:45.289] [Server] Node 5 excluded (FAILED, нет ответа)
[23:20:45.289] [Server] Node 3 elected as LEADER (Term 3)
...
[23:20:45.290] [Server] Leader Node 3 → PROPOSE Version=4, Ticket 4: ReservationStatus=Purchased
...
[23:20:45.791] [Server] Node 1 → YES
[23:20:45.792] [Server] Node 2 → YES
[23:20:45.792] [Server] Node 3 → YES
[23:20:45.792] [Server] Node 4 → NO RESPONSE
[23:20:45.792] [Server] Node 5 → NO RESPONSE
[23:20:45.792] [Server] YES = 3, NO = 0, NO RESPONSE = 2
[23:20:45.792] [Server] Majority = 3 (из 5)
[23:20:45.792] [Server] CONSENSUS ACHIEVED: Version=4, Ticket 4: ReservationStatus=Purchased принято
```

## 17. Невозможность достижения консенсуса при отсутствии большинства

**Что.** `YES < Majority` → `CONSENSUS NOT ACHIEVED`, решение не принимается.

**Где.** [NodeCluster.cs:307](src/TicketSales.Server/Cluster/NodeCluster.cs#L307),
ошибка клиенту — [ClusterHandlers.cs:108](src/TicketSales.Server/Dispatching/Handlers/ClusterHandlers.cs#L108).

**Как работает.**
- Лидер не отправляет COMMIT, ни один узел не сохраняет значение.
- Сервер не вызывает BuyTicket / CancelTicket, данные в PostgreSQL не меняются.
- Клиент получает ошибку `CONSENSUS_NOT_ACHIEVED: YES = 2, Majority = 3, доступно узлов 2/5. Изменение не применено`.

Эксперимент 4 (Node 3, 4, 5 FAILED, 2/5):

```
[23:20:45.792] [Server] Node 3 → FAILED (смоделирован отказ, роль была LEADER)
[23:20:45.792] [Server] Node 1 → HEARTBEAT → Node 3
[23:20:46.293] [Server] Node 3 → NO RESPONSE
[23:20:46.294] [Server] Node 3 → HEARTBEAT TIMEOUT
[23:20:46.294] [Server] Node 3 → FAILED (LEADER недоступен)
[23:20:46.294] [Server] New election started (инициатор Node 1)
[23:20:46.798] [Server] Node 1 AVAILABLE
[23:20:46.798] [Server] Node 2 AVAILABLE
[23:20:46.798] [Server] Node 3 excluded (FAILED, нет ответа)
[23:20:46.798] [Server] Node 4 excluded (FAILED, нет ответа)
[23:20:46.798] [Server] Node 5 excluded (FAILED, нет ответа)
[23:20:46.799] [Server] Node 2 elected as LEADER (Term 4)
[23:20:46.799] [Server] Node 1 → AVAILABLE, FOLLOWER
[23:20:46.799] [Server] Node 2 → AVAILABLE, LEADER
[23:20:46.799] [Server] Node 3 → FAILED
[23:20:46.799] [Server] Node 4 → FAILED
[23:20:46.799] [Server] Node 5 → FAILED
[23:20:46.799] [Server] Доступно 2/5 < Majority 3: лидер выбран, но консенсус невозможен
[23:20:46.799] [Server] Проверка: LEADER среди доступных узлов = 1
[23:20:46.799] [Server] Leader Node 2 → PROPOSE Version=5, Ticket 5: ReservationStatus=Purchased
[23:20:46.799] [Server] Leader Node 2 → Node 1: PROPOSE
[23:20:46.799] [Server] Leader Node 2 → Node 3: PROPOSE
[23:20:46.799] [Server] Leader Node 2 → Node 4: PROPOSE
[23:20:46.799] [Server] Leader Node 2 → Node 5: PROPOSE
[23:20:47.301] [Server] Node 1 → YES
[23:20:47.301] [Server] Node 2 → YES
[23:20:47.301] [Server] Node 3 → NO RESPONSE
[23:20:47.301] [Server] Node 4 → NO RESPONSE
[23:20:47.301] [Server] Node 5 → NO RESPONSE
[23:20:47.301] [Server] YES = 2, NO = 0, NO RESPONSE = 3
[23:20:47.301] [Server] Majority = 3 (из 5)
[23:20:47.301] [Server] CONSENSUS NOT ACHIEVED: Version=5, Ticket 5: ReservationStatus=Purchased не принято
```

Итог экспериментов (последняя таблица вывода демо):

| Эксперимент | Доступно | YES | Majority | Результат |
| --- | --- | --- | --- | --- |
| 1. Все узлы доступны | 5/5 | 5 | 3 | CONSENSUS ACHIEVED |
| 2. Отказ Node 5 | 4/5 | 4 | 3 | CONSENSUS ACHIEVED |
| Голос NO от Node 3 | 4/5 | 3 | 3 | CONSENSUS ACHIEVED |
| 3. Отказ Node 4, 5 | 3/5 | 3 | 3 | CONSENSUS ACHIEVED |
| 4. Отказ Node 3, 4, 5 | 2/5 | 2 | 3 | CONSENSUS NOT ACHIEVED |
| После восстановления 3, 4, 5 | 5/5 | 5 | 3 | CONSENSUS ACHIEVED |

**Вывод.** Пока доступно большинство (≥ 3 из 5), кластер переживает отказ лидера и продолжает принимать решения.
Без большинства лидер существует, но решение не принимается. Это и защищает от расхождения данных:
две половины кластера не могут одновременно принять разные решения.

## 18. Журнал работы узлов

**Что.** Журнал всех событий кластера.

**Где.** [ClusterLog.cs](src/TicketSales.Server/Cluster/ClusterLog.cs).

**Как работает.** `ClusterLog.Write` выводит строку в консоль сервера в формате `[HH:mm:ss.fff] [клиент] сообщение`
(общий формат журнала ЛР №5–6) и хранит её в памяти. Последние 20 строк возвращаются клиенту в `GetClusterStatus`.

| Требование ТЗ | Пример строки журнала |
| --- | --- |
| время | `[23:20:42.244]` |
| ID, состояние и роль узла | `Node 4 → AVAILABLE, LEADER`, `Node 5 → FAILED` |
| начало выборов | `Election started`, `New election started` |
| результат выборов | `Node 4 elected as LEADER (Term 2)` |
| обнаружение отказа | `Node 5 → NO RESPONSE`, `Node 5 → HEARTBEAT TIMEOUT` |
| повторный выбор | `New election started (инициатор Node 1)`, `Node 5 excluded` |
| предлагаемое значение | `Leader Node 4 → PROPOSE Version=3, Ticket 3: ReservationStatus=Cancelled` |
| голоса | `Node 3 → NO (узел не согласен)`, `Node 5 → NO RESPONSE` |
| количество YES | `YES = 3, NO = 1, NO RESPONSE = 1` |
| большинство | `Majority = 3 (из 5)` |
| результат консенсуса | `CONSENSUS ACHIEVED` / `CONSENSUS NOT ACHIEVED` |

## 19. Unit-тесты

**Где.** [tests/ClusterTests.cs](tests/TicketSales.Tests/ClusterTests.cs) — механизм без БД (таймаут 20 мс);
[tests/ClusterIntegrationTests.cs](tests/TicketSales.Tests/ClusterIntegrationTests.cs) — через TCP-клиента и PostgreSQL.

| Требование ТЗ | Тест |
| --- | --- |
| создание узлов | `Create_FiveNodes_AvailableFollowersWithUniqueIds` |
| уникальность ID | `Create_DuplicateId_IsRejected` |
| определение доступных узлов | `AvailableNodes_ExcludeFailedNodes` |
| выбор лидера | `Election_AvailableNodeWithMaxId_BecomesLeader` |
| невозможность выбрать недоступный узел | `Election_FailedNode_CannotBeElected` |
| обнаружение отказа лидера | `Heartbeat_DetectsLeaderFailure`, `Heartbeat_AllAlive_LeaderDoesNotChange` |
| повторный выбор лидера | `LeaderFailure_NewLeaderIsElected_AndOnlyOneLeaderExists` |
| определение большинства | `Majority_IsHalfPlusOne` (5→3, 4→3, 3→2) |
| положительное голосование | `Vote_AllAgree_PositiveVotesAndConsensus` |
| отрицательное голосование | `Vote_NodeDisagrees_NegativeVoteIsCounted`, `Vote_InvalidReservationStatus_AllVoteNo_NoConsensus` |
| достижение консенсуса | `Consensus_IsAchieved_WhileMajorityIsAvailable` (5/5, 4/5, 3/5) |
| нет консенсуса без большинства | `Consensus_IsNotAchieved_WithoutMajority_AndValueIsNotApplied` |
| обработка отказа узла | `FailedNode_CannotVote_AndDoesNotProcessMessages`, `UnknownNode_CannotBeContacted` |
| корректная работа после смены лидера | `AfterLeaderChange_ProposalIsMadeByNewLeader`, `RecoveredNode_RejoinsAndBecomesLeaderAgain` |
| ошибочные и одновременные сообщения | `InvalidMessages_AreRejectedWithoutChangingState`, `ConcurrentMessagesAndElections_KeepSingleLeader`, `ConcurrentProposals_GetDistinctVersions` |
| через сервер: решение применяется к билету | `ProposeReservationStatus_WithMajority_IsAppliedToTicket` |
| через сервер: без большинства билет не меняется | `ProposeReservationStatus_WithoutMajority_ReturnsError_AndTicketIsUnchanged` |

Результат `dotnet test` (с `docker compose up -d`): **77 тестов, 0 ошибок** (26 тестов кластера, 3 интеграционных,
остальные — тесты ЛР №1–6).

## 20. Обработка ошибок

**Где.** [ClusterNode.cs:195](src/TicketSales.Server/Cluster/ClusterNode.cs#L195) `Receive`,
[ClusterNetwork.cs:31](src/TicketSales.Server/Cluster/ClusterNetwork.cs#L31), [NodeCluster.cs](src/TicketSales.Server/Cluster/NodeCluster.cs).

| Ситуация из ТЗ | Как обрабатывается |
| --- | --- |
| недоступность одного узла | его голос — `NO RESPONSE`, остальные продолжают, консенсус возможен |
| недоступность нескольких узлов | то же. Если доступно меньше 3 узлов — `CONSENSUS NOT ACHIEVED` |
| отказ текущего лидера | heartbeat → `HEARTBEAT TIMEOUT` → новые выборы. `ProposeAsync` сам проверяет лидера перед предложением |
| отсутствие ответа на heartbeat | таймаут `ClusterNetwork.Timeout` → `NO RESPONSE` → узел помечается FAILED |
| невозможность связаться с узлом | несуществующий ID → `NodeUnreachableException("UNREACHABLE")` внутри кластера и `NOT_FOUND` клиенту |
| отсутствие большинства | решение не принимается, COMMIT не рассылается, данные не меняются, клиенту — `CONSENSUS_NOT_ACHIEVED` |
| попытка голосования недоступного узла | узел FAILED не отвечает, на прямое сообщение возвращает `Error`, `Vote()` бросает исключение. Его голос не учитывается |
| повторный запуск выбора лидера | выборы выполняются под `SemaphoreSlim` по очереди. Устаревший COORDINATOR отклоняется. Всегда один LEADER |
| получение нескольких сообщений одновременно | `Receive` / `Vote` / `Commit` изменяют состояние узла под `lock`. Предложения выполняются под gate (тест на 50 параллельных heartbeat и 5 параллельных предложений) |
| ошибочное сообщение от узла | неизвестный отправитель, чужой адресат, PROPOSE / COMMIT не от лидера, устаревший Term, неожиданный тип → ответ `Error` и запись `Node 1 → ERROR: ... отклонено (...)`. Исключение не выбрасывается, состояние не меняется |
| некорректные данные клиента | TicketId ≤ 0 → `VALIDATION_ERROR`. Ошибка BuyTicket / CancelTicket → обычная ошибка предметной области (ЛР №4–5) |

Сервер не падает ни в одном из случаев. Фоновый heartbeat перехватывает любые исключения и пишет их в журнал.

```
[23:20:47.302] [Server] Node 1 → ERROR: Propose от Node 9 отклонено (неизвестный отправитель Node 9)
[23:20:47.302] [Server] Node 1 → ERROR: Coordinator от Node 2 отклонено (устаревший Term 0 < 4)
[23:20:47.302] [Server] Node 1 → ERROR: Vote от Node 2 отклонено (некорректное сообщение Vote)
[23:20:47.302] [Server] Node 5 → ERROR: Propose от Node 2 отклонено (узел FAILED и не обрабатывает сообщения)
```

## Связь с Paxos, Raft и Zab

| Лабораторная | Raft | Paxos | Zab (ZooKeeper) |
| --- | --- | --- | --- |
| Node | сервер | acceptor / proposer / learner | сервер ансамбля |
| Leader / Follower | Leader / Follower (+ Candidate) | выделенный proposer (Multi-Paxos) | Leader / Follower |
| Leader election по max ID | голосование за кандидата, случайный election timeout | выбор не обязателен, лидер — оптимизация | выбор узла с самой свежей историей (zxid), затем ID |
| Term | term | номер предложения (ballot) | epoch |
| HEARTBEAT | пустой AppendEntries | — | ping лидера |
| PROPOSE / VOTE | AppendEntries / ответ | Prepare–Promise, Accept–Accepted | PROPOSAL / ACK |
| Majority | кворум | кворум | кворум |
| COMMIT | commitIndex | Learn | COMMIT |
| Version | индекс записи журнала | номер предложения | zxid |

Отличия от реальных алгоритмов:
- Выборы здесь не требуют большинства, а лидером выбирается узел с максимальным ID. Raft и Zab выбирают лидера только
  большинством голосов и только из узлов с актуальным журналом.
- Нет реплицируемого журнала команд: согласуется одно значение за раз.

---

# Ответы на контрольные вопросы

1. **Что такое консенсус?** Процесс, при котором узлы распределённой системы приходят к одному согласованному решению.
   Решение считается принятым, если его поддержало большинство, и после принятия не меняется. Здесь это
   `CONSENSUS ACHIEVED` для нового ReservationStatus при YES ≥ 3 из 5.

2. **Для чего необходим консенсус в распределённой системе?** Чтобы узлы, которые работают независимо и могут отказывать,
   не принимали противоречивых решений. Например, один узел считает билет купленным, а другой — отменённым, или два узла
   одновременно считают себя лидером. Консенсус даёт одно состояние системы и при этом допускает отказ части узлов.

3. **Что такое Leader?** Узел, которому поручена координация: он предлагает изменения, рассылает их остальным,
   собирает голоса и объявляет результат. Здесь это доступный узел с максимальным ID. Лидер не единственный узел системы:
   при его отказе выбирается новый.

4. **Что такое Follower?** Узел, который не координирует, а участвует: отвечает на heartbeat, участвует в выборах,
   получает предложения лидера, голосует YES/NO и применяет принятые решения (COMMIT).

5. **Что такое Leader election?** Процедура выбора лидера. Здесь инициатор отправляет ELECTION всем узлам, ответившие
   считаются доступными, лидером становится узел с наибольшим ID, результат рассылается сообщением COORDINATOR.

6. **Что происходит при отказе лидера?**
   1. Лидер перестаёт отвечать.
   2. Follower не получает ответ на heartbeat за таймаут (`HEARTBEAT TIMEOUT`) и помечает лидера FAILED.
   3. Follower запускает новые выборы, лидером становится доступный узел с наибольшим ID (Node 5 → Node 4).
   4. Пока лидера нет, предложения не принимаются. После выборов система продолжает работу.

7. **Для чего используется Heartbeat?** Для обнаружения отказов: узлы периодически обмениваются короткими сообщениями
   «жив?» / «жив». Отсутствие ответа — признак отказа, по которому запускаются перевыборы.

8. **Как определить, что узел недоступен?** По отсутствию ответа на heartbeat за заданный таймаут
   (здесь `ClusterNetwork.Timeout`, 1 с). В реальной сети нельзя отличить упавший узел от медленного, поэтому вывод
   «узел недоступен» — это предположение по таймауту. Таймаут выбирают больше обычной задержки ответа.

9. **Что такое Majority?** Большинство (кворум): `⌊N / 2⌋ + 1` от **общего** числа узлов. Для 5 узлов — 3, для 4 — 3.
   Любые два большинства пересекаются хотя бы в одном узле, поэтому невозможно принять два разных решения одновременно.

10. **Что такое голосование в распределённой системе?** Каждый узел сам отвечает лидеру YES или NO на предложенное
    значение. Лидер считает голоса и принимает значение только при YES ≥ Majority.

11. **Что произойдёт, если голоса разделились поровну?** Ничья — это не большинство, значит решение не принимается.
    Например, при 4 узлах: 2 YES / 2 NO, а Majority = 3. Значение не фиксируется, лидер может повторить предложение позже.
    Поэтому в кластерах используют нечётное число узлов (3, 5, 7): при нём ровная ничья невозможна, если отвечают все.

12. **Почему наличие лидера само по себе не гарантирует консенсус?** Лидер только предлагает значение. Принимает его
    большинство. В эксперименте 4 лидер Node 2 есть, но доступно 2 из 5 узлов, и решение не принимается. Кроме того,
    лидер может отказать или устареть: в Raft лидер старого term не может закоммитить запись без кворума.

13. **Чем выбор лидера отличается от достижения консенсуса?** Выбор лидера определяет, **кто** координирует. Консенсус —
    **что** принято (конкретное значение). Выборы — частный случай консенсуса (договориться о значении «ID лидера»).
    Но наличие лидера не означает, что значение принято: для этого нужно отдельное голосование большинства.

14. **Что такое отказ узла?** Состояние, в котором узел перестаёт выполнять свою функцию: остановлен (crash),
    недоступен по сети или не отвечает вовремя. Здесь моделируется состоянием FAILED: узел не отвечает на сообщения и
    не голосует. Отказы бывают crash-stop, crash-recovery и византийские (узел отвечает неверно).
    Paxos, Raft и Zab рассчитаны на отказы без византийского поведения.

15. **Что такое Paxos?** Классический алгоритм консенсуса Лэмпорта. Роли: proposer, acceptor и learner.
    1. Фаза 1 (Prepare / Promise): proposer получает от большинства acceptor обещание не принимать предложения
       с меньшим номером.
    2. Фаза 2 (Accept / Accepted): proposer предлагает значение, большинство его принимает.

    Paxos гарантирует безопасность при отказе меньшинства узлов. Его сложно понять и реализовать, на практике
    используется Multi-Paxos с выделенным лидером.

16. **Что такое Raft?** Алгоритм консенсуса, спроектированный ради понятности. Состоит из трёх частей:
    - выбор лидера: роли Follower / Candidate / Leader, term, случайный election timeout, голосование большинством;
    - репликация журнала: лидер рассылает AppendEntries, и запись закоммичена, когда её сохранило большинство;
    - безопасность: лидером может стать только узел с актуальным журналом.

    Heartbeat в Raft — пустой AppendEntries. Используется в etcd, Consul, CockroachDB. Эта ЛР ближе всего к Raft:
    роли, term, heartbeat, перевыборы, кворум.

17. **Что такое Zab?** ZooKeeper Atomic Broadcast — протокол Apache ZooKeeper. Лидер упорядочивает все изменения
    и рассылает их followers: PROPOSAL → ACK от кворума → COMMIT. Каждое изменение получает номер zxid
    (epoch + счётчик), поэтому все узлы применяют изменения в одном порядке. При отказе лидера выбирается узел с самой
    свежей историей (максимальный zxid), затем выполняется синхронизация. Цепочка
    PROPOSE → VOTE → COMMIT в этой ЛР повторяет эту схему.
