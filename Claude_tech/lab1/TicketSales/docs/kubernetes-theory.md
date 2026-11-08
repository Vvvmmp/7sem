# Kubernetes — теория к ЛР №9

Примеры — на манифестах этого проекта ([k8s/](../k8s)).

## 1. Зачем нужен Kubernetes

Docker решает задачу «упаковать приложение и запустить контейнер». Когда контейнеров много, появляются новые задачи:

- держать N экземпляров сервиса и автоматически заменять упавшие;
- давать им стабильный адрес, хотя контейнеры пересоздаются и меняют IP;
- обновлять версию без остановки (по одному экземпляру);
- хранить конфигурацию и секреты отдельно от образа;
- распределять контейнеры по нескольким машинам.

**Kubernetes (k8s)** — платформа оркестрации контейнеров. Главная идея — **декларативность**: вы описываете *желаемое состояние* («3 экземпляра сервера из образа `ticketsales-server:1.0`»), а Kubernetes постоянно сравнивает его с *фактическим* и устраняет расхождения. Этот цикл называется **reconciliation loop** (цикл согласования): наблюдать → сравнить → действовать → повторить.

Kubernetes **не собирает образы** (это делает Docker) и **не исправляет ошибки приложения** — он только запускает, перезапускает и соединяет контейнеры.

## 2. Архитектура кластера

```
┌──────────────── Control Plane ─────────────────┐
│ kube-apiserver   — единая точка входа (REST API) │  ← kubectl
│ etcd             — хранилище всего состояния      │
│ kube-scheduler   — выбирает узел для нового Pod   │
│ controller-manager — контроллеры (Deployment,     │
│                    ReplicaSet, Node, Endpoints…)  │
└───────────────────────────────────────────────────┘
          │
┌──────── Node (рабочий узел) ────────┐
│ kubelet        — запускает Pod-ы,    │
│                  выполняет probes    │
│ container runtime (containerd)       │
│ kube-proxy     — правила сети для    │
│                  Service             │
└──────────────────────────────────────┘
```

- **Cluster** — control plane + узлы.
- **Node** — физическая или виртуальная машина, где выполняются Pod-ы. В Minikube/Docker Desktop весь кластер — один узел (он же control plane).
- Количество узлов ≠ количество экземпляров приложения: в ЛР на 1 узле работают 3 Pod сервера + 2 Pod БД.

Что происходит при `kubectl apply -f deployment.yaml`:
1. kubectl отправляет YAML в API server → объект сохраняется в etcd.
2. Контроллер Deployment создает ReplicaSet.
3. Контроллер ReplicaSet видит «нужно 3, есть 0» → создает 3 объекта Pod (пока без узла, `Pending`).
4. Scheduler назначает каждому Pod узел.
5. kubelet на узле скачивает образ (если нужно), запускает контейнер, выполняет probes и докладывает статус.

Поэтому успешный `kubectl apply` означает лишь «объект принят», а не «приложение работает» — в ЛР это видно на примере `ErrImagePull`.

## 3. Основные объекты

### Pod
Минимальная единица развертывания: один или несколько контейнеров с общим сетевым пространством (один IP, общий `localhost`) и общими томами. Pod **эфемерен**: его не «чинят», а заменяют новым — с новым именем и новым IP.

Pod vs контейнер Docker: контейнер — процесс из образа; Pod — обертка Kubernetes вокруг одного/нескольких контейнеров + сеть + тома + метки + probes + политика перезапуска.

Фазы и состояния:

| Статус | Значение |
|---|---|
| `Pending` | Pod принят, но контейнеры еще не запущены (ждет узла, тома, скачивания образа) |
| `ContainerCreating` | Узел выбран, создается контейнер |
| `Running` | Контейнер запущен. **Не означает готовность** — смотрите колонку `READY` |
| `Succeeded` / `Failed` | Все контейнеры завершились успешно / с ошибкой (для задач-«джоб») |
| `ErrImagePull` / `ImagePullBackOff` | Образ не удалось получить; kubelet повторяет с растущей паузой |
| `CrashLoopBackOff` | Контейнер стартует и падает; паузы между перезапусками растут (10 с, 20 с … до 5 мин) |
| `Terminating` | Pod удаляется: процессу отправлен SIGTERM, ждем завершения (до 30 с), потом SIGKILL |
| `Unknown` | Нет связи с узлом |

### ReplicaSet
Поддерживает заданное число одинаковых Pod, находя «свои» по меткам. Напрямую обычно не используется — им управляет Deployment.

### Deployment
Описывает желаемое состояние приложения: шаблон Pod (образ, порты, env, probes) + `replicas` + стратегию обновления. При изменении шаблона (новый образ, новый env) создается новый ReplicaSet, и Pod-ы заменяются постепенно (**RollingUpdate**: по умолчанию `maxSurge 25%`, `maxUnavailable 25%`). Если новые Pod не становятся готовыми (как с `ticketsales-server:9.9`), старые продолжают работать — обновление «застревает», но сервис не падает. Откат: `kubectl rollout undo deployment/<имя>`.

Ключевые поля [k8s/deployment.yaml](../k8s/deployment.yaml):

```yaml
spec:
  replicas: 1                       # желаемое число Pod
  selector:
    matchLabels: { app: ticketsales-server }   # какие Pod считать своими
  template:
    metadata:
      labels: { app: ticketsales-server }      # метки создаваемых Pod (должны подходить под selector)
    spec:
      containers:
        - image: ticketsales-server:1.0
          imagePullPolicy: IfNotPresent        # брать локальный образ, если он есть на узле
          ports: [{ containerPort: 5050 }]     # документирует порт, ничего не публикует
```

`imagePullPolicy`: `Always` — всегда тянуть из реестра; `IfNotPresent` — только если нет на узле; `Never` — только локальный. Для тега `latest` по умолчанию `Always`, поэтому в ЛР используется явный тег `1.0`.

### Service
Стабильная точка доступа к группе Pod. Pod-ы приходят и уходят, а у Service постоянные имя и виртуальный IP (ClusterIP). Service выбирает Pod по **selector**, и в список адресов (**Endpoints / EndpointSlice**) попадают только **готовые** Pod (прошедшие readinessProbe).

```yaml
spec:
  type: ClusterIP
  selector: { app: ticketsales-server }
  ports:
    - port: 5050        # порт Service
      targetPort: 5050  # порт контейнера
```

Типы Service:

| Тип | Доступ |
|---|---|
| `ClusterIP` (по умолчанию) | Только внутри кластера |
| `NodePort` | + порт 30000–32767 на каждом узле |
| `LoadBalancer` | + внешний балансировщик (облако; в Minikube — `minikube tunnel`) |
| `ExternalName` | DNS-псевдоним внешнего имени |

**DNS:** внутри кластера Service доступен по имени: `postgres-primary`, полностью `postgres-primary.default.svc.cluster.local`. Поэтому сервер подключается к БД по `DB_PRIMARY_HOST=postgres-primary`, а не по `localhost` — `localhost` внутри Pod означает сам Pod.

Балансировка: kube-proxy распределяет **TCP-соединения** (не отдельные запросы) между Endpoints. Клиент TicketSales держит одно соединение, поэтому все его запросы идут в один Pod.

Типичные причины «Pod работает, а Service недоступен»:
- селектор не совпадает с метками Pod → `Endpoints: <none>`;
- `targetPort` не совпадает с портом, который реально слушает приложение;
- Pod не прошел readinessProbe;
- приложение слушает `127.0.0.1` вместо `0.0.0.0` (TicketSales слушает `IPAddress.Any`).

### Labels и selectors
Метка — пара ключ/значение (`app: ticketsales-server`, `tier: database`). Селектор — фильтр по меткам. Используются везде: Deployment → свои Pod, Service → Pod для трафика, kubectl → `kubectl get pods -l tier=database`.

### ConfigMap и Secret
- **ConfigMap** — неконфиденциальные настройки (режим, имена сервисов, флаги).
- **Secret** — пароли, токены. Хранится в base64 (это не шифрование!), но доступ к Secret можно ограничить RBAC, и он не кладется в репозиторий.

Передача в контейнер: через переменные окружения (`configMapKeyRef`, `secretKeyRef`, `envFrom`) или как файлы (volume).

**Важно:** переменные окружения читаются процессом один раз при старте. Изменение ConfigMap **не меняет** env у работающих Pod — нужен перезапуск: `kubectl rollout restart deployment/<имя>`. (ConfigMap, смонтированный как том, обновляется в файлах автоматически, но приложение должно само перечитать файл.)

Ошибки: неверное имя ConfigMap или ключа → Pod в `CreateContainerConfigError`, в `describe` — `configmap "…" not found` / `couldn't find key …`.

### PersistentVolume и PersistentVolumeClaim
Файловая система контейнера исчезает вместе с Pod. Для данных БД используется **PVC** — запрос «дайте мне 1 Gi диска»; StorageClass (в Minikube — `standard`, hostPath) создает **PV** и подключает его к Pod. Pod БД можно удалить, новый Pod подключит тот же PVC — данные сохранятся (проверено в ЛР).

Для баз данных в продакшене обычно применяют **StatefulSet** (стабильные имена Pod, свой PVC на каждый экземпляр), в ЛР достаточно Deployment с `strategy: Recreate`.

### Probes (проверки здоровья)
| Probe | Если не прошла |
|---|---|
| `readinessProbe` | Pod исключается из Endpoints Service (трафик не идет), но не перезапускается |
| `livenessProbe` | kubelet перезапускает контейнер |
| `startupProbe` | Пока не прошла, остальные probes не выполняются (для медленного старта) |

Виды: `tcpSocket` (порт открыт), `httpGet` (код 200–399), `exec` (команда вернула 0, например `pg_isready`). В ЛР readinessProbe сервера — TCP 5050: порт открывается только после подключения к БД, поэтому `Running 0/1` → `1/1` показывает момент реальной готовности.

### Namespace
Логическое разделение ресурсов в кластере (`default`, `kube-system`, …). В ЛР все в `default`.

## 4. Самовосстановление и его границы

| Событие | Кто реагирует | Что происходит |
|---|---|---|
| Процесс в контейнере упал | kubelet | Перезапуск контейнера **в том же Pod** (`RESTARTS +1`), при повторах — `CrashLoopBackOff` |
| Pod удален (`kubectl delete pod`) | ReplicaSet | Создается **новый Pod** с новым именем и IP |
| Узел недоступен | Node controller | Через ~5 мин Pod-ы переносятся на другие узлы (если они есть) |
| `replicas` изменено | ReplicaSet | Создает или удаляет Pod-ы |

Kubernetes **не гарантирует**:
- сохранность данных, хранившихся внутри Pod (без тома они пропадут);
- работоспособность при недоступной БД — Pod будет пересоздан, но операции выполнять не сможет;
- защиту от отказа единственного компьютера — в локальном кластере все Pod на одной машине;
- ускорение при масштабировании — общий ресурс (БД, блокировки строк, CPU одного узла) остается узким местом.

## 5. kubectl — шпаргалка

```bash
kubectl config get-contexts / current-context / use-context <имя>   # к какому кластеру обращаемся
kubectl cluster-info ; kubectl get nodes
kubectl apply -f <файл|каталог>          # создать/обновить (декларативно)
kubectl delete -f <файл|каталог>
kubectl get pods|deployments|services|configmaps|events [-o wide] [-l app=…] [-w]
kubectl describe pod|service|deployment <имя>   # подробности + События внизу
kubectl logs <pod> [-f] [--previous]      # --previous — журнал предыдущего (упавшего) контейнера
kubectl exec -it <pod> -- sh              # команда внутри контейнера
kubectl scale deployment <имя> --replicas=3
kubectl rollout status|restart|undo|history deployment/<имя>
kubectl set image deployment/<имя> <контейнер>=<образ:тег>
kubectl port-forward service/<имя> 5050:5050
kubectl get events --sort-by=.metadata.creationTimestamp
```

`kubectl port-forward service/…` — отладочный туннель: kubectl выбирает **один** Pod за Service и пробрасывает трафик только к нему. Если этот Pod удалить, туннель обрывается (`error: lost connection to pod`), и команду нужно перезапустить.

Важно: `kubectl apply` файла с `replicas: 1` перезапишет число, выставленное `kubectl scale`.

## 6. Порядок диагностики

1. `kubectl config current-context`, `kubectl get nodes` — тот ли кластер, доступен ли.
2. `kubectl get deployments` — создан ли, `READY x/y`.
3. `kubectl get pods` — есть ли Pod, статус, `RESTARTS`.
4. `kubectl describe pod <имя>` → раздел **Events** — причина (`Failed to pull image`, `configmap not found`, `Readiness probe failed`, `OOMKilled`).
5. `kubectl logs <pod>` / `--previous` — ошибки самого приложения.
6. `kubectl describe service <имя>` — `Selector`, `TargetPort`, `Endpoints` (пусто ⇒ метки не совпали или Pod не готов).
7. `kubectl get pods --show-labels` — сравнить с селектором.
8. `kubectl exec <pod> -- printenv` — получил ли контейнер параметры.

| Симптом | Вероятная причина |
|---|---|
| `The connection to the server … was refused` | Кластер не запущен / неверный контекст |
| `ErrImagePull`, `ImagePullBackOff` | Неверное имя/тег, образ не загружен в кластер (`minikube image load`) |
| `CrashLoopBackOff` | Приложение завершается: смотреть `logs --previous` (в ЛР — недоступна БД) |
| `CreateContainerConfigError` | Нет ConfigMap/Secret или ключа |
| `Pending` | Не хватает ресурсов узла или не привязался PVC |
| `Running`, `0/1` | Не прошла readinessProbe |
| `Endpoints: <none>` | Селектор Service ≠ метки Pod |
| port-forward: `connection refused` | Неверный `targetPort` или приложение не слушает порт |
| Старое значение настройки | ConfigMap изменен, но Pod не перезапущен |

## 7. Установка локального кластера (macOS)

**Вариант A — Docker Desktop.** Settings → Kubernetes → *Enable Kubernetes* → Apply & Restart. Docker Desktop скачивает образы компонентов кластера (несколько сотен МБ); если загрузка зависла или упала — *Reset Kubernetes Cluster* на той же вкладке, проверить интернет/VPN/прокси. Контекст — `docker-desktop`. В режиме *kubeadm* кластер видит локальные образы Docker; в режиме *kind* их нужно загружать отдельно.

**Вариант B — Minikube** (использован в ЛР):
```bash
brew install minikube
minikube start --driver=docker     # скачивает образ kicbase (~470 МБ), контекст → minikube
minikube image load ticketsales-server:1.0
minikube stop / minikube delete    # остановить / удалить кластер
```
`kubectl` уже есть в составе Docker Desktop (`~/.docker/bin/kubectl`), отдельно ставить не нужно.

## 8. Docker Compose и Kubernetes

| | Docker Compose | Kubernetes |
|---|---|---|
| Масштаб | Одна машина | Кластер из многих узлов |
| Модель | «Запусти эти контейнеры» | «Поддерживай это состояние» |
| Отказ | `restart:` перезапускает упавший контейнер; удаленный не возвращается | Удаленный/упавший Pod заменяется, трафик идет только в готовые |
| Сеть | Сеть проекта, DNS по имени сервиса | Service с ClusterIP, балансировка, DNS |
| Масштабирование | `--scale`, мешают фиксированные порты/имена | `replicas`, `kubectl scale`, HPA (автомасштабирование) |
| Обновление | Пересоздание контейнера (простой) | RollingUpdate, откат `rollout undo` |
| Конфигурация | `.env`, `environment:` | ConfigMap, Secret |
| Назначение | Локальная разработка, тесты | Продакшен, отказоустойчивость |

## 9. Ответы на контрольные вопросы

1. **Kubernetes** — платформа оркестрации контейнеров: развертывание, поддержание числа экземпляров, сетевой доступ, конфигурация, обновления.
2. **Отличие от Docker:** Docker собирает образы и запускает отдельные контейнеры; Kubernetes управляет множеством контейнеров на множестве узлов по декларативному описанию.
3. **Cluster** — control plane (API server, etcd, scheduler, контроллеры) + рабочие узлы.
4. **Pod** — минимальная единица развертывания: один или несколько контейнеров с общим IP и томами.
5. **Pod vs контейнер:** Pod — обертка Kubernetes над контейнером(ами) с сетью, томами, метками, probes; Pod пересоздается целиком.
6. **Deployment** — объект с желаемым состоянием приложения (шаблон Pod, replicas, стратегия обновления), управляющий ReplicaSet.
7. **replicas** — желаемое число одинаковых Pod.
8. **Service** — стабильное имя/IP и балансировка трафика на группу Pod по селектору.
9. **Почему не обращаться к Pod напрямую:** IP Pod меняется при пересоздании, Pod-ов несколько, некоторые могут быть не готовы — Service это скрывает.
10. **Метки и селекторы** — связывают объекты: Deployment ↔ Pod, Service ↔ Pod, фильтрация в kubectl.
11. **YAML** — текстовый формат структур данных; в нем описываются ресурсы (`apiVersion`, `kind`, `metadata`, `spec`).
12. **Состояние Pod:** `kubectl get pods`.
13. **Подробности:** `kubectl describe pod <имя>`, `kubectl logs <имя>`.
14. **Удаление Pod из Deployment:** ReplicaSet замечает нехватку и создает новый Pod с новым именем и IP.
15. **Почему данные не восстанавливаются:** новый Pod создается из образа «с нуля»; данные сохраняются только во внешнем хранилище (БД, PVC).
16. **ConfigMap** — объект для неконфиденциальной конфигурации, передаваемой в Pod через env или файлы.
17. **Переменные окружения** — способ передать настройки процессу без пересборки образа (один образ для разных сред).
18. **port-forward** — временный туннель с локального порта на Pod/Service для отладки без публикации наружу.
19. **Несколько Pod на одном компьютере:** все они разделяют одно железо и ОС; отказ компьютера уносит их все.
20. **Compose vs Kubernetes:** Compose запускает набор контейнеров на одной машине; Kubernetes непрерывно поддерживает желаемое состояние в кластере (самовосстановление, балансировка, rolling update, масштабирование).
