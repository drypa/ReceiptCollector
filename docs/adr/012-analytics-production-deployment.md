# 12. Подготовка Analytics-сервиса к продакшен-деплою через docker compose

## Статус

Принято (проектирование; реализация — по плану `docs/plans/analytics-production-deployment.md`)

## Контекст

По [задаче analytics-production-deployment](../tasks/analytics-production-deployment.md) сервис
Analytics (миграция данных MongoDB → PostgreSQL + аналитический webUI) не может быть развёрнут
в продакшен одной командой `./up.sh`: `docker compose` не поднимет его из-за несоответствия имён
образов, отсутствия продакшен-конфигурации, незапускаемых миграций и неполадающего в образ
фронтенда. Требуется устранить блокеры раздела 2.2 задачи с соблюдением решений заказчика
(раздел 6): конфигурация через переменные окружения; nginx раздаёт фронтенд как статику;
миграции — отдельный сервис в том же `docker-compose.yml`; `.env` не правится (формируется на
сервере вручную); SSL уже развёрнут; healthcheck'и для БД и сервисов.

Фактическое состояние кодовой базы на момент проектирования:

1. **Имя образа.** CI (`.github/workflows/build-analytics-image.yml`) публикует
   `drypa/receipt-collector-analytics` (тег `latest` — только для дефолтной ветки `master`).
   `docker-compose.yml` ссылается на `drypa/receipt-analytics:latest` → `docker compose pull`
   не найдёт образ. У сервиса `analytics` нет `build:` секции → `./build.sh` его не соберёт.
   Ветка `feature/analytics` не смёржена в `master` — тег `latest` для Analytics не публикуется.

2. **Порты.** `nginx.prod.conf` проксирует `/api` на `analytics:5039`, а `/` — на
   `analytics:5173` (dev-порт Vite). В `Analytics/Dockerfile` — `EXPOSE 8080`; без
   `ASPNETCORE_URLS` .NET слушает порт по умолчанию → nginx не достучится до API. Порт `5173`
   отсутствует в проде. При этом `vite.config.ts` уже использует `5039` для dev-прокси — порт
   `5039` является фактическим стандартом проекта.

3. **Конфигурация.** `appsettings.json` (Production) не содержит ConnectionStrings для
   Postgres и MongoDB; значения есть только в `appsettings.Development.json` (localhost).
   В проде API падает при старте: `Postgres connection string is not configured.`
   Опции биндятся из секций: `Infrastructure:Postgres`, `Infrastructure:Receipts:Mongo`,
   `Infrastructure:Receipts:Synchronization`, `Infrastructure:AuthLinks`, `Infrastructure:AdminUsers`.
   Проект миграций читает конфиг через env с префиксом `RECEIPTCOLLECTOR_` (`Program.cs`
   миграций: `.AddEnvironmentVariables(prefix: "RECEIPTCOLLECTOR_")`).
   Несовместимость формы `AdminUsers`: в `appsettings.json` это массив `[123, 987]`, а опции
   ожидают `{ "TelegramIds": [...] }` → в проде список админов из файла не биндится.

4. **Миграции.** Проект `ReceiptCollector.Analytics.Migrations` (net10.0, консольное
   приложение) применяет SQL-скрипты из `Scripts/` (5 скриптов), идемпотентен
   (таблица `migration_scripts_history`, транзакции, сам создаёт БД). Скрипты и `appsettings.json`
   копируются в выходную папку (`CopyToOutputDirectory`). В `docker-compose.yml` сервиса
   миграций нет.

5. **Фронтенд.** Сборка `npm run build` (Vite 7) настроена на outDir
   `../src/ReceiptCollector.Analytics.Api/wwwroot` (относительно `Analytics/frontend`).
   Ассеты `wwwroot/assets/*` не закоммичены (игнорируются `.gitignore`), Dockerfile не собирает
   фронтенд → в образе будет `index.html` без JS/CSS.

6. **Healthcheck'и.** У `mongo`, `postgres`, `analytics` нет healthcheck; `analytics` не зависит
   от `mongo` в `depends_on`. В API нет эндпоинта здоровья. Runtime-образ `aspnet:10.0`
   не содержит `curl`/`wget` (healthcheck извне нечем выполнить).

7. **Compose-формат.** `docker-compose.yml` объявлен как `version: '2'` — формат 2.0 не
   поддерживает `condition: service_healthy` / `condition: service_completed_successfully`
   (нужен формат ≥ 2.1 / ≥ 2.4 для `service_completed_successfully`).

### Бизнес-требования (из задачи)

1. FR-1: имя образа в compose и CI совпадает; у `analytics` есть `build:`; тег `latest`
   публикуется и доступен для `pull`.
2. FR-2: все продакшен-настройки Analytics задаются через переменные окружения
   (`Infrastructure__*`), значения — через `environment:` compose с подстановкой из `.env`.
3. FR-3: собранный фронтенд попадает в Docker-образ; nginx раздаёт его как статику; без
   проксирования на dev-порт.
4. FR-4: отдельный сервис миграций в `docker-compose.yml`; API стартует после успешного
   завершения миграций.
5. FR-5: healthcheck'и для mongo/postgres/analytics; `depends_on` с условиями готовности;
   Analytics стартует при доступных PostgreSQL и MongoDB.
6. FR-6: единый порт API `5039` (совпадает с upstream nginx); `EXPOSE` не конфликтует.

## Рассмотренные варианты

### Ключевое решение A: Имя Docker-образа Analytics

#### Вариант A1: Унифицировать на `drypa/receipt-collector-analytics` (имя из CI) — **выбран**

`docker-compose.yml` переходит на `image: drypa/receipt-collector-analytics:latest` и получает
`build: ./Analytics` (сборка помечает образ тем же именем). CI не меняется.

**Плюсы:** CI уже работает и публикует это имя; нулевой риск рассинхронизации «CI→compose»;
`build.sh` (docker compose build) и `up.sh` (pull) используют один источник имени.

**Минусы:** нет (требуется лишь смёржить `feature/analytics` в `master` для публикации `latest` —
операционный шаг, см. риски).

#### Вариант A2: Переименовать в CI на `drypa/receipt-analytics`

**Описание:** обновить `images:` в `build-analytics-image.yml` на имя из compose.

**Минусы:** меняется рабочий CI; ломается кэш предыдущих сборок; суть та же, но источник
изменений — CI, а не композ-файл; имя `receipt-analytics` конфликтует с контейнерным именем
`receipt-analytics`.

#### Вариант A3: Оставить рассогласование

**Минусы:** это и есть блокер 2.2.1 — `docker compose pull` не найдёт образ.

**Вывод:** выбран **A1**.

### Ключевое решение B: Доставка фронтенда в nginx (раздача статики)

#### Вариант B1: Один образ Analytics (фронтенд собран в нём) + общий том, наполняемый entrypoint'ом — **выбран**

**Описание:** В Dockerfile Analytics добавляется node-стадия сборки фронтенда
(`npm ci && npm run build` → `wwwroot`), publish включает ассеты. В compose объявляется named
volume `analytics-wwwroot`: API монтирует его в `/srv/wwwroot` и при старте через
`docker-entrypoint.sh` синхронизирует `/app/api/wwwroot` → `/srv/wwwroot`; nginx монтирует тот же
том read-only в `/usr/share/nginx/html` и раздаёт статику через `root`/`try_files`.

**Плюсы:**
- Один артефакт сборки фронтенда; образ Analytics самодостаточен (FR-3.1 и критерий приёмки 6
  «в образе есть `wwwroot/assets/*`» выполнены напрямую); SPA-fallback работает даже при прямом
  обращении к API-контейнеру.
- Том пересинхронизируется при каждом старте Analytics → ассеты не устаревают при обновлении
  образа.
- nginx остаётся `nginx:alpine`, без привязки к артефактам образа Analytics на этапе сборки.

**Минусы:** маленький entrypoint-скрипт в образе; nginx должен стартовать после наполнения тома
(решается `depends_on` на healthcheck Analytics).

#### Вариант B2: nginx-образ собирает фронтенд самостоятельно (собственный Dockerfile)

**Описание:** `docker/nginx/Dockerfile`: node-стадия выполняет `npm run build` с переопределением
outDir (`--outDir /out`), nginx-стадия копирует результат в `/usr/share/nginx/html`.

**Плюсы:** nginx полностью автономен, без томов и entrypoint'ов.

**Минусы:** дублирование сборки фронтенда (два артефакта, два источника истины, двойное время в
CI); усложнение конфигурации vite через CLI; противоречит духу «фронтенд в образе Analytics».

#### Вариант B3: `volumes_from` + `VOLUME /app/wwwroot` в Dockerfile

**Описание:** Dockerfile объявляет `VOLUME /app/wwwroot`; nginx использует `volumes_from: analytics:ro`.

**Минусы:** при пересоздании только Analytics новый анонимный том получает свежий контент, но
nginx (не пересозданный) продолжает держать старый том → устаревшие ассеты; `volumes_from` —
устаревшая/неоднозначная механика. Отклонено из-за проблемы свежести статики.

#### Вариант B4: nginx проксирует `/` на статику из API-контейнера

**Минусы:** прямое нарушение решения заказчика (п. 2 раздела 6).

**Вывод:** выбран **B1**.

### Ключевое решение C: Сервис миграций

#### Вариант C1: Отдельный compose-сервис на том же образе Analytics (entrypoint-override) — **выбран**

**Описание:** Dockerfile публикует в образ оба приложения: API → `/app/api`,
Migrations → `/app/migrations` (вместе со `Scripts/` и `appsettings.json`). В compose сервис
`analytics-migrations` использует тот же образ, но с
`entrypoint: ["dotnet", "ReceiptCollector.Analytics.Migrations.dll"]` и `working_dir: /app/migrations`.
`restart: "no"` (run-to-completion), `depends_on` API →
`analytics-migrations: condition: service_completed_successfully`.

**Плюсы:** один образ/один тег/один CI-джоб; миграции применяются детерминированно до старта API;
конфигурация миграций — те же переменные окружения (FR-4.3); идемпотентность уже обеспечена
раннером миграций (NFR-2).

**Минусы:** в runtime-образ попадает и миграционный бинарник (небольшой объём); образ
«заточен» под два приложения (незначительная потеря изоляции).

#### Вариант C2: Отдельный образ миграций `receipt-collector-analytics-migrations`

**Плюсы:** чистая изоляция; API-образ меньше.

**Минусы:** новый образ → новые теги и CI-джоб; два артефакта вместо одного; для масштаба
проекта избыточно (YAGNI).

#### Вариант C3: Миграции на старте API

**Минусы:** противоречит решению заказчика (п. 3 раздела 6); гонка с горизонтальными
перезапусками; API-процесс получает право на DDL. Отклонено.

**Вывод:** выбран **C1**.

### Ключевое решение D: Порт API

#### Вариант D1: Единый порт `5039` через `ASPNETCORE_URLS=http://+:5039` — **выбран**

**Описание:** `environment` сервиса analytics задаёт `ASPNETCORE_URLS`; Dockerfile меняет
`EXPOSE 8080` → `EXPOSE 5039`; `expose: ["5039"]` в compose; upstream nginx остаётся
`analytics:5039`; `vite.config.ts` не меняется (уже проксирует на `5039`).

**Плюсы:** `5039` — фактический стандарт проекта (nginx.prod.conf и vite.config.ts); FR-6.1/6.2
выполнены.

**Минусы:** нет.

#### Вариант D2: Порт `8080` (как в текущем Dockerfile)

**Минусы:** потребует правки `nginx.prod.conf` и `vite.config.ts`; dev- и prod-конфигурации
разъезжаются.

**Вывод:** выбран **D1**.

### Ключевое решение E: Health-эндпоинт Analytics

#### Вариант E1: Минимальный эндпоинт `/health` (liveness) — **выбран**

**Описание:** в `Program.cs` добавляется `app.MapGet("/health", () => Results.Ok("healthy"))`.
Готовность БД обеспечивается не эндпоинтом, а `depends_on` на healthcheck'и
`postgres`/`mongo` и `service_completed_successfully` миграций. Healthcheck контейнера —
`curl -fsS http://localhost:5039/health` (curl добавляется в runtime-стадию Dockerfile).

**Плюсы:** минимальное изменение (не бизнес-логика); соответствует FR-5.2 и NFR-3;
не вводит новых зависимостей (без пакетов HealthChecks).

**Минусы:** liveness не проверяет Postgres/Mongo напрямую (компенсировано `depends_on`).

#### Вариант E2: ASP.NET Health Checks (`AddHealthChecks` + проверка БД)

**Плюсы:** полноценная проверка БД.

**Минусы:** новые пакеты (AspNetCore.HealthChecks.*), больше кода; для оркестрации через
compose с `depends_on` избыточно (YAGNI).

**Вывод:** выбран **E1**.

## Решение

Выбраны: **A1** (имя образа `drypa/receipt-collector-analytics` + `build:`), **B1** (фронтенд
в образе Analytics, статика через общий том с синхронизацией entrypoint'ом), **C1** (сервис
миграций на том же образе, entrypoint-override), **D1** (порт `5039` через `ASPNETCORE_URLS`),
**E1** (эндпоинт `/health`). Плюс: конфигурация только через переменные окружения (решение
заказчика), healthcheck'и для БД/сервисов, подъём формата compose до `2.4`.

### Обоснование

1. **Единый источник имени образа** (A1): имя из CI, как уже работающего механизма; compose
   лишь приводит `image` в соответствие и добавляет `build:` для локальной сборки.
2. **Один артефакт фронтенда** (B1): сборка выполняется ровно один раз в образе Analytics,
   что одновременно удовлетворяет FR-3.1, критерию приёмки 6 и решению заказчика о раздаче
   статики nginx'ом без проксирования.
3. **Детерминированность деплоя** (C1, D1, E1): миграции выполняются до старта API и
   идемпотентны; порт зафиксирован единым значением; health-эндпоинт даёт compose условия
   готовности. Всё вместе закрывает NFR-1/NFR-2/NFR-3.
4. **Минимум изменений кода** (KISS/YAGNI): не меняется бизнес-логика сервисов; изменения
   ограничены инфраструктурой (Dockerfile, compose, nginx, entrypoint, один маршрут `/health`).

### Детали решения

#### 1. `docker-compose.yml` (итоговая схема)

Формат файла: `version: '2'` → **`version: '2.4'`** (требуется для
`condition: service_healthy` и `condition: service_completed_successfully`).

```yaml
version: '2.4'

services:
  mongo:
    container_name: receipt-mongo
    image: mongo:4.1
    restart: unless-stopped
    environment:
      - MONGO_INITDB_ROOT_USERNAME=${MONGO_LOGIN}
      - MONGO_INITDB_ROOT_PASSWORD=${MONGO_SECRET}
    volumes:
      - ${MONGO_DATA}:/data/db
      - ${MONGO_BACKUP}:/backup
    networks: [collector-net]
    healthcheck:
      test: ["CMD", "mongo", "--quiet", "--eval", "db.adminCommand('ping').ok"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 10s

  postgres:
    container_name: receipt-postgres
    image: postgres:18.0
    restart: unless-stopped
    environment:
      POSTGRES_DB: postgres
      POSTGRES_USER: ${PG_LOGIN}
      POSTGRES_PASSWORD: ${PG_SECRET}
    volumes:
      - ${PG_DATA}:/var/lib/postgresql/data
    networks: [collector-net]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${PG_LOGIN} -d postgres"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 10s

  collector:
    container_name: receipt-collector
    build: backend
    image: drypa/receipt-collector:latest
    restart: always
    depends_on:
      mongo:
        condition: service_healthy
    environment:
      - MONGO_URL=mongodb://mongo:27017
      - MONGO_LOGIN=${MONGO_LOGIN}
      - MONGO_SECRET=${MONGO_SECRET}
      - CLIENT_SECRET=${CLIENT_SECRET}
      - TEMPLATES_PATH=/usr/share/receipts/templates
      - GET_RECEIPT_WORKER_INTERVAL=15m
      - NALOGRU_BASE_ADDR=https://irkkt-mobile.nalog.ru:8888
    networks: [collector-net]
    volumes:
      - "${SSL_CERTS_PATH}:/usr/share/receipts/ssl/certs/"
      - "${RAW_TICKET_DUMP_PATH}:/var/lib/receipts/raw/"
      - "${GET_TICKET_ERROR_PATH}:/var/lib/receipts/error/"
      - "${TEMPLATES_PATH}:/usr/share/receipts/templates"

  bot:
    container_name: receipt-bot
    build: bot
    image: drypa/receipt-telegram-bot:latest
    restart: unless-stopped
    depends_on:
      mongo:
        condition: service_healthy
    environment:
      - BOT_TOKEN=${BOT_TOKEN}
      - BACKEND_GRPC_ADDR=collector:15000
      - REPORTS_GRPC_ADDR=collector:15001
      - HTTP_PROXY=${HTTP_PROXY}
    networks: [collector-net]
    volumes:
      - "${SSL_CERTS_PATH}:/usr/share/receipts/ssl/certs/"

  # ---- Сервис миграций: run-to-completion, применяет SQL-скрипты к PostgreSQL ----
  analytics-migrations:
    container_name: receipt-analytics-migrations
    image: drypa/receipt-collector-analytics:latest
    restart: "no"
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      - RECEIPTCOLLECTOR_Infrastructure__Postgres__ConnectionString=Host=postgres;Port=5432;Database=receipts;Username=${PG_LOGIN};Password=${PG_SECRET}
      - RECEIPTCOLLECTOR_MigrationScripts__DirectoryPath=/app/migrations/Scripts
      - RECEIPTCOLLECTOR_MigrationScripts__CommandTimeoutSeconds=60
    working_dir: /app/migrations
    entrypoint: ["dotnet", "ReceiptCollector.Analytics.Migrations.dll"]
    networks: [collector-net]

  analytics:
    container_name: receipt-analytics
    build: ./Analytics
    image: drypa/receipt-collector-analytics:latest
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
      mongo:
        condition: service_healthy
      analytics-migrations:
        condition: service_completed_successfully
    environment:
      - ASPNETCORE_URLS=http://+:5039
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
      - Infrastructure__Postgres__ConnectionString=Host=postgres;Port=5432;Database=receipts;Username=${PG_LOGIN};Password=${PG_SECRET}
      - Infrastructure__Receipts__Mongo__ConnectionString=mongodb://${MONGO_LOGIN}:${MONGO_SECRET}@mongo:27017/receipt_collection?authSource=admin
      - Infrastructure__Receipts__Mongo__Database=receipt_collection
      - Infrastructure__Receipts__Mongo__Collection=receipt_requests
      - Infrastructure__Receipts__Mongo__UsersCollection=system_users
      - Infrastructure__Receipts__Synchronization__Skip=${ANALYTICS_SYNC_SKIP:-true}
      - Infrastructure__AuthLinks__BaseUrl=${ANALYTICS_AUTHLINK_BASE_URL}
      - Infrastructure__AuthLinks__LifetimeMinutes=15
      - Infrastructure__AdminUsers__TelegramIds__0=${ANALYTICS_ADMIN_TELEGRAM_ID_0}
      - Infrastructure__AdminUsers__TelegramIds__1=${ANALYTICS_ADMIN_TELEGRAM_ID_1}
    expose:
      - "5039"
    networks: [collector-net]
    volumes:
      - analytics-wwwroot:/srv/wwwroot
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:5039/health"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 30s

  nginx:
    container_name: receipt-nginx
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.prod.conf:/etc/nginx/nginx.conf:ro
      - ${SSL_CERTS_PATH}:/etc/nginx/ssl/certs/
      - analytics-wwwroot:/usr/share/nginx/html:ro
    depends_on:
      analytics:
        condition: service_healthy
    networks: [collector-net]

volumes:
  analytics-wwwroot:

networks:
  collector-net:
```

Ключевые моменты схемы:

- `analytics` зависит от готовности **обеих БД** (FR-5.3) и от **успешного завершения миграций**
  (FR-4.2). `nginx` стартует после healthy Analytics — к этому моменту общий том уже наполнен
  статикой (FR-3.2).
- `collector` и `bot` переведены на `condition: service_healthy` для mongo (FR-5.1).
- `analytics-wwwroot` — named volume: в Analytics монтируется в `/srv/wwwroot` (наполняется
  entrypoint'ом), в nginx — read-only в `/usr/share/nginx/html`.
- `expose: ["5039"]` не публикует порт наружу — наружу смотрит только nginx.
- Строки подключения собираются compose-интерполяцией из существующих переменных `.env`
  (`PG_LOGIN`/`PG_SECRET`, `MONGO_LOGIN`/`MONGO_SECRET`) — единый источник учётных данных,
  без дублирования секретов в `.env`.

#### 2. `Analytics/Dockerfile` (сборка фронтенда + оба приложения + curl)

```dockerfile
# ---------- Стадия сборки фронтенда ----------
FROM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build
# Результат (vite outDir из vite.config.ts): /src/src/ReceiptCollector.Analytics.Api/wwwroot

# ---------- Стадия сборки .NET ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY *.sln .
COPY src/ReceiptCollector.Analytics.Api/ReceiptCollector.Analytics.Api.csproj ./src/ReceiptCollector.Analytics.Api/
COPY src/ReceiptCollector.Analytics.Application/ReceiptCollector.Analytics.Application.csproj ./src/ReceiptCollector.Analytics.Application/
COPY src/ReceiptCollector.Analytics.Domain/ReceiptCollector.Analytics.Domain.csproj ./src/ReceiptCollector.Analytics.Domain/
COPY src/ReceiptCollector.Analytics.Infrastructure/ReceiptCollector.Analytics.Infrastructure.csproj ./src/ReceiptCollector.Analytics.Infrastructure/
COPY src/ReceiptCollector.Analytics.Migrations/ReceiptCollector.Analytics.Migrations.csproj ./src/ReceiptCollector.Analytics.Migrations/
COPY tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj ./tests/ReceiptCollector.Analytics.Api.Tests/
RUN dotnet restore

COPY . .
# Свежая сборка фронтенда поверх контекста (не перезаписывается `COPY . .`)
COPY --from=frontend /src/src/ReceiptCollector.Analytics.Api/wwwroot ./src/ReceiptCollector.Analytics.Api/wwwroot

WORKDIR /src/src/ReceiptCollector.Analytics.Api/
RUN dotnet publish -c Release -o /app/publish/api --no-restore

WORKDIR /src/src/ReceiptCollector.Analytics.Migrations/
RUN dotnet publish -c Release -o /app/publish/migrations --no-restore

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY docker-entrypoint.sh .
RUN chmod +x docker-entrypoint.sh
EXPOSE 5039
ENTRYPOINT ["/app/docker-entrypoint.sh"]
```

Пояснения:

- **node:22-alpine** — Vite 7 требует Node ≥ 20.19; pin-версия `package-lock.json` даёт
  воспроизводимую установку через `npm ci`.
- Порядок `COPY . .` → `COPY --from=frontend ...` обязателен: `COPY . .` не должен
  перезаписать свежие ассеты локальным (возможно, устаревшим) `wwwroot`. Дополнительно в
  `Analytics/.dockerignore` добавляется `**/wwwroot/`, чтобы локальные артефакты сборки вообще
  не попадали в build-контекст.
- Оба приложения (API и Migrations) публикуются в один образ; в миграционный выход
  автоматически попадают `Scripts/` и `appsettings.json` (задано в csproj через
  `CopyToOutputDirectory`).
- `curl` в runtime-образе нужен для healthcheck контейнера (в `aspnet:10.0` его нет).
- `docker-entrypoint.sh` синхронизирует статику в общий том (см. ниже). Для сервиса миграций
  entrypoint переопределяется в compose, поэтому скрипт на нём не выполняется.

#### 3. `docker-entrypoint.sh` (в корне `Analytics/`)

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

Идемпотентен, выполняется при каждом старте контейнера Analytics → ассеты в томе всегда
актуальны (важно при обновлении образа).

#### 4. Конфигурация через переменные окружения (маппинг .NET)

API использует стандартный провайдер env .NET: `__` заменяет `:`. Миграции — провайдер с
префиксом `RECEIPTCOLLECTOR_` (уже реализован в `Program.cs` миграций).

| Компонент | Переменная окружения | Назначение / значение |
|---|---|---|
| API | `ASPNETCORE_URLS` | `http://+:5039` — порт прослушивания (FR-6.1) |
| API | `ASPNETCORE_ENVIRONMENT` | `Production` |
| API | `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` — корректный scheme/HTTPS за nginx (X-Forwarded-Proto) |
| API | `Infrastructure__Postgres__ConnectionString` | Npgsql-строка (`Host=postgres;...;Database=receipts`) |
| API | `Infrastructure__Receipts__Mongo__ConnectionString` | MongoDB URI c `authSource=admin` |
| API | `Infrastructure__Receipts__Mongo__Database` | `receipt_collection` |
| API | `Infrastructure__Receipts__Mongo__Collection` | `receipt_requests` |
| API | `Infrastructure__Receipts__Mongo__UsersCollection` | `system_users` |
| API | `Infrastructure__Receipts__Synchronization__Skip` | `true` на первом деплое (данные мигрируют вручную/позже) |
| API | `Infrastructure__AuthLinks__BaseUrl` | публичный URL логина, напр. `https://<домен>/login` |
| API | `Infrastructure__AuthLinks__LifetimeMinutes` | `15` |
| API | `Infrastructure__AdminUsers__TelegramIds__0..N` | Telegram ID администраторов (обязательны к заполнению) |
| Migrations | `RECEIPTCOLLECTOR_Infrastructure__Postgres__ConnectionString` | та же строка PostgreSQL |
| Migrations | `RECEIPTCOLLECTOR_MigrationScripts__DirectoryPath` | `/app/migrations/Scripts` |
| Migrations | `RECEIPTCOLLECTOR_MigrationScripts__CommandTimeoutSeconds` | `60` |

Новые ключи `.env` (файл формируется на сервере вручную; репозиторий не меняется):

- `ANALYTICS_AUTHLINK_BASE_URL` — публичный URL (https) для ссылок авторизации.
- `ANALYTICS_ADMIN_TELEGRAM_ID_0`, `ANALYTICS_ADMIN_TELEGRAM_ID_1`, … — ID админов.
- `ANALYTICS_SYNC_SKIP` — опционально (`true` по умолчанию).

Замечания:

- В проде **ничего не берётся из `appsettings.json`**, кроме ненастраиваемых значений по
  умолчанию (Logging, `AllowedHosts`). Секретная конфигурация — только env (FR-2.3, NFR-4).
- **Рекомендуется** (опциональный cleanup, не блокер): привести форму `AdminUsers` в
  `appsettings.json` к `{ "TelegramIds": [...] }` либо удалить секцию из файла — текущая
  форма-массив не биндится в `AdminUserOptions.TelegramIds`, из-за чего список админов в проде
  без env пуст.
- В `appsettings.json` проекта миграций строку-заглушку `Host=localhost;...` рекомендуется
  очистить (env всё равно переопределит) — чтобы placeholder не попадал в продакшен-образ.

#### 5. `nginx.prod.conf` (статика + прокси API)

```nginx
events {
    worker_connections 1024;
}

http {
    upstream analytics_api {
        server analytics:5039;   # Единый порт API (FR-6.1)
    }

    # HTTP -> HTTPS
    server {
        listen 80;
        server_name _;
        return 301 https://$host$request_uri;
    }

    server {
        listen 443 ssl http2;
        server_name localhost;  # заменить на реальный домен при необходимости

        ssl_certificate     /etc/nginx/ssl/certs/fullchain.pem;
        ssl_certificate_key /etc/nginx/ssl/certs/private.key;

        add_header X-Frame-Options "SAMEORIGIN" always;
        add_header X-XSS-Protection "1; mode=block" always;
        add_header X-Content-Type-Options "nosniff" always;
        add_header Referrer-Policy "no-referrer-when-downgrade" always;
        add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

        # Статика фронтенда (раздаётся nginx, не проксируется)
        root /usr/share/nginx/html;
        index index.html;

        # API -> Analytics
        location /api {
            proxy_pass http://analytics_api;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_set_header X-Forwarded-Host $server_name;
            proxy_connect_timeout 30s;
            proxy_send_timeout 30s;
            proxy_read_timeout 30s;
        }

        # Кэшируемые ассеты сборки
        location /assets/ {
            expires 1y;
            add_header Cache-Control "public, immutable";
        }

        # SPA-fallback
        location / {
            try_files $uri $uri/ /index.html;
        }

        # Health
        location /health {
            access_log off;
            return 200 "healthy\n";
            add_header Content-Type text/plain;
        }
    }
}
```

Изменения относительно текущего файла:

- удалён upstream `analytics_frontend` (dev-порт `5173`) — FR-3.3;
- `location /` вместо `proxy_pass` раздаёт файлы из `/usr/share/nginx/html` с SPA-fallback —
  FR-3.2; кэширование ассетов перенесено в `location /assets/`;
- upstream `analytics_api` без изменений (порт `5039`).

#### 6. Health-эндпоинт API

В `Program.cs` (инфраструктурное изменение, не бизнес-логика) добавляется:

```csharp
app.MapGet("/health", () => Results.Ok("healthy"));
```

Liveness-эндпоинт; готовность БД и миграций обеспечивается условиями `depends_on` в compose.

#### 7. CI и ветки

- `.github/workflows/build-analytics-image.yml` **менять не требуется**: имя
  `drypa/receipt-collector-analytics` уже совпадает с новым `image:` в compose (FR-1.1).
- Для публикации тега `latest` (FR-1.3) необходимо **смёржить `feature/analytics` в `master`**
  (текущая схема CI пушит `latest` только для дефолтной ветки). Альтернатива — добавить в CI
  тег `latest` для отдельной deploy-ветки; для проекта достаточно merge.
- Локальная сборка `./build.sh` (docker compose build) собирает analytics из контекста
  `./Analytics` — внутри Dockerfile выполняется `npm ci`, поэтому окружение сборки должно иметь
  доступ к npm-реестру.

## Последствия

### Положительные

- **Деплой одной командой** (NFR-1): `./up.sh` на сервере с заполненным `.env` поднимает весь
  стек; критерии приёмки 1–3, 5, 8, 9 задачи выполняются.
- **Фронтенд в образе** (FR-3.1): образ Analytics самодостаточен; nginx раздаёт статику без
  dev-проксирования; критерий приёмки 4 и 6 выполнен.
- **Детерминированные миграции** (FR-4, NFR-2): отдельный сервис применяет скрипты до старта
  API; повторный запуск безопасен.
- **Наблюдаемость** (NFR-3): healthcheck'и mongo/postgres/analytics дают корректный порядок
  запуска и состояние `healthy`.
- **Конфигурация через env** (FR-2, NFR-4): секреты — только в `.env` на сервере, без правки
  `appsettings.json`; учётные данные БД берутся из одного источника (`PG_LOGIN`/`PG_SECRET`,
  `MONGO_LOGIN`/`MONGO_SECRET`).
- **Dev-контур не затронут** (NFR-5): `docker-compose.develop.yml` и `up.dev.sh` не меняются.

### Отрицательные

- `AdminUsers` в `appsettings.json` несовместимой формы — до cleanup список админов в проде
  определяется только env; при незаполненных `ANALYTICS_ADMIN_TELEGRAM_ID_*` доступ админов в
  webUI отсутствует.
- В runtime-образ Analytics попадает бинарник миграций (небольшой объём) и `curl`
  (увеличение образа).
- `UseHttpsRedirection` в контейнере работает как no-op (нет HTTPS-порта): без
  `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` схема запросов за nginx выглядела бы как `http`;
  при включённом forwarded-headers поведение корректно.

### Компромиссы

- **Один образ на два приложения (API + Migrations)** вместо отдельных образов — принято в
  пользу одного артефакта/тега/CI-джоба; потеря изоляции незначительна для self-hosted проекта.
- **Общий том с синхронизацией entrypoint'ом** вместо автономной сборки фронтенда в nginx-образе —
  принято во избежание двойной сборки фронтенда; цена — маленький скрипт и порядок запуска
  через `depends_on`.
- **Liveness `/health` без проверки БД** — готовность БД гарантируется условиями `depends_on`,
  а не эндпоинтом; для compose этого достаточно.
- **Подъём формата compose до `2.4`** — требует docker-compose ≥ 1.23 либо Docker Compose v2
  (plugin). Для современных серверов с `docker compose` ограничений нет.

### Риски

- **Тег `latest` не опубликован** (ветка `feature/analytics` не смёржена): `docker compose pull`
  упадёт на аналитике. **Митигация:** merge в `master` — обязательный шаг перед деплоем
  (FR-1.3, критерий приёмки 9).
- **Пустой/неполный `.env`**: отсутствие `ANALYTICS_AUTHLINK_BASE_URL` → провал валидации
  `[Url]` на старте; отсутствие admin-id → нет админов. **Митигация:** чек-лист переменных в
  плане реализации; серверный `.env` формируется вручную (решение заказчика).
- **Устаревшая статика в томе**: если Analytics не стартует (например, упали миграции), nginx
  не поднимется (depends_on healthy) либо будет отдавать прежние ассеты. **Митигация:**
  синхронизация тома на каждом старте Analytics + `depends_on` nginx → analytics.
- **Сборка требует npm-реестра**: локальный `docker compose build` и CI-сборка нуждаются в
  сетевом доступе к npm. **Митигация:** `npm ci` по lock-файлу; кэш слоёв в CI
  (`cache-from: type=gha` уже настроен).
- **Формат compose `2.4`** на устаревших установках docker-compose v1. **Митигация:**
  фиксируется требование к Docker Compose v2 в плане реализации.

## Ссылки

- [Задача: Подготовка Analytics-сервиса к продакшен-деплою](../tasks/analytics-production-deployment.md)
- [ADR 001: Nginx Proxy with TLS Termination](001-nginx-proxy-with-tls.md) (роль nginx как
  единственной точки входа, статика + прокси API)
- [ADR 002: Dev Nginx](002-dev-nginx.md) (dev-контур, не меняется)
- [ADR 007: Skip Receipt Synchronization Flag](007-skip-receipt-synchronization-flag.md)
  (семантика `Infrastructure__Receipts__Synchronization__Skip`)
- Фактический код: `docker-compose.yml`, `nginx.prod.conf`, `Analytics/Dockerfile`,
  `Analytics/docker-entrypoint.sh` (новый), `Analytics/.dockerignore`,
  `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs` (добавлен `/health`),
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `Analytics/src/ReceiptCollector.Analytics.Migrations/Program.cs` (префикс `RECEIPTCOLLECTOR_`),
  `.github/workflows/build-analytics-image.yml`
- [План реализации: analytics-production-deployment](../plans/analytics-production-deployment.md)
