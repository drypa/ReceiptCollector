# Декомпозиция: Подготовка Analytics-сервиса к продакшен-деплою через docker compose

> Источники: [задача](../tasks/analytics-production-deployment.md),
> [ADR 012](../adr/012-analytics-production-deployment.md), `AGENTS.md`.
> Объём: **инфраструктура Analytics + docker-compose.yml + nginx.prod.conf**. Бизнес-логика
> сервисов (Go backend, bot, .NET API) **не меняется**; CI **не меняется** (ADR п. 7);
> dev-контур (`docker-compose.develop.yml`, `nginx.dev.conf`, `up.dev.sh`) **не трогаем** (NFR-5).
> Единственные касания backend/bot в compose — перевод `depends_on` mongo на `service_healthy`.

## Сводка

| # | Подзадача | Приоритет | Оценка |
|---|-----------|-----------|--------|
| T1 | Health-эндпоинт `/health` в `Program.cs` | P0 | 0.25 дн |
| T2 | `Analytics/Dockerfile`: node-стадия + publish обоих приложений + curl + EXPOSE 5039 | P0 | 1 дн |
| T3 | `Analytics/docker-entrypoint.sh` (новый): синхронизация wwwroot + exec API | P0 | 0.25 дн |
| T4 | `Analytics/.dockerignore`: добавить `**/wwwroot/` | P0 | 0.1 дн |
| T5 | `docker-compose.yml`: формат 2.4, healthcheck'и, сервис миграций, env/volume/зависимости analytics | P0 | 1.5 дн |
| T6 | `nginx.prod.conf`: статика + прокси `/api`, убрать upstream 5173 | P0 | 0.5 дн |
| T7 | Опциональный cleanup: `AdminUsers` в appsettings API + строка-заглушка в appsettings миграций | P1 | 0.25 дн |
| T8 | Интеграционная верификация: config, build, тесты, ручная проверка | P0 | 0.5 дн |
| T9 | Операционный шаг: merge `feature/analytics` → `master` (публикация `latest`) | P0 | операционный |

**Итого (последовательно): ~4.9 дн; с учётом параллельности треков — ~3.5–4 дн.**

## Порядок выполнения и параллельность

```
Трек A (образ)   T3 → T4 → T2 ──────────────┐
Трек B (health)  T1 ───────────────────────┤
Трек C (nginx)   T6 ───────────────────────┼─→ T5 ─→ T8 (верификация)
Трек D (cleanup) T7 (в любой момент) ──────┤
Операция         T9 (merge → master → latest) — до серверного деплоя
```

- **T3, T4, T2 — одно связное изменение** (одна ветка/коммит): Dockerfile ссылается на
  `docker-entrypoint.sh` (`ENTRYPOINT ["/app/docker-entrypoint.sh"]`) — без T3 `docker compose build`
  упадёт на этапе runtime-стадии; `.dockerignore` (T4) обязателен, чтобы локальный (возможно
  устаревший) `wwwroot` не попадал в build-контекст.
- **T1 и T6 независимы** — можно выполнять параллельно с треком A.
- **T5 можно писать параллельно**, но его критерий готовности (сборка и запуск стека) проверяется
  только после T1–T4: healthcheck analytics требует эндпоинт `/health` (T1), `build: ./Analytics`
  требует рабочий Dockerfile (T2).
- **T7 — не блокер**, параллельно, приоритет P1.
- **T8 — финальная**, после T1–T6. Локальная часть T8 возможна без T9 (сборка из контекста),
  серверная часть (`docker compose pull`) требует T9.
- **T9 — операционный шаг** (merge в `master`), без него на сервере нет тега `latest` и
  `docker compose pull` падает (риск из ADR). Выполняется после код-ревью T1–T7.

---

## T1. Health-эндпоинт `/health` в `Program.cs`

**Файл:** `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs`

**Действия** (ADR, решение E1 / п. 6 «Детали решения»):

1. Добавить liveness-эндпоинт рядом с остальными `Map*`-вызовами (после `MapMerchantEndpoints()`,
   **до** `app.MapFallbackToFile("/index.html")`):
   ```csharp
   app.MapGet("/health", () => Results.Ok("healthy"));
   ```
2. Никаких других изменений: никаких новых пакетов (HealthChecks не добавляем — YAGNI),
   бизнес-логика не затрагивается. Готовность БД/миграций обеспечивается `depends_on` в compose (T5),
   а не этим эндпоинтом.

**Критерий готовности:**
- `cd Analytics && dotnet build` — без ошибок; `dotnet test` — без регрессий (T8).
- Локальная проверка: `dotnet run` в Api, `curl -fsS http://localhost:5039/health` → `healthy`.
  (В dev-контуре порт может отличаться — для проверки достаточно ответа 200 на `/health`.)

**Оценка:** 0.25 дня.

---

## T3. `Analytics/docker-entrypoint.sh` (новый файл)

**Файл:** `Analytics/docker-entrypoint.sh` (создать; выполнить раньше Dockerfile — см. порядок)

**Действия** (ADR, решение B1 / п. 3 «Детали решения»):

1. Создать файл с содержимым (idемпотентная синхронизация статики в общий том + запуск API):
   ```sh
   #!/bin/sh
   set -e

   # Синхронизация собранного фронтенда в общий том для nginx (read-only на стороне nginx)
   if [ -d /app/api/wwwroot ] && [ -d /srv/wwwroot ]; then
       rm -rf /srv/wwwroot/*
       cp -r /app/api/wwwroot/. /srv/wwwroot/
   fi

   exec dotnet /app/api/ReceiptCollector.Analytics.Api.dll
   ```
2. Убедиться, что файл не попадает под `.dockerignore` (в текущем `.dockerignore` исключений
   на него нет — проверять при T4).
3. Git-track файла: файл новый и не должен игнорироваться `.gitignore`.

**Критерий готовности:**
- Файл существует, валидный `sh`-скрипт (`sh -n Analytics/docker-entrypoint.sh` без ошибок),
  исполняемый бит выставлен в git (`git update-index --chmod=+x` либо `chmod +x` до коммита —
  в Dockerfile также будет `RUN chmod +x docker-entrypoint.sh`, это дублирование на случай
  отсутствия бита в git).
- Скрипт синхронизирует `/app/api/wwwroot` → `/srv/wwwroot` и завершается `exec dotnet ...`.

**Оценка:** 0.25 дня.

---

## T4. `Analytics/.dockerignore`: добавить `**/wwwroot/`

**Файл:** `Analytics/.dockerignore`

**Действия** (ADR, пояснения к Dockerfile):

1. Добавить в конец файла строку `**/wwwroot/` (с комментарием, например
   `# Собранный фронтенд (собирается в Dockerfile, локальные артефакты не нужны)`).
2. Это гарантирует, что:
   - локальные (возможно, устаревшие) `wwwroot/assets/*` не попадают в build-контекст;
   - `COPY . .` в .NET-стадии не перезаписывает свежие ассеты из node-стадии.

**Критерий готовности:**
- В build-контексте Dockerfile отсутствует локальный `wwwroot` (проверка: `tar -tf`
  контекста не требуется — достаточно собрать образ по T2 и убедиться, что ассеты в образе
  свежие, см. T8 п. 5).
- Существующие исключения `.dockerignore` не нарушены (`**/node_modules/` уже есть — проверить).

**Оценка:** 0.1 дня.

---

## T2. `Analytics/Dockerfile`: сборка фронтенда + публикация обоих приложений

**Файл:** `Analytics/Dockerfile` (полная замена)

**Действия** (ADR, решения B1 + C1 + D1 / п. 2 «Детали решения»):

1. **Стадия `frontend`** (`node:22-alpine`):
   - `WORKDIR /src/frontend`, `COPY frontend/package.json frontend/package-lock.json ./`, `RUN npm ci`;
   - `COPY frontend/ ./`, `RUN npm run build`
     (vite outDir из `vite.config.ts` → результат в `/src/src/ReceiptCollector.Analytics.Api/wwwroot`).
2. **Стадия `build`** (`mcr.microsoft.com/dotnet/sdk:10.0`):
   - скопировать `*.sln` и все 6 csproj (Api, Application, Domain, Infrastructure, Migrations,
     tests/Api.Tests), `RUN dotnet restore`;
   - `COPY . .`;
   - **обязательный порядок**: `COPY --from=frontend /src/src/ReceiptCollector.Analytics.Api/wwwroot
     ./src/ReceiptCollector.Analytics.Api/wwwroot` — **после** `COPY . .`, чтобы свежие ассеты
     не были перезаписаны локальным `wwwroot` (см. T4);
   - `WORKDIR src/ReceiptCollector.Analytics.Api/ && RUN dotnet publish -c Release -o /app/publish/api --no-restore`;
   - `WORKDIR src/ReceiptCollector.Analytics.Migrations/ && RUN dotnet publish -c Release -o /app/publish/migrations --no-restore`
     (в выход миграций попадают `Scripts/` и `appsettings.json` — задано в csproj
     `CopyToOutputDirectory=PreserveNewest`);
   - убрать отладочный `RUN ls -alqh`.
3. **Стадия `runtime`** (`mcr.microsoft.com/dotnet/aspnet:10.0`):
   - `RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*`
     (curl нужен для healthcheck контейнера — в aspnet-образе его нет);
   - `WORKDIR /app`, `COPY --from=build /app/publish .` → `/app/api` и `/app/migrations`;
   - `COPY docker-entrypoint.sh .`, `RUN chmod +x docker-entrypoint.sh`;
   - `EXPOSE 5039` (заменить текущий `EXPOSE 8080`);
   - `ENTRYPOINT ["/app/docker-entrypoint.sh"]` (заменить текущий прямой `dotnet ...`).

**Критерий готовности:**
- `docker compose build analytics` (или `docker build -f Analytics/Dockerfile Analytics`) — без ошибок.
- В образе есть оба приложения: `/app/api/ReceiptCollector.Analytics.Api.dll`,
  `/app/migrations/ReceiptCollector.Analytics.Migrations.dll` и `/app/migrations/Scripts/` (5 скриптов).
- В образе есть собранный фронтенд: `/app/api/wwwroot/index.html` и `/app/api/wwwroot/assets/*`
  (критерий приёмки задачи п. 6).
- `EXPOSE 5039` совпадает с `ASPNETCORE_URLS` из T5 и upstream nginx из T6 (FR-6).

**Оценка:** 1 день.

---

## T6. `nginx.prod.conf`: статика + прокси `/api`

**Файл:** `nginx.prod.conf`

**Действия** (ADR, решение B1 / п. 5 «Детали решения»; FR-3):

1. **Удалить** upstream `analytics_frontend` (блок `server analytics:5173`) — FR-3.3;
   upstream `analytics_api` (`server analytics:5039`) **оставить без изменений**.
2. В HTTPS-сервере задать раздачу статики (вместо `proxy_pass http://analytics_frontend/`):
   - на уровне `server`: `root /usr/share/nginx/html;` и `index index.html;`;
   - `location / { try_files $uri $uri/ /index.html; }` — SPA-fallback;
   - `location /assets/ { expires 1y; add_header Cache-Control "public, immutable"; }`
     (кэш-правило переносится из вложенного `location ~* \.(js|css|...)$`);
   - `location /api { proxy_pass http://analytics_api; ... }` — без изменений (прокси на `analytics:5039`);
   - `location /health { ... }` — без изменений (отвечает nginx, `access_log off`, 200).
3. Секьюрити-заголовки (X-Frame-Options, HSTS и т.д.) и редирект 80→443 — не трогать.

**Критерий готовности:**
- В файле **нет** упоминаний `5173` и `analytics_frontend` (критерий приёмки задачи п. 5).
- `docker run --rm -v $(pwd)/nginx.prod.conf:/etc/nginx/nginx.conf:ro nginx:alpine nginx -t` —
  конфигурация валидна.
- `location /` раздаёт файлы из `/usr/share/nginx/html` (том из T5), а не проксирует.

**Оценка:** 0.5 дня.

---

## T5. `docker-compose.yml`: формат 2.4, healthcheck'и, сервис миграций, конфигурация Analytics

**Файл:** `docker-compose.yml`

**Действия** (ADR, решения A1 + C1 + D1 + E1 / п. 1 «Детали решения»; FR-1, FR-2, FR-4, FR-5):

1. **Формат:** `version: '2'` → `version: '2.4'` (требуется для `condition: service_healthy`
   и `condition: service_completed_successfully`). Требование к окружению: Docker Compose v2
   (plugin) либо docker-compose ≥ 1.23.
2. **mongo:** добавить `healthcheck`:
   `test: ["CMD", "mongo", "--quiet", "--eval", "db.adminCommand('ping').ok"]`,
   `interval: 10s, timeout: 5s, retries: 10, start_period: 10s`.
3. **postgres:** добавить `healthcheck`:
   `test: ["CMD-SHELL", "pg_isready -U ${PG_LOGIN} -d postgres"]`, те же параметры.
4. **collector / bot:** `depends_on` → `mongo: { condition: service_healthy }` (FR-5.1).
5. **Новый сервис `analytics-migrations`** (run-to-completion; FR-4.1, FR-4.3):
   - `image: drypa/receipt-collector-analytics:latest`, `container_name: receipt-analytics-migrations`,
     `restart: "no"`;
   - `depends_on: postgres: { condition: service_healthy }`;
   - `environment:`:
     - `RECEIPTCOLLECTOR_Infrastructure__Postgres__ConnectionString=Host=postgres;Port=5432;Database=receipts;Username=${PG_LOGIN};Password=${PG_SECRET}`;
     - `RECEIPTCOLLECTOR_MigrationScripts__DirectoryPath=/app/migrations/Scripts`;
     - `RECEIPTCOLLECTOR_MigrationScripts__CommandTimeoutSeconds=60`;
   - `working_dir: /app/migrations`, `entrypoint: ["dotnet", "ReceiptCollector.Analytics.Migrations.dll"]`.
6. **analytics** (FR-1.2, FR-2.2, FR-6.1, FR-5.2/5.3):
   - `build: ./Analytics` + `image: drypa/receipt-collector-analytics:latest`
     (**имя из CI**, единый источник — ADR A1; исправить текущее `drypa/receipt-analytics`);
   - `depends_on:`
     - `postgres: { condition: service_healthy }`,
     - `mongo: { condition: service_healthy }`,
     - `analytics-migrations: { condition: service_completed_successfully }`;
   - `environment:` — все продакшен-настройки через env (FR-2):
     - `ASPNETCORE_URLS=http://+:5039`,
     - `ASPNETCORE_ENVIRONMENT=Production`,
     - `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`,
     - `Infrastructure__Postgres__ConnectionString=Host=postgres;Port=5432;Database=receipts;Username=${PG_LOGIN};Password=${PG_SECRET}`,
     - `Infrastructure__Receipts__Mongo__ConnectionString=mongodb://${MONGO_LOGIN}:${MONGO_SECRET}@mongo:27017/receipt_collection?authSource=admin`,
     - `Infrastructure__Receipts__Mongo__Database=receipt_collection`,
     - `Infrastructure__Receipts__Mongo__Collection=receipt_requests`,
     - `Infrastructure__Receipts__Mongo__UsersCollection=system_users`,
     - `Infrastructure__Receipts__Synchronization__Skip=${ANALYTICS_SYNC_SKIP:-true}`,
     - `Infrastructure__AuthLinks__BaseUrl=${ANALYTICS_AUTHLINK_BASE_URL}`,
     - `Infrastructure__AuthLinks__LifetimeMinutes=15`,
     - `Infrastructure__AdminUsers__TelegramIds__0=${ANALYTICS_ADMIN_TELEGRAM_ID_0}`,
     - `Infrastructure__AdminUsers__TelegramIds__1=${ANALYTICS_ADMIN_TELEGRAM_ID_1}`
       (для N админов — строки `__0..__N` по числу админов);
   - `expose: ["5039"]` (наружу не публикуем — смотрит только nginx);
   - `volumes: - analytics-wwwroot:/srv/wwwroot`;
   - `healthcheck:` `test: ["CMD", "curl", "-fsS", "http://localhost:5039/health"]`,
     `interval: 10s, timeout: 5s, retries: 5, start_period: 30s`.
7. **nginx:**
   - `depends_on: analytics: { condition: service_healthy }`;
   - добавить в volumes `- analytics-wwwroot:/usr/share/nginx/html:ro`;
   - монтирование `./nginx.prod.conf` сделать `:ro` (добавить суффикс).
8. **Объявить** в конце файла:
   ```yaml
   volumes:
     analytics-wwwroot:
   ```

**Критерий готовности:**
- `docker compose config` — валиден, интерполяция `.env` без ошибок (T8).
- `docker compose build` собирает analytics из контекста `./Analytics` (FR-1.2).
- `docker compose up -d` на dev-машине с временным `.env` поднимает стек в порядке:
  mongo/postgres (healthy) → migrations (completed) → analytics (healthy) → nginx.
- `docker ps` показывает `healthy` для mongo/postgres/analytics.
- Имя образа analytics в compose == имени в CI (`drypa/receipt-collector-analytics`), критерий
  приёмки задачи п. 9 (локальная часть).

**Оценка:** 1.5 дня.

---

## T7. Опциональный cleanup `appsettings.json` (не блокер)

**Файлы:**
- `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`
- `Analytics/src/ReceiptCollector.Analytics.Migrations/appsettings.json`

**Действия** (ADR, п. 4 «Замечания»):

1. В `appsettings.json` API секция `Infrastructure:AdminUsers` сейчас в форме **массива**
   `[123, 987]`, а опции `AdminUserOptions` ожидают `{ "TelegramIds": [...] }` → форма не биндится.
   Привести к совместимому виду:
   ```json
   "AdminUsers": {
     "TelegramIds": [123456789, 987654321]
   }
   ```
   либо **удалить секцию** (в проде список админов всё равно задаётся env
   `Infrastructure__AdminUsers__TelegramIds__0..N`).
2. В `appsettings.json` проекта миграций очистить строку-заглушку
   `Host=localhost;Port=5432;...Password=change_me` (env `RECEIPTCOLLECTOR_*` всё равно
   переопределит; placeholder не должен попадать в продакшен-образ).
3. НЕ трогать остальное (`Logging`, `AllowedHosts`, `AuthLinks.BaseUrl`-заглушку — в проде
   переопределяется env).

**Критерий готовности:**
- `cd Analytics && dotnet build` и `dotnet test` — без ошибок (T8).
- `AdminUsers` в appsettings API не имеет несовместимой массивой формы.
- В appsettings миграций нет placeholder-пароля.

**Оценка:** 0.25 дня. **Приоритет P1** (при нехватке времени может быть отложен — на деплой
не влияет, т.к. в проде конфиг только через env).

---

## T8. Интеграционная верификация

**Файлы:** изменений нет — только проверка.

**Действия:**

1. `docker compose config` — файл валиден, формат 2.4, подстановка `.env` корректна.
2. `docker compose build` (или `./build.sh`) — собираются все образы, включая analytics из `./Analytics`.
3. `cd Analytics && dotnet test` — все тесты зелёные (включая существующие — без регрессий).
4. `cd backend && go test ./...` и `cd bot && go test ./...` — регрессионный смоук
   (файлы backend/bot не менялись, проверка на всякий случай).
5. Проверка содержимого образа:
   `docker run --rm drypa/receipt-collector-analytics:latest ls -la /app/api/wwwroot/assets/`
   — ассеты на месте; `ls /app/migrations/Scripts/` — 5 скриптов.
6. Локальный прогон стека с тестовым `.env`: `./up.sh`, затем
   - `docker ps` — все сервисы, mongo/postgres/analytics = `healthy`;
   - `docker logs receipt-analytics-migrations` — миграции применены, exit 0;
   - `curl -fsS https://<host>/health` — `healthy` (nginx);
   - `curl -fsS https://<host>/api/...` — отвечает API (прокси работает);
   - открыть webUI — страница грузится, JS/CSS из `/assets/` отдаются с `Cache-Control: public, immutable`.
7. `docker compose pull` на сервере — образ `drypa/receipt-collector-analytics:latest` находится
   (требует T9 — merge в master).

**Критерий готовности:** все пункты выше пройдены; критерии приёмки задачи п. 1–9 закрыты.

**Оценка:** 0.5 дня.

---

## T9. Операционный шаг: merge `feature/analytics` → `master`

**Действия** (ADR, п. 7 «CI и ветки»; FR-1.3):

1. После код-ревью и зелёного CI на ветке `feature/analytics` выполнить merge в `master`
   (и пушить `master`). Текущая ветка репозитория — `feature/analytics` (проверено).
2. CI `.github/workflows/build-analytics-image.yml` **не меняется**; тег `latest` публикуется
   автоматически для дефолтной ветки `master` (условие `is_default_branch` уже в workflow).
3. Дождаться завершения CI-джоба и появления тега `drypa/receipt-collector-analytics:latest`
   в Docker Hub (проверка: `docker manifest inspect drypa/receipt-collector-analytics:latest`).

**Критерий готовности:**
- Тег `latest` доступен для `docker compose pull` на сервере (критерий приёмки задачи п. 9).

**Оценка:** операционный (~0.5 дня с учётом ожидания CI). Выполняет техлид/владелец ветки.

---

## Чек-лист `.env` для продакшен-сервера

Файл формируется вручную на сервере (решение заказчика п. 4; `.env` в репозиторий не
коммитится, `gitignore` уже содержит `.env`). Новые ключи для Analytics: `ANALYTICS_AUTHLINK_BASE_URL`,
`ANALYTICS_ADMIN_TELEGRAM_ID_0..N`, `ANALYTICS_SYNC_SKIP` (см. ADR, п. 4).

| Переменная | Обязательность | Назначение |
|---|---|---|
| `PG_LOGIN` | ✔ | Пользователь PostgreSQL (init postgres, connection strings, healthcheck) |
| `PG_SECRET` | ✔ | Пароль PostgreSQL |
| `MONGO_LOGIN` | ✔ | Root-пользователь MongoDB (init mongo, analytics connection string) |
| `MONGO_SECRET` | ✔ | Пароль MongoDB |
| `MONGO_DATA` | ✔ | Путь на хосте для тома `/data/db` (mongo) |
| `MONGO_BACKUP` | ✔ | Путь на хосте для тома `/backup` (mongo) |
| `PG_DATA` | ✔ | Путь на хосте для тома `/var/lib/postgresql/data` (postgres) |
| `SSL_CERTS_PATH` | ✔ | Каталог с `fullchain.pem`/`private.key` (nginx, collector, bot); уже развёрнут |
| `RAW_TICKET_DUMP_PATH` | ✔ | Том raw-чеков (collector) |
| `GET_TICKET_ERROR_PATH` | ✔ | Том ошибок чека (collector) |
| `TEMPLATES_PATH` | ✔ | Том шаблонов чеков (collector) |
| `CLIENT_SECRET` | ✔ | Секрет backend (collector) |
| `BOT_TOKEN` | ✔ | Токен Telegram-бота |
| `HTTP_PROXY` | ✔* | Прокси для бота (`*` — если не нужен, передать пустое/без прокси) |
| `ANALYTICS_AUTHLINK_BASE_URL` | ✔ | Публичный HTTPS URL ссылок авторизации, напр. `https://<домен>/login`; **при отсутствии — провал валидации `[Url]` на старте** (ADR, риски) |
| `ANALYTICS_ADMIN_TELEGRAM_ID_0..N` | ✔ | Telegram ID администраторов (по строке на админа); при незаполнении в webUI нет админов |
| `ANALYTICS_SYNC_SKIP` | опц. | Пропуск синхронизации чеков при старте; `true` по умолчанию (`${ANALYTICS_SYNC_SKIP:-true}`) |

---

## Риски и зависимости

| Риск | Митигация |
|------|-----------|
| **T3/T4/T2 связка:** Dockerfile без `docker-entrypoint.sh` не соберётся; локальный `wwwroot` без `.dockerignore` может перезаписать свежие ассеты | Выполнять T3→T4→T2 одним изменением (одна ветка/коммит); порядок `COPY . .` → `COPY --from=frontend` строго соблюдён |
| **Тег `latest` не опубликован** (ветка не смёржена) → `docker compose pull` падает на сервере | T9 (merge в `master`) — обязателен до серверного деплоя; проверить `docker manifest inspect` |
| **Пустой/неполный `.env`** на сервере: нет `ANALYTICS_AUTHLINK_BASE_URL` → падение при старте; нет admin-id → нет админов | Чек-лист `.env` выше; серверный `.env` заполняется вручную по чек-листу |
| **Устаревшая статика в томе `analytics-wwwroot`** (Analytics не стартует / nginx держит старый том) | Синхронизация тома на каждом старте Analytics (T3) + `depends_on` nginx → analytics healthy |
| **Сборка требует доступа к npm-реестру** (node-стадия) | `npm ci` по `package-lock.json` (воспроизводимо); кэш CI `cache-from: type=gha` уже настроен |
| **Формат compose 2.4 на устаревшем docker-compose v1** | Требование: Docker Compose v2 (plugin) / ≥ 1.23; зафиксировать в чек-листе деплоя |
| **`AdminUsers`-массив не биндится в опции** (T7 не выполнен) | В проде список админов задаётся только env `ANALYTICS_ADMIN_TELEGRAM_ID_*`; T7 — косметический cleanup |
| **Регрессия dev-контура (NFR-5)** | `docker-compose.develop.yml`, `nginx.dev.conf`, `up.dev.sh` не изменяются; проверка в T8 |
| **Имена коллекций MongoDB** разойдутся | Уже согласованы: `receipt_collection` / `receipt_requests` / `system_users` (см. задачу п. 2.1); в T5 используются именно они |
