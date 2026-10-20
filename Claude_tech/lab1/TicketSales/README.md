# TicketSales — ЛР №6: взаимодействие компонентов по модели Pub/Sub

Вариант 6, **продажа билетов**:

| Основное событие | Дополнительные события | Subscriber |
| --- | --- | --- |
| `TicketPurchased` | `SeatReserved`, `TicketCancelled` | `Logger`, `SeatService`, `NotificationService` и новый `AnalyticsService` |

Проект продолжает ЛР №5: клиент-сервер по TCP и PostgreSQL Primary/Replica. После успешной операции предметной области
сервер формирует событие и передаёт его в Publisher. Publisher отправляет событие в Broker, а Broker доставляет его подписчикам.
Промышленные брокеры (Kafka, RabbitMQ) не используются: всё написано на C#.

## Переключение между лабораторными

Каждая ЛР — отдельный коммит с тегом. В дереве каждого тега лежит только то, что нужно для этой ЛР.

```bash
git checkout claude-tech-lab1   # ЛР №1 — клиент-сервер, TCP + JSON
git checkout claude-tech-lab2   # ЛР №2 — асинхронная обработка, Race Condition
git checkout claude-tech-lab3   # ЛР №3 — lock / SemaphoreSlim, синхронизация времени
git checkout claude-tech-lab4   # ЛР №4 — PostgreSQL, транзакции
git checkout claude-tech-lab5   # ЛР №5 — репликация, Failover, Recovery
git checkout claude-tech-lab6   # ЛР №6 — Pub/Sub (этот README)
git checkout master             # вернуться к последнему состоянию
```

## Запуск

```bash
docker compose up -d                                 # PostgreSQL Primary :5433, Replica :5434
dotnet run --project src/TicketSales.Server -- 5050  # сервер
dotnet run --project src/TicketSales.Client          # клиент (можно запустить несколько)
dotnet run --project src/TicketSales.PubSubDemo      # итоговый эксперимент одной командой (сбрасывает БД)
dotnet test                                          # тесты
```

Меню клиента (пункты ЛР №6 — 7–11):

```
1. Список событий           7. Состояние Pub/Sub (Broker, Subscriber, очередь, статистика)
2. Забронировать место      8. Отказ / восстановление Subscriber
3. Купить билет             9. Отказ / восстановление Broker
4. Отменить билет           10. Подписать Subscriber на событие
5. Локальное время          11. Временные ошибки Subscriber (повторная доставка)
6. Синхронизировать время   0. Выход
```

Все журналы ниже — реальный вывод `TicketSales.PubSubDemo`.

---

# Отчёт для защиты

## 1. Архитектура приложения

**Что.** К архитектуре ЛР №5 добавлен слой событий:

```
Client 1..N ──TCP/JSON──▶ Server (ClientSession → OperationDispatcher → ReplicatedTicketSalesService)
                                   │ 1. операция на Primary/Replica (ЛР №5)
                                   │ 2. new PubSubEvent(...)
                                   ▼
                               Publisher ──▶ Broker-1 (основной)  ─┐
                                        └──▶ Broker-2 (резервный) ─┤ подписки, очередь, повторы
                                                                   ▼
                     Logger · SeatService · NotificationService · AnalyticsService
```

**Где в коде.**

| Компонент | Файл |
| --- | --- |
| Event | [PubSub/PubSubEvent.cs](src/TicketSales.Server/PubSub/PubSubEvent.cs) |
| Publisher | [PubSub/Publisher.cs](src/TicketSales.Server/PubSub/Publisher.cs) |
| Broker, очередь, повторы | [PubSub/Broker.cs](src/TicketSales.Server/PubSub/Broker.cs) |
| Subscriber | [PubSub/Subscriber.cs](src/TicketSales.Server/PubSub/Subscriber.cs) |
| Сборка системы | [PubSub/PubSubSystem.cs](src/TicketSales.Server/PubSub/PubSubSystem.cs) |
| Публикация из сервера | [ReplicatedTicketSalesService.cs:52](src/TicketSales.Server/Domain/Services/ReplicatedTicketSalesService.cs#L52) |
| Операции протокола | [Handlers/PubSubHandlers.cs](src/TicketSales.Server/Dispatching/Handlers/PubSubHandlers.cs) |
| DTO | [Shared/Dto/PubSubDtos.cs](src/TicketSales.Shared/Dto/PubSubDtos.cs) |

**Как работает.** Каждая операция ЛР №5 выполняется как раньше. После успеха сервер вызывает только
`Publisher.Publish(event)`; о подписчиках он ничего не знает. Через протокол доступны новые операции:

- `GetPubSubStatus` — состояние Broker и Subscriber, очередь, статистика;
- `SetPubSubAvailability { Component, Available, TransientFailures }` — моделирование отказа;
- `Subscribe { Subscriber, EventType }` — регистрация подписки.

## 2. Publisher

**Что.** Компонент, который принимает событие от сервера и передаёт его в Broker.

**Где.** [Publisher.cs:19](src/TicketSales.Server/PubSub/Publisher.cs#L19), метод `Publish`.

**Как работает.**
- Перебирает Broker по порядку: первый доступный принимает событие.
- Возвращает `PublishResult(Success, Broker, Error, Delivery)`. `Delivery` — задача доставки, которую сервер не ждёт.
- Исключений наружу не выбрасывает: недоступный Broker или некорректное событие превращаются в `Success=false` с понятным `Error` и пишутся в журнал.
- В коде Publisher нет ни одного типа Subscriber.

## 3. Broker

**Что.** Принимает события, хранит подписки, определяет получателей, доставляет события, обрабатывает ошибки и хранит очередь.

**Где.** [Broker.cs:83](src/TicketSales.Server/PubSub/Broker.cs#L83) `Publish`, [Broker.cs:134](src/TicketSales.Server/PubSub/Broker.cs#L134) `DeliverAsync`.

**Как работает.**
1. Если Broker недоступен, выбрасывается `BrokerUnavailableException`.
2. `e.Validate()` проверяет событие, в журнал пишется `EVENT RECEIVED`.
3. Получатели берутся из словаря `тип события → List<Subscriber>`.
4. Доставка запускается в фоне через `Task.Run` и `Task.WhenAll`.
5. Ошибка доставки одному Subscriber перехватывается внутри `DeliverAsync` и не влияет на остальных.

## 4. Subscriber

**Что.** Абстрактный класс `Subscriber` и четыре реализации:

| Subscriber | Назначение | Задержка |
| --- | --- | --- |
| `LoggerSubscriber` («Logger») | пишет событие в журнал | 1 с |
| `SeatService` | изменяет состояние предметной области (билет → Reserved / Sold / Free) | 3 с |
| `NotificationService` | формирует уведомление покупателю | 2 с |
| `AnalyticsService` | собирает статистику по типам и клиентам, данные не меняет | 0,5 с |

**Где.** [Subscriber.cs:37](src/TicketSales.Server/PubSub/Subscriber.cs#L37) `HandleAsync`, реализации ниже в том же файле.

**Как работает.** `HandleAsync`:
1. проверяет `Available`;
2. ждёт `Delay`;
3. при необходимости имитирует временную ошибку;
4. вызывает `Process(e)` и сохраняет событие в `Received`.

Результат и ошибка возвращаются в Broker: успешное завершение задачи либо исключение.

## 5. Структура Event

**Что.** `PubSubEvent`:

| Поле | Значение |
| --- | --- |
| `Id` | Guid |
| `Type` | тип события |
| `EntityId` | id места или билета |
| `ClientId` | `Client N` из `RequestContext` |
| `Timestamp` | время создания |
| `Data` | дополнительные данные |

Класс назван `PubSubEvent`, потому что `Event` в проекте — это мероприятие.

**Где.** [PubSubEvent.cs](src/TicketSales.Server/PubSub/PubSubEvent.cs). `EventTypes` — три типа, `Validate()` — проверка.

**Как работает.** Какие события создаёт сервер:

| Операция | Событие | EntityId | Data |
| --- | --- | --- | --- |
| ReserveSeat | `SeatReserved` | id места | `TicketId`, `EventId` |
| BuyTicket | `TicketPurchased` | id билета | `Amount` |
| CancelTicket | `TicketCancelled` | id билета | — |

## 6. Механизм регистрации подписки

**Что.** `Subscribe(subscriber, eventType)`.

**Где.** [Broker.cs:41](src/TicketSales.Server/PubSub/Broker.cs#L41) и [PubSubSystem.cs:50](src/TicketSales.Server/PubSub/PubSubSystem.cs#L50).

**Как работает.**
- `PubSubSystem.Subscribe` ищет Subscriber в каталоге. Если его там нет — `NOT_FOUND «Subscriber не зарегистрирован»`.
- Затем подписывает его на всех Broker.
- `Broker.Subscribe` проверяет тип события (неизвестный тип — `VALIDATION_ERROR`) и добавляет Subscriber в список под `lock`.

Начальные подписки создаёт `PubSubSystem.Create`:

```
Broker-1 → SUBSCRIBE Logger: TicketPurchased        Broker-1 → SUBSCRIBE SeatService: SeatReserved
Broker-1 → SUBSCRIBE NotificationService: TicketPurchased  ... (то же на Broker-2)
```

| Subscriber | TicketPurchased | SeatReserved | TicketCancelled |
| --- | --- | --- | --- |
| Logger | ✓ | ✓ | ✓ |
| SeatService | ✓ | ✓ | ✓ |
| NotificationService | ✓ | — | ✓ |
| AnalyticsService | подписывается во время работы (п. 18) | | |

## 7. Публикация события

**Что.** Путь Server → Publisher → Broker.

**Где.** [ReplicatedTicketSalesService.cs:52](src/TicketSales.Server/Domain/Services/ReplicatedTicketSalesService.cs#L52) `PublishAsync`.

**Как работает.** Событие публикуется после успешной операции и репликации, то есть только для изменений, которые действительно произошли:

```
[10:42:21.050] [Client 1] Server → SeatReserved
[10:42:21.051] [Client 1] Publisher → PUBLISH SeatReserved(EntityId=1, Client 1)
[10:42:21.051] [Client 1] Broker-1 → EVENT RECEIVED SeatReserved(EntityId=1, Client 1)
[10:42:21.051] [Client 1] Broker-1 → получатели SeatReserved: Logger, SeatService
[10:42:21.051] [Client 1] Publisher → ACCEPTED by Broker-1
[10:42:21.056] [Client 1] Request → SUCCESS (экземпляр: Primary)
```

## 8. Доставка события Subscriber

**Что.** Broker вызывает `HandleAsync` каждого подходящего Subscriber.

**Где.** [Broker.cs:126](src/TicketSales.Server/PubSub/Broker.cs#L126) `DeliverAllAsync` и `DeliverAsync`.

**Как работает.**

```
[10:42:21.053] [Client 1] Broker-1 → SeatService SeatReserved (attempt 1)
[10:42:21.053] [Client 1] Broker-1 → Logger SeatReserved (attempt 1)
[10:42:22.055] [Client 1] Logger → журнал: SeatReserved(EntityId=1, Client 1) at 10:42:21.050
[10:42:22.055] [Client 1] Logger → SUCCESS SeatReserved (attempt 1, 1003 ms)
[10:42:24.057] [Client 1] SeatService → SUCCESS SeatReserved (attempt 1, 3004 ms)
[10:42:24.058] [Client 1] Broker-1 → ALL DONE SeatReserved: SUCCESS 2/2, 3008 ms от публикации
```

## 9. Несколько Subscriber

**Что.** Одно событие `TicketPurchased` получают три Subscriber.

**Где.** Тест `OneEvent_IsDeliveredToAllSubscribers` и шаг 6 в демо.

**Как работает.**

```
[10:42:24.618] [Client 1] Broker-1 → получатели TicketPurchased: Logger, SeatService, NotificationService
[10:42:25.620] [Client 1] Logger → SUCCESS TicketPurchased (attempt 1, 1002 ms)
[10:42:26.622] [Client 1] NotificationService → уведомление покупателю: TicketPurchased, билет 1
[10:42:26.622] [Client 1] NotificationService → SUCCESS TicketPurchased (attempt 1, 2004 ms)
[10:42:27.624] [Client 1] SeatService → SUCCESS TicketPurchased (attempt 1, 3005 ms)
[10:42:27.624] [Client 1] Broker-1 → ALL DONE TicketPurchased: SUCCESS 3/3, 3006 ms от публикации
```

## 10. Несколько типов событий

**Что.** Три типа событий: `TicketPurchased`, `SeatReserved`, `TicketCancelled`.

**Где.** `EventTypes` в [PubSubEvent.cs](src/TicketSales.Server/PubSub/PubSubEvent.cs). Тесты `SeveralEventTypes_AreRoutedBySubscription` и `NotSubscribedSubscriber_DoesNotReceiveEvent`.

**Как работает.** Каждый Subscriber получает только те события, на которые подписан. Например, `SeatReserved` уходит только Logger и SeatService (список получателей выше), а NotificationService его не получает. В тесте после публикации всех трёх типов у NotificationService в `Received` оказываются ровно `[TicketPurchased, TicketCancelled]`.

## 11. Журнал публикации

**Что.** Записи вида `[время] [клиент] компонент → действие`.

**Где.** `ReplicationLog.Write` из ЛР №5. Записи журнала пишут Server, Publisher и Broker.

**Как работает.** Идентификатор клиента берётся из `RequestContext`. Это `AsyncLocal`, поэтому `Client N` сохраняется и в фоновых задачах доставки. В журнале видны:

- время;
- клиент;
- тип события;
- Publisher;
- Broker, который принял событие;
- ошибка публикации (`Publisher → FAIL: …`).

## 12. Журнал доставки

**Что.** Записи `Broker → Subscriber (attempt n)`, `Subscriber → SUCCESS / FAIL / UNAVAILABLE`, `QUEUED`, `REDELIVERY`, `ALL DONE`.

**Где.** `DeliverAsync` и `History` в [Broker.cs](src/TicketSales.Server/PubSub/Broker.cs).

**Как работает.** Каждая доставка сохраняется в `DeliveryResult`:

| Поле | Значение |
| --- | --- |
| `Broker` | Broker, который доставлял |
| `EventType` | тип события |
| `Subscriber` | получатель |
| `Status` | результат |
| `Attempts` | число попыток |
| `Started`, `Finished` | начало и конец доставки |
| `Error` | ошибка |

Последние 10 доставок клиент видит в пункте меню 7 «Последние доставки».

## 13. Асинхронная обработка

**Что.** Subscriber обрабатывают событие параллельно и независимо от Publisher и клиента.

**Где.** `Task.Run(() => DeliverAllAsync(...))` в `Broker.Publish` и `Task.WhenAll(... Task.Run(DeliverAsync))`.

**Как работает.** Клиент получает ответ сразу после публикации: `Request → SUCCESS` в 10:42:24.627, а SeatService закончил в 10:42:27.624. Все три Subscriber стартуют в 10:42:24.619. Общее время равно самой долгой обработке (3 с), а не сумме задержек (6 с).

## 14. Искусственная задержка

**Что.** У каждого Subscriber своя задержка `Delay`:

| Subscriber | Задержка |
| --- | --- |
| Logger | 1 с |
| NotificationService | 2 с |
| SeatService | 3 с |
| AnalyticsService | 0,5 с |

Пауза между повторами — 0,3 с.

**Где.** `PubSubSystem.Create(delayFactor)`: для тестов `delayFactor = 0`.

**Как работает.** Анализ задержек (Часть 5, Требование 6), событие `TicketPurchased`:

| Subscriber | Задержка | Начало доставки, мс от публикации | Обработка, мс | Завершение, мс от публикации |
| --- | --- | --- | --- | --- |
| Logger | 1 с | 0 | 1002 | 1002 |
| NotificationService | 2 с | 0 | 2004 | 2004 |
| SeatService | 3 с | 0 | 3005 | 3005 |

| Этап | Время |
| --- | --- |
| Публикация (Publisher → ACCEPTED) | < 1 мс |
| Ответ клиенту после публикации | 9 мс |
| Доставка (старт обработки) | ≈ 1 мс |
| Завершение всех Subscriber | 3006 мс, определяется самым медленным (SeatService) |

## 15. Отказ одного Subscriber

**Что.** SeatService становится `UNAVAILABLE`.

**Где.** Флаг `Subscriber.Available`; операция `SetPubSubAvailability` (меню 8).

**Как работает.** `HandleAsync` выбрасывает `SubscriberUnavailableException`:

```
[10:42:28.142] [Client 2] SeatService → UNAVAILABLE
```

## 16. Обработка отказа

**Что.** Broker перехватывает ошибку, повторяет доставку, затем ставит событие в очередь.

**Где.** `catch` в [Broker.cs:134](src/TicketSales.Server/PubSub/Broker.cs#L134) `DeliverAsync`.

**Как работает.** В журнале зафиксированы: факт отказа, Subscriber, событие, сообщение об ошибке и результат доставки остальным.

```
[10:42:28.183] [Client 2] Broker-1 → Logger SeatReserved (attempt 1)
[10:42:28.183] [Client 2] Broker-1 → SeatService SeatReserved (attempt 1)
[10:42:28.183] [Client 2] SeatService → UNAVAILABLE SeatReserved (attempt 1): SeatService недоступен
[10:42:28.484] [Client 2] SeatService → UNAVAILABLE SeatReserved (attempt 2): SeatService недоступен
[10:42:28.785] [Client 2] SeatService → UNAVAILABLE SeatReserved (attempt 3): SeatService недоступен
[10:42:28.786] [Client 2] Broker-1 → QUEUED SeatReserved для SeatService (в очереди: 1)
[10:42:29.185] [Client 2] Logger → SUCCESS SeatReserved (attempt 1, 1002 ms)
[10:42:29.185] [Client 2] Broker-1 → ALL DONE SeatReserved: SUCCESS 1/2, 1002 ms от публикации
```

## 17. Продолжение работы других Subscriber

**Что.** Logger успешно обработал то же событие, сервер и Broker продолжили работу. Клиент получил `ReserveSeat → OK`.

**Где.** Тест `UnavailableSubscriber_IsQueued_OthersContinue`. После отказа тест публикует ещё одно событие и проверяет, что Broker его доставил.

**Как работает.** Каждая доставка — отдельная задача со своим `try/catch`, поэтому исключение одного Subscriber не прерывает `Task.WhenAll` и доставку остальным.

## 18. Добавление нового Subscriber

**Что.** `AnalyticsService` создан при запуске, но не подписан ни на что. Подписку он получает во время работы по запросу клиента (`Subscribe`, меню 10).

**Где.** [Subscriber.cs:95](src/TicketSales.Server/PubSub/Subscriber.cs#L95), [PubSubHandlers.cs](src/TicketSales.Server/Dispatching/Handlers/PubSubHandlers.cs) `SubscribeHandler`.

**Как работает.** Publisher и сервер не изменялись: тот же объект `Publisher`, тот же вызов `Publish`.

```
[10:42:33.723] [Client 2] Broker-1 → SUBSCRIBE AnalyticsService: TicketPurchased
[10:42:33.724] [Client 2] Broker-1 → SUBSCRIBE AnalyticsService: SeatReserved
[10:42:33.725] [Client 2] Broker-1 → SUBSCRIBE AnalyticsService: TicketCancelled
```

## 19. Получение события новым Subscriber

**Что.** После подписки AnalyticsService получает `TicketCancelled`.

**Где.** Тест `NewSubscriber_ReceivesEvent_WithSamePublisher`. До подписки событие не приходит, после — приходит, при этом `Assert.Same(publisher, …)`.

**Как работает.**

```
[10:42:33.764] [Client 2] Broker-1 → получатели TicketCancelled: Logger, SeatService, NotificationService, AnalyticsService
[10:42:34.269] [Client 2] AnalyticsService → SUCCESS TicketCancelled (attempt 1, 504 ms)
[10:42:36.770] [Client 2] Broker-1 → ALL DONE TicketCancelled: SUCCESS 4/4, 3006 ms от публикации
```

## 20. Сравнение прямого взаимодействия и Pub/Sub

| Критерий | Прямое взаимодействие (Server → Logger, SeatService, …) | Pub/Sub (Server → Publisher → Broker → Subscriber) |
| --- | --- | --- |
| Кто о ком знает | Сервер знает каждый компонент, его адрес и интерфейс | Сервер знает только Publisher. Publisher знает только Broker. Subscriber знает только Broker |
| Где маршрутизация | В коде сервера: последовательность вызовов | В Broker: таблица подписок `тип → Subscriber` |
| Добавить компонент | Изменить и пересобрать сервер | Зарегистрировать Subscriber и подписку (`Subscribe`); Publisher не меняется |
| Отказ одного компонента | Исключение в цепочке вызовов, операция может прерваться или зависнуть | Broker фиксирует ошибку, повторяет, ставит в очередь; остальные работают |
| Время ответа клиенту | Сумма времени всех вызовов (1+2+3 = 6 с) | Время публикации (мс), обработка асинхронна |
| Связанность | Сильная (compile-time и runtime) | Слабая: общий только контракт события |
| Минусы | — | Нет мгновенного результата обработки, сложнее отладка, нужна доставка «не менее одного раза» |

## 21. Unit-тесты

**Где.**
- [tests/TicketSales.Tests/PubSubTests.cs](tests/TicketSales.Tests/PubSubTests.cs): 16 тестов в памяти, без БД.
- [PubSubIntegrationTests.cs](tests/TicketSales.Tests/PubSubIntegrationTests.cs): 3 теста через TCP и PostgreSQL.

| Требование | Тест |
| --- | --- |
| создание события | `Event_IsCreatedWithTypeEntityTimestampAndData` |
| некорректное / неизвестное событие | `InvalidOrUnknownEvent_IsRejected` (2 случая) |
| публикация | `Publish_IsAcceptedByBroker_AndDeliveredToSubscriber` |
| регистрация Subscriber | `Subscribe_RegistersSubscriptionInEveryBroker` |
| незарегистрированный Subscriber / неизвестный тип | `Subscribe_UnknownSubscriberOrEventType_Fails`, `UnknownSubscriber_ReturnsError` |
| получение подписанным | `Publish_IsAcceptedByBroker_AndDeliveredToSubscriber`, `ReserveSeat_PublishesSeatReserved_ToSubscribers` |
| нет доставки неподписанному | `NotSubscribedSubscriber_DoesNotReceiveEvent` |
| одно событие нескольким | `OneEvent_IsDeliveredToAllSubscribers` |
| несколько типов | `SeveralEventTypes_AreRoutedBySubscription` |
| нет подписчиков | `NoSubscribers_EventIsAcceptedWithoutDeliveries` |
| ошибка Subscriber, работа Broker продолжается | `UnavailableSubscriber_IsQueued_OthersContinue` |
| новый Subscriber, получение, Publisher без изменений | `NewSubscriber_ReceivesEvent_WithSamePublisher` |
| несколько Broker | `FirstBrokerUnavailable_PublisherUsesSecondBroker`, `AllBrokersUnavailable_PublisherReturnsClearError` |
| очередь | `QueuedEvent_IsNotLost_AndRedeliveredAfterRecovery` |
| повторная доставка | `TransientFailures_AreRetried_UntilSuccess` |
| статистика | `Status_ShowsSubscriptionsQueueAndStatistics` |
| несколько клиентов | `SeveralClients_EventsAreDeliveredPerClient` |

**Как работает.** `dotnet test` — 48 тестов, включая тесты ЛР №1–5. Все проходят.

## 22. Обработка ошибок

| Ситуация | Что происходит |
| --- | --- |
| Сервер недоступен | Клиент: `ServerUnavailableException` → «Не удалось подключиться» (ЛР №1) |
| Broker недоступен | Publisher переключается на следующий Broker |
| Все Broker недоступны | `PublishResult.Success = false`, `Publisher → FAIL: Broker недоступен: …`. Операция клиента выполнена, сервер работает |
| Publisher не может опубликовать (некорректное событие) | `Validate()` → `PublishResult` с ошибкой, без исключения |
| Subscriber недоступен / ошибка соединения | `UNAVAILABLE`, 3 попытки, затем `QUEUED` |
| Ошибка обработки события | `FAIL (attempt n): …`, повтор |
| Несколько Subscriber недоступны | Каждый независимо уходит в очередь, доступные получают событие |
| Subscriber не зарегистрирован | `NOT_FOUND: Subscriber 'StockService' не зарегистрирован` |
| Неизвестный тип события | `VALIDATION_ERROR: Неизвестный тип события: 'OrderCreated'` |
| Нет подписки на тип | `NO SUBSCRIBERS for …`, событие принято без доставок |
| Некорректный запрос клиента | `VALIDATION_ERROR` / `UNKNOWN_OPERATION` (ЛР №1) |

Пример, когда недоступны оба Broker:

```
[10:42:44.865] [Client 3] Publisher → PUBLISH SeatReserved(EntityId=5, Client 3)
[10:42:44.865] [Client 3] Publisher → Broker-1 UNAVAILABLE, переключение на следующий Broker
[10:42:44.865] [Client 3] Publisher → Broker-2 UNAVAILABLE, переключение на следующий Broker
[10:42:44.865] [Client 3] Publisher → FAIL: Broker недоступен: Broker-1 недоступен; Broker-2 недоступен
[10:42:44.866] [Client 3] Request → SUCCESS (экземпляр: Primary)
>>> Клиент: Subscribe → ОШИБКА NOT_FOUND: Subscriber 'StockService' не зарегистрирован
>>> Клиент: Subscribe → ОШИБКА VALIDATION_ERROR: Неизвестный тип события: 'OrderCreated'
```

## 23. Работа с несколькими Broker

**Что.** `Broker-1` — основной, `Broker-2` — резервный. Подписки регистрируются на обоих.

**Где.** `PubSubSystem.Create`; цикл по Broker в [Publisher.cs](src/TicketSales.Server/PubSub/Publisher.cs).

**Как работает.**

```
[10:42:41.299] [Client 3] Broker-1 → UNAVAILABLE
[10:42:41.335] [Client 3] Publisher → Broker-1 UNAVAILABLE, переключение на следующий Broker
[10:42:41.335] [Client 3] Broker-2 → EVENT RECEIVED SeatReserved(EntityId=4, Client 3)
[10:42:41.335] [Client 3] Publisher → ACCEPTED by Broker-2
[10:42:44.337] [Client 3] Broker-2 → ALL DONE SeatReserved: SUCCESS 3/3, 3003 ms от публикации
```

**Анализ.**
- **Зачем несколько Broker:** отказоустойчивость (нет единой точки отказа), распределение нагрузки, разделение по типам событий или регионам.
- **При отказе одного Broker:** Publisher переключается на другой, и события продолжают доставляться. Но очередь отказавшего Broker недоступна, пока он не восстановится. Если отказали все Broker, событие теряется: операция в БД выполнена, а событие нет. В промышленных системах это решают паттерном Transactional Outbox.
- **Дополнительные проблемы:**
  - подписки нужно синхронизировать между Broker;
  - при повторной публикации в другой Broker возможны дубли, поэтому Subscriber должны быть идемпотентны (по `Event.Id`);
  - нет глобального порядка событий;
  - сложнее мониторинг и согласованность очередей.

## 24. Очередь событий

**Что.** `Broker.Pending` (`ConcurrentQueue`) — временное хранилище событий, которые не удалось доставить после всех попыток.

**Где.** `Pending.Enqueue` в `DeliverAsync`, [Broker.cs:109](src/TicketSales.Server/PubSub/Broker.cs#L109) `RedeliverPendingAsync`.

**Как работает.** Когда Subscriber восстанавливается (`SetPubSubAvailability … Available = true`), все Broker повторно доставляют события из очереди. Событие не теряется:

```
    Broker-1: AVAILABLE, в очереди 1
    SeatService: UNAVAILABLE, ..., обработано 2
[10:42:30.703] [Client 2] SeatService → AVAILABLE
[10:42:30.704] [Client 2] Broker-1 → REDELIVERY из очереди: 1
[10:42:30.705] [Client 2] Broker-1 → SeatService SeatReserved (attempt 1)
[10:42:33.709] [Client 2] SeatService → SUCCESS SeatReserved (attempt 1, 3005 ms)
```

## 25. Повторная доставка

**Что.** До 3 попыток с паузой `RetryDelay` между ними.

**Где.** Цикл `for (attempt…)` в `DeliverAsync`. Временный сбой моделирует `Subscriber.TransientFailures` (меню 11).

**Как работает.** Attempt 1 → FAIL, Attempt 2 → FAIL, Attempt 3 → SUCCESS:

```
[10:42:37.270] [Client 1] Logger → AVAILABLE, временных ошибок: 2
[10:42:38.296] [Client 1] Logger → FAIL SeatReserved (attempt 1): Logger: временная ошибка обработки SeatReserved
[10:42:38.597] [Client 1] Broker-1 → Logger SeatReserved (attempt 2)
[10:42:39.599] [Client 1] Logger → FAIL SeatReserved (attempt 2): Logger: временная ошибка обработки SeatReserved
[10:42:39.902] [Client 1] Broker-1 → Logger SeatReserved (attempt 3)
[10:42:40.903] [Client 1] Logger → SUCCESS SeatReserved (attempt 3, 3608 ms)
```

## 26. Статистика по событиям

**Что.** `AnalyticsService` — новый тип Subscriber. Он не изменяет данные предметной области и считает события по типам и по клиентам.

**Где.** [Subscriber.cs:95](src/TicketSales.Server/PubSub/Subscriber.cs#L95); выдача через `GetPubSubStatus` (меню 7).

**Как работает.** Состояние в конце демо:

```
    Logger: AVAILABLE, подписки [TicketPurchased, SeatReserved, TicketCancelled], обработано 9
    SeatService: AVAILABLE, подписки [TicketPurchased, SeatReserved, TicketCancelled], обработано 9
    NotificationService: AVAILABLE, подписки [TicketPurchased, TicketCancelled], обработано 2
    AnalyticsService: AVAILABLE, подписки [TicketPurchased, SeatReserved, TicketCancelled], обработано 6
    AnalyticsService по типам: SeatReserved=5, TicketCancelled=1
    AnalyticsService по клиентам: Client 2=2, Client 1=2, Client 3=2
```

## 27. Работа с несколькими клиентами

**Что.** Client 1, 2 и 3 одновременно бронируют места 6, 7 и 8.

**Где.** Демо и тест `SeveralClients_EventsAreDeliveredPerClient`.

**Как работает.** Каждый запрос получает своё событие с собственным `ClientId`. Broker доставляет их параллельно, журналы не смешивают клиентов:

```
[10:42:44.882] [Client 1] Publisher → PUBLISH SeatReserved(EntityId=6, Client 1)
[10:42:44.894] [Client 2] Publisher → PUBLISH SeatReserved(EntityId=7, Client 2)
[10:42:44.907] [Client 3] Publisher → PUBLISH SeatReserved(EntityId=8, Client 3)
[10:42:45.383] [Client 1] AnalyticsService → SUCCESS SeatReserved (attempt 1, 501 ms)
[10:42:45.395] [Client 2] AnalyticsService → SUCCESS SeatReserved (attempt 1, 501 ms)
[10:42:45.409] [Client 3] AnalyticsService → SUCCESS SeatReserved (attempt 1, 501 ms)
[10:42:47.883] [Client 1] Broker-1 → ALL DONE SeatReserved: SUCCESS 3/3, 3001 ms от публикации
[10:42:47.894] [Client 2] Broker-1 → ALL DONE SeatReserved: SUCCESS 3/3, 3001 ms от публикации
[10:42:47.908] [Client 3] Broker-1 → ALL DONE SeatReserved: SUCCESS 3/3, 3001 ms от публикации
```

## Итоговый эксперимент (Часть 4, Требование 3)

`dotnet run --project src/TicketSales.PubSubDemo` выполняет весь цикл:

1. Сервер выполняет операцию.
2. Формируется Event.
3. Publisher публикует Event.
4. Broker принимает Event.
5. Broker определяет подписчиков.
6. Несколько Subscriber получают Event.
7. SeatService становится недоступным.
8. Остальные Subscriber продолжают работу.
9. Подключается AnalyticsService.
10. AnalyticsService получает событие.

Затем демо показывает Часть 5: очередь, повторы, Broker-2, все Broker недоступны, ошибки, несколько клиентов.

---

# Ответы на контрольные вопросы

1. **Что такое Pub/Sub?** Модель взаимодействия, в которой отправитель публикует событие определённого типа, не зная получателей. Посредник (Broker) доставляет событие всем, кто подписался на этот тип.

2. **Что такое Publisher?** Компонент, который публикует события. Здесь это `Publisher`: получает событие от сервера и передаёт в Broker. С Subscriber напрямую не общается.

3. **Что такое Broker?** Посредник. Принимает события, хранит подписки, определяет получателей, доставляет события, обрабатывает ошибки, повторяет доставку и хранит очередь недоставленных событий.

4. **Что такое Subscriber?** Компонент, который регистрирует интерес к типам событий и обрабатывает полученные события. Здесь это Logger, SeatService, NotificationService и AnalyticsService.

5. **Что такое Event?** Сообщение о факте, который уже произошёл (например, `SeatReserved`), а не команда. Содержит тип, id объекта, время, клиента и дополнительные данные.

6. **Как взаимодействуют Publisher и Broker?** Publisher вызывает `Broker.Publish(event)`. Broker проверяет событие и сразу подтверждает приём (`ACCEPTED`), а доставку выполняет асинхронно. Если Broker недоступен, Publisher получает исключение, пробует следующий Broker и возвращает понятный `PublishResult`.

7. **Может ли один Subscriber получать несколько типов событий?** Да. Logger и SeatService подписаны на все три типа, NotificationService — на два.

8. **Что такое слабая связанность компонентов?** Компоненты зависят только от общего контракта (формата события и Broker), а не друг от друга. Их можно изменять, добавлять, удалять и запускать независимо.

9. **Почему Publisher не должен напрямую обращаться к Subscriber?** Иначе Publisher должен знать состав, адреса и интерфейсы всех получателей. Любой новый компонент потребовал бы изменения Publisher, а отказ или медленная работа Subscriber блокировали бы публикацию.

10. **Для чего используется асинхронная обработка событий?** Чтобы операция и ответ клиенту не ждали обработки (ответ за миллисекунды вместо 3–6 с), чтобы Subscriber работали параллельно и чтобы отказ одного Subscriber не задерживал остальных.

11. **Как проверить, что Subscriber не получает событие, на которое он не подписан?** Опубликовать событие и проверить, что у неподписанного Subscriber пусто (`Received` / счётчик «обработано»), а в журнале его нет в списке получателей. Тест `NotSubscribedSubscriber_DoesNotReceiveEvent`: после `SeatReserved` у NotificationService и AnalyticsService ничего нет.

12. **Что произойдёт при отсутствии подписчиков на событие?** Broker примет событие, запишет `NO SUBSCRIBERS for …` и завершит доставку с пустым результатом. Публикация успешна, ошибки нет. Событие никуда не попадает, поскольку оно не хранится для будущих подписчиков.

13. **Что произойдёт, если Broker недоступен?** Publisher получит `BrokerUnavailableException` и переключится на Broker-2. Если недоступны все Broker, он вернёт `Success=false` с сообщением «Broker недоступен». Сервер не падает, операция клиента уже выполнена, но событие потеряно (нужен outbox).

14. **Какие преимущества даёт Pub/Sub при добавлении новых компонентов?** Новый Subscriber подключается только регистрацией подписки, без изменения Publisher, сервера и других Subscriber. Пример — AnalyticsService, подключённый во время работы.

15. **Какие недостатки может иметь Pub/Sub?**
    - Нет немедленного результата обработки (eventual consistency).
    - Broker — дополнительный компонент и потенциальная точка отказа.
    - Сложнее отладка и трассировка цепочки.
    - Возможны дубли и нарушение порядка событий.
    - Нужны идемпотентность, очереди и мониторинг.

16. **Какие проблемы могут возникнуть при большом количестве событий?**
    - Рост очередей и памяти Broker.
    - Увеличение задержки доставки.
    - Перегрузка медленных Subscriber, поэтому нужны back-pressure и ограничение параллелизма.
    - Лавина повторов при массовых отказах.
    - Потеря событий при переполнении или отказе Broker без персистентности.
    - Сложность сохранения порядка.
    - Нагрузка на журналы и мониторинг.
