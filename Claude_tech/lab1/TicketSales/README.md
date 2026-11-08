# ЛР №9 — развертывание TicketSales в Kubernetes

Вариант 6 «Продажа билетов». Сервер из ЛР №8 (образ `ticketsales-server:1.0`) развернут в локальном кластере Minikube.
Подробная теория: [docs/kubernetes-theory.md](docs/kubernetes-theory.md).

Другие лабораторные — по тегам: `git checkout claude-tech-lab8` (Docker Compose), `claude-tech-lab7` и т.д.

## Что изменено

| Что | Где | Как работает |
|---|---|---|
| ConfigMap с `APP_MODE` | [k8s/configmap.yaml](k8s/configmap.yaml) | `ticketsales-config`, ключ `APP_MODE: "Kubernetes"` — неконфиденциальная настройка |
| Deployment сервера | [k8s/deployment.yaml](k8s/deployment.yaml) | `ticketsales-server`, `replicas: 1`, метка `app: ticketsales-server`, образ `ticketsales-server:1.0` (`IfNotPresent`), порт 5050. `APP_MODE` берется из ConfigMap (`configMapKeyRef`), пароль БД — из Secret (`secretKeyRef`), адреса БД — имена Service (`postgres-primary`, `postgres-replica`), не localhost. `readinessProbe` (TCP 5050): Pod попадает в Service только когда сервер реально слушает порт |
| Service сервера | [k8s/service.yaml](k8s/service.yaml) | `ClusterIP`, `selector: app: ticketsales-server`, `port 5050 → targetPort 5050` |
| PostgreSQL в кластере | [k8s/postgres.yaml](k8s/postgres.yaml) | Primary и Replica: Deployment + Service + PVC (данные переживают пересоздание Pod БД). Метка `tier: database`, `readinessProbe` — `pg_isready` |
| Использование `APP_MODE` | [ServerSettings.cs](src/TicketSales.Server/Configuration/ServerSettings.cs) (`DatabaseHint`), [Program.cs](src/TicketSales.Server/Program.cs) | При старте пишет `Application mode: <режим>, instance: <имя Pod>`. Если БД недоступна, в режиме `Kubernetes` подсказка — `kubectl get pods,services -l tier=database`, иначе — `docker compose up -d` |
| Исправление: пустой `APP_MODE` | [ServerSettings.cs](src/TicketSales.Server/Configuration/ServerSettings.cs) (`Env`) | Значение из пробелов раньше давало пустой режим; теперь `Trim` выполняется до проверки → `Local` |
| Безопасный старт нескольких Pod | [PostgresSchemaInitializer.cs](src/TicketSales.Server/Storage/Postgres/PostgresSchemaInitializer.cs) | Создание схемы и начальное заполнение выполняются в транзакции под `pg_advisory_xact_lock`: 3 Pod на пустой БД не создают таблицы и данные дважды |
| Unit-тесты | [ServerSettingsTests.cs](tests/TicketSales.Tests/ServerSettingsTests.cs) | `APP_MODE=Kubernetes` → режим и подсказка для k8s; пустое/из пробелов значение → `Local` |
| Тег образа | [compose.yaml](compose.yaml) | `docker compose build server` собирает `ticketsales-server:1.0` — тот же образ использует Deployment |
| Удалено (демо ЛР №8) | — | `TicketSales.LoadTest`, `DockerComposeIntegrationTests`, Dockerfile и сервис клиента в compose — клиент в ЛР №9 запускается локально |

## Запуск

```bash
# 0. Кластер (один из вариантов) и образ
minikube start --driver=docker              # или Docker Desktop → Settings → Kubernetes → Enable
kubectl config current-context              # minikube / docker-desktop
docker compose build server                 # ticketsales-server:1.0
minikube image load ticketsales-server:1.0  # для Docker Desktop (режим kubeadm) не нужно

# 1. Secret с паролем БД (в репозиторий не попадает)
kubectl create secret generic ticketsales-db --from-literal=POSTGRES_PASSWORD=<пароль>

# 2. БД, ConfigMap, сервер, Service
kubectl apply -f k8s/postgres.yaml
kubectl apply -f k8s/configmap.yaml -f k8s/deployment.yaml -f k8s/service.yaml
kubectl rollout status deployment/ticketsales-server

# 3. Доступ с компьютера (отдельный терминал) и клиент
kubectl port-forward service/ticketsales-server 5050:5050
dotnet run --project src/TicketSales.Client   # адрес 127.0.0.1, порт 5050

# Удаление
kubectl delete -f k8s/ && kubectl delete secret ticketsales-db
```

Тесты: `docker compose up -d postgres-primary postgres-replica && dotnet test` → **93/93 пройдено**.

## Результаты (Minikube v1.39.0, Kubernetes v1.37.0)

### Часть 1. Развертывание

| Параметр | Результат |
|---|---|
| Среда Kubernetes | Minikube (driver docker) |
| Текущий контекст | `minikube` |
| Количество узлов | 1 (`minikube`, Ready, control-plane) |
| Имя Deployment | `ticketsales-server` |
| Образ и тег | `ticketsales-server:1.0` |
| Заданное / фактическое число Pod | 1 / 1 |
| Состояние Pod | `Running`, `1/1 Ready` |
| Запуск приложения | `Application mode: Kubernetes, instance: ticketsales-server-587789b655-8lp5w`, `БД доступна (попытка 1/30)`, `Сервер … запущен на порту 5050 (режим Kubernetes)` |
| Ошибки | 1) Docker Desktop: Kubernetes был выключен (включается только в GUI) → использован Minikube. 2) Локальный образ не виден кластеру Minikube → `minikube image load` |

### Часть 2. Service

| Параметр | Результат |
|---|---|
| Имя / тип | `ticketsales-server` / `ClusterIP` (10.101.251.130) |
| port / targetPort | 5050 / 5050 |
| Селектор | `app=ticketsales-server` |
| Совпадение меток | Да: метки Pod `app=ticketsales-server`, `Endpoints: 10.244.0.5:5050` |
| Доступ | `kubectl port-forward service/ticketsales-server 5050:5050` → клиент `127.0.0.1:5050` |
| Операция 1 — `GetEvents` | 2 мероприятия, свободно 10 и 8 мест |
| Операция 2 — `ReserveSeat` (событие 1, место 1) | `Место забронировано. TicketId = 1` (в логе `LOCK → COMMIT`) |
| Операция 3 — `BuyTicket` (билет 1) | `Билет куплен. PaymentId = 1, сумма 1500, статус Completed` |
| Повторная бронь места 1 | Ожидаемая ошибка `Место уже занято` (`VALIDATION_ERROR`, `ROLLBACK`) |
| Ошибки | Нет необработанных |

### Часть 3. Масштабирование и восстановление

| Эксперимент | Задано | Фактически | Результат |
|---|---|---|---|
| Первоначальное развертывание | 1 | 1 | `8lp5w` Running |
| Масштабирование вверх | 3 | 3 | Созданы `6pmgn`, `wmntl` (сначала `ContainerCreating`), через ~5 с `3/3`; Service: 3 Endpoints |
| Масштабирование вниз | 2 | 2 | Удален один из созданных при масштабировании — `wmntl`; `8lp5w`, `6pmgn` продолжают работать |
| Повторно вверх | 3 | 3 | Создан `52rht`, `3/3` |
| Удаление Pod `6pmgn` | 3 | 3 → 2 Running | Сразу: `6pmgn Terminating` и уже создан `zvcqq` (`ContainerCreating`) |
| Восстановление | 3 | 3 | `zvcqq`: `0/1 Running` (readiness еще не пройдена) → `1/1` через ~10 с. Клиент: бронь, покупка, отмена билета 2 — успешно |

Наблюдения:
- Service распределяет соединения: 9 запросов изнутри кластера (`busybox` + `nc ticketsales-server 5050`) обработаны Pod-ами `wmntl` (6) и `6pmgn` (3) — по журналам.
- `kubectl port-forward service/…` выбирает **один** Pod: все 6 операций клиента ушли в `8lp5w`. После удаления `8lp5w` port-forward завершился с `error: lost connection to pod`, клиент — `Сервер 127.0.0.1:5050 недоступен`. Решение — перезапустить port-forward.

Ответы:
1. **Почему создана замена?** Deployment хранит желаемое состояние (`replicas: 3`); контроллер ReplicaSet увидел 2 Pod вместо 3 и создал новый по шаблону (событие `SuccessfulCreate`).
2. **Как Deployment знает число экземпляров?** Поле `spec.replicas` (меняется `kubectl scale` или `apply`); свои Pod он находит по `selector.matchLabels`.
3. **Почему число Pod временно отличается?** Создание Pod, загрузка образа, запуск процесса и readinessProbe занимают время; удаляемый Pod какое-то время в `Terminating`.
4. **Почему 3 Pod не спасают данные при отказе БД?** Все Pod сервера используют одну и ту же БД; Pod сервера не хранят данные. При отказе БД все 3 не могут выполнять операции (проверено ниже).
5. **Почему масштабирование не ускоряет?** Узкое место — общая БД и блокировки строк мест; все Pod на одном узле делят CPU; через port-forward нагрузка вообще идет в один Pod.

### Часть 4. Конфигурация и диагностика

| Параметр | Результат |
|---|---|
| Имя ConfigMap / ключ | `ticketsales-config` / `APP_MODE` |
| Переменная окружения | `APP_MODE` |
| Значение | `Kubernetes` |
| Получает ли приложение | Да: `kubectl describe pod` → `APP_MODE: <set to the key 'APP_MODE' of config map 'ticketsales-config'>`, `kubectl exec … printenv APP_MODE` → `Kubernetes` |
| Подтверждение использования | Журнал: `Application mode: Kubernetes`, `Mode=Kubernetes`, `режим Kubernetes`; подсказка при недоступной БД — для Kubernetes |
| Работоспособность | Сохранена |

Изменение ConfigMap: после `kubectl patch configmap … APP_MODE=Kubernetes-Test` работающий Pod продолжал видеть `Kubernetes` (переменная окружения фиксируется при старте); после `kubectl rollout restart deployment/ticketsales-server` новые Pod пишут `режим Kubernetes-Test`. Затем возвращено `Kubernetes`.

**Намеренная ошибка — несуществующий тег `ticketsales-server:9.9`:**

| Шаг | Наблюдение |
|---|---|
| `kubectl apply` | `deployment.apps/ticketsales-server configured` — применилось, но это не значит, что работает |
| `kubectl get pods` | Новый Pod `ErrImagePull` → `ImagePullBackOff`; старый Pod остался `Running` (RollingUpdate не удаляет старые, пока новые не готовы) |
| `kubectl describe pod` | `Failed to pull image "ticketsales-server:9.9": … pull access denied, repository does not exist` |
| Причина | Образа с таким тегом нет ни в кластере, ни в Docker Hub |
| Исправление | Вернул `1.0`, `kubectl apply -f k8s/deployment.yaml` → `successfully rolled out`, все Pod `ticketsales-server:1.0` |
| Побочное наблюдение | `apply` вернул `replicas: 1` из файла (ручной `scale` перезаписывается) → снова `kubectl scale --replicas=3` |

**Отказ зависимого компонента (БД):** `kubectl scale deployment postgres-primary postgres-replica --replicas=0` + удаление Pod сервера → новый Pod `0/1 Running`, в Endpoints Service не попал; журнал: `БД не готова (попытка 1/30) … (29/30)`, затем `Не удалось подключиться к базе данных` и `Проверьте Pod и Service БД: kubectl get pods,services -l tier=database`; контейнер перезапущен (`RESTARTS 1`). После возврата БД Pod стал `1/1`, Endpoints — 3. Данные сохранились (PVC): у мероприятия 1 осталось 9 свободных мест.

**Итоговое тестирование** (3/3 Pod, после всех экспериментов): `GetEvents` (9 и 8 мест) → `ReserveSeat` событие 2, место 11 → `TicketId = 3` → `BuyTicket` → `PaymentId = 3, 800, Completed`; повторная бронь → `Место уже занято`; некорректный email → `Некорректный email покупателя`; статус кластера узлов — `Node 5 LEADER`. В журналах всех Pod 0 исключений.

### Сравнение Docker и Kubernetes

| Критерий | Docker / Compose (ЛР №8) | Kubernetes (ЛР №9) |
|---|---|---|
| Основная задача | Собрать образ и запустить контейнеры на одной машине | Поддерживать желаемое состояние приложения в кластере |
| Описание конфигурации | `Dockerfile`, `compose.yaml`, `.env` | YAML-ресурсы: Deployment, Service, ConfigMap, Secret, PVC |
| Запуск | `docker compose up -d` | `kubectl apply -f k8s/` |
| Управление экземплярами | Контейнер = 1 экземпляр, `container_name` фиксирован | `replicas`, ReplicaSet следит за количеством Pod |
| Организация доступа | Публикация порта `5050:5050`, DNS по имени сервиса в сети | Service (ClusterIP, балансировка по меткам) + `port-forward` |
| Изменение числа экземпляров | `--scale` вручную, конфликт портов/имен | `kubectl scale --replicas=N` |
| Удаление контейнера / Pod | `restart: on-failure` перезапускает только при сбое, удаленный контейнер не возвращается | Удаленный Pod сразу заменяется новым |
| Диагностика | `docker ps`, `docker logs`, `docker inspect` | `get`, `describe`, `logs`, `get events`, статусы `ErrImagePull`, `CrashLoopBackOff`, readiness |
| Требования к среде | Только Docker Engine | Кластер (Minikube / Docker Desktop k8s), kubectl, образ доступен кластеру |
