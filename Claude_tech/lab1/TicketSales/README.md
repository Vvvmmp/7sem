# ЛР №10 — CI/CD для TicketSales (Jenkins + Kubernetes)

Вариант 6 «Продажа билетов». Pipeline в Jenkins: получить код → собрать → протестировать → собрать Docker-образ → обновить Deployment в Kubernetes (Docker Desktop).
Теория простым языком: [docs/cicd-theory.md](docs/cicd-theory.md).

Другие лабораторные — по тегам: `git checkout claude-tech-lab9` (Kubernetes), `claude-tech-lab8` (Docker) и т.д.

## Что изменено

| Что | Где | Как работает |
|---|---|---|
| Jenkinsfile (Declarative Pipeline) | [Jenkinsfile](Jenkinsfile) | Хранится в Git вместе с кодом. Этапы: `Checkout → Restore → Build → Test → Package → Deploy → Verify`. Любая ошибка этапа останавливает pipeline, следующие этапы не выполняются |
| Путь к проекту | `APP_DIR` в [Jenkinsfile](Jenkinsfile) | Репозиторий — монорепозиторий (`7sem`), проект лежит в `Claude_tech/lab1/TicketSales`; все этапы после `Checkout` выполняются через `dir(env.APP_DIR)` |
| Restore / Build | [Jenkinsfile](Jenkinsfile) | `dotnet restore TicketSales.sln`, затем `dotnet build … -c Release --no-restore` (восстановление и компиляция — отдельные этапы) |
| Test | [Jenkinsfile](Jenkinsfile) | Тестам нужен PostgreSQL (порты 5433/5434), поэтому перед ними `docker compose up -d --wait postgres-primary postgres-replica`; затем `dotnet test` с `--logger trx`. В `post { always }` — архив `.trx` и `docker compose down`, даже если тесты упали. Пароль `POSTGRES_PASSWORD=ticketsales` — тестовый, он же зашит в [ReplicatedTestServer.cs](tests/TicketSales.Tests/ReplicatedTestServer.cs) |
| Package | [Jenkinsfile](Jenkinsfile), [Dockerfile](src/TicketSales.Server/Dockerfile) (из ЛР №8, без изменений) | `docker build -t ticketsales-server:$BUILD_NUMBER` — тег = номер сборки Jenkins, затем `docker image inspect` |
| Deploy | [Jenkinsfile](Jenkinsfile), [k8s/deployment.yaml](k8s/deployment.yaml) (из ЛР №9) | Отдельная передача образа не нужна: Kubernetes Docker Desktop использует тот же Docker Engine, что и Jenkins (`imagePullPolicy: IfNotPresent`) → `kubectl set image deployment/ticketsales-server server=ticketsales-server:N` → `rollout status --timeout=180s` → `get deployments`, `get pods`. Контекст явный: `--context docker-desktop` |
| Verify | [Jenkinsfile](Jenkinsfile) | Сверяет, что образ в Deployment равен `ticketsales-server:$BUILD_NUMBER` (т.е. выкатилась новая версия, а не старая); `port-forward` на Service + реальный запрос `GetEvents` через `nc`, ожидает `"Success":true`; печатает последние строки журнала |
| Poll SCM | `triggers { pollSCM('H/2 * * * *') }` | Jenkins раз в ~2 минуты проверяет Git и сам запускает pipeline при новом коммите |
| `PATH` | `environment` в [Jenkinsfile](Jenkinsfile) | Добавлены `/opt/homebrew/bin`, `~/.docker/bin`: у процесса Jenkins другой `PATH`, чем у терминала |
| Удалено (ЛР №9) | — | `docs/kubernetes-theory.md` (теория ЛР №9 — в теге `claude-tech-lab9`). Код приложения не менялся |

## Настройка Jenkins (macOS)

```bash
brew install jenkins-lts && brew services start jenkins-lts   # http://localhost:8080
# Docker Desktop → Settings → Kubernetes → Enable; kubectl config use-context docker-desktop
kubectl create secret generic ticketsales-db --from-literal=POSTGRES_PASSWORD=<пароль>
kubectl apply -f k8s/                                         # Deployment/Service/БД из ЛР №9 (один раз)
```

В Jenkins: *New Item → Pipeline* `TicketSales-CICD` → *Pipeline script from SCM* → Git, URL `https://github.com/Vvvmmp/7sem.git`, ветка `*/master`, **Script Path** `Claude_tech/lab1/TicketSales/Jenkinsfile` → *Build Triggers* (Poll SCM берется из Jenkinsfile после первого запуска).

Требования к машине Jenkins: `dotnet`, `git`, `docker` (запущен Docker Desktop), `kubectl`; доступ Jenkins к `~/.kube/config` пользователя (Jenkins из brew работает от вашего пользователя, поэтому доступ есть).

## Проверка сценариев

| Сценарий | Как воспроизвести | Ожидаемо |
|---|---|---|
| Успех | Build Now | Все 7 этапов зеленые, Success |
| Ошибка теста | Изменить `Assert` в любом тесте, commit, push | `Test` Failed, `Package`/`Deploy`/`Verify` не выполняются (серые), образ не создан |
| Ошибка Deploy | В Jenkinsfile `CONTAINER = 'wrong'`, push | `Deploy` Failed: `unable to find container named "wrong"`; вернуть имя `server` |
| Новая версия | Изменить текст в `Program.cs` сервера, push | Новый тег образа = номер сборки, `kubectl describe deployment ticketsales-server` показывает его, в журнале Pod новый текст |

## Проверено локально (без Jenkins)

Шаги pipeline выполнены вручную теми же командами: `dotnet restore` → `dotnet build -c Release` (0 ошибок, 0 предупреждений) → `docker compose up -d --wait` + `dotnet test` → **93/93 пройдено** → `docker build -f src/TicketSales.Server/Dockerfile` (образ 333 МБ) → `inspect`.

**Не проверено:** сам запуск в Jenkins и этапы `Deploy`/`Verify` — Jenkins не установлен, Docker Desktop Kubernetes на момент работы не запускал. Таблицу результатов и скриншоты запусков заполните после первого прогона.

## Результаты (заполнить после прогона в Jenkins)

| Параметр | Результат |
|---|---|
| Репозиторий / ветка | `Vvvmmp/7sem` / `master` |
| Jenkins Job / Jenkinsfile | `TicketSales-CICD` / `Claude_tech/lab1/TicketSales/Jenkinsfile` |
| Версии dotnet / git / docker / kubectl | 10.0.402 / — / 29.9.2 / v1.37.2 |
| Номер сборки → тег образа | |
| Статус при успешных тестах / при упавшем тесте | |
| Образ в Deployment после Deploy | |
| Ошибки и решения | |
