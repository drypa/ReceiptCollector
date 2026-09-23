# План: Проброс переменных окружения из `.env` в контейнеры (`docker-compose.yml`)

**Связанный ADR:** [ADR-021](../adr/021-env-vars-passthrough-docker-compose.md)
**Файл задачи:** [pass-env-vars-to-docker-compose.md](../tasks/pass-env-vars-to-docker-compose.md)

## Область изменений

Только `docker-compose.yml` (prod) и одна правка в `.env.example` (см. Изменение 5).
Код сервисов (Go backend, bot, .NET Analytics) — **без изменений**.
`docker-compose.develop.yml` — **без изменений**.

Эмпирически проверено на .NET 10: пустой `AI__Timeout`/`AI__Concurrency`/
`CommodityCategoryCache__MaxSize` (пустая строка) вызывает
`InvalidOperationException: Failed to convert configuration value '' ...`
**при старте** analytics — поэтому для типизированных полей проброс идёт
с `:-`-дефолтами (отклонение от буквы критериев задачи, см. ADR-021).

## Что меняется

### Изменение 1: `docker-compose.yml`, сервис `analytics`, секция `environment`

Добавить после строки `- ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
(порядок не важен, главное — в секции `environment` сервиса `analytics`):

```yaml
      # AI-категоризация товаров (ADR 009, NFR-1). Пустой AI__BaseUrl = категоризация
      # отключена (suggest -> 503). Типизированные поля — с :-дефолтами (крэш при пустой строке).
      - AI__BaseUrl=${AI__BaseUrl}
      - AI__Model=${AI__Model:-qwen}
      - AI__Timeout=${AI__Timeout:-00:00:10}
      - AI__Concurrency=${AI__Concurrency:-3}
      - AI__ApiKey=${AI__ApiKey}
      - CommodityCategoryCache__MaxSize=${CommodityCategoryCache__MaxSize:-1000}
```

### Изменение 2: `docker-compose.yml`, сервис `bot`, секция `environment`

Добавить (без дефолта — пустое значение безопасно: `ParseBool("")` → `false`):

```yaml
      - BOT_DEBUG=${BOT_DEBUG}
```

### Изменение 3: `docker-compose.yml`, сервис `collector`, секция `environment`

Заменить три захардкоженные строки:

Было:

```yaml
      - MONGO_URL=mongodb://mongo:27017
      - GET_RECEIPT_WORKER_INTERVAL=15m
      - NALOGRU_BASE_ADDR=https://irkkt-mobile.nalog.ru:8888
```

Стало:

```yaml
      - MONGO_URL=${MONGO_URL:-mongodb://mongo:27017}
      - GET_RECEIPT_WORKER_INTERVAL=${GET_RECEIPT_WORKER_INTERVAL:-15m}
      - NALOGRU_BASE_ADDR=${NALOGRU_BASE_ADDR:-https://irkkt-mobile.nalog.ru:8888}
```

### Изменение 4 (без изменений — проверить, что не тронуто)

- Контейнерные адреса бота: `BACKEND_GRPC_ADDR=collector:15000`,
  `REPORTS_GRPC_ADDR=collector:15001`, `ANALYTICS_URL=http://analytics:5039` —
  остаются как есть.
- Volume-пути: `RAW_TICKET_DUMP_PATH`, `GET_TICKET_ERROR_PATH`,
  `SSL_CERTS_PATH`, `TEMPLATES_PATH` — остаются как есть.
- `docker-compose.develop.yml` — не трогаем.

### Изменение 5: `.env.example`, раздел «Backend (Go)» — поправить `MONGO_URL`

После перехода на `${MONGO_URL:-mongodb://mongo:27017}` значение
`MONGO_URL=mongodb://localhost:27017` из шаблона сломает collector в контейнере
(`localhost` внутри контейнера — сам контейнер). Заменить:

Было:

```
# URL MongoDB для backend (авторизация задаётся через MONGO_LOGIN/MONGO_SECRET)
MONGO_URL=mongodb://localhost:27017
```

Стало:

```
# URL MongoDB для backend (авторизация задаётся через MONGO_LOGIN/MONGO_SECRET).
# ВАЖНО: в docker compose (prod) значение должно быть контейнерным: mongodb://mongo:27017.
# Для запуска backend с хоста (dev):
#   MONGO_URL=mongodb://localhost:27017
MONGO_URL=mongodb://mongo:27017
```

## Отклонение от критериев приёмки задачи (обоснование)

Критерии задачи требовали `AI__Timeout=${AI__Timeout}` и т.п. без дефолтов
(«как есть»). Эмпирическая проверка показала, что при незаданной переменной
compose передаёт в контейнер **пустую строку**, а .NET-биндинг для `TimeSpan`/`int`
падает при старте сервиса. Поэтому:
- `AI__Timeout`, `AI__Concurrency`, `AI__Model`, `CommodityCategoryCache__MaxSize`
  — с `:-`-дефолтами, равными прод-базису (HEAD `appsettings.json`: `00:00:10`,
  `3`, `qwen`, `1000`);
- `AI__BaseUrl`, `AI__ApiKey` — как в критериях, без дефолтов (пустая строка —
  штатная семантика «AI отключён» / «без авторизации»).
Подробности и матрица поведения — в ADR-021.

## Порядок выполнения

1. Внести изменения 1–3 в `docker-compose.yml` и изменение 5 в `.env.example`.
2. `docker compose config` — убедиться, что интерполяция валидна и в выводе нет
   предупреждений о незаданных переменных (пустой/незаданный `AI__BaseUrl` и
   `BOT_DEBUG` дают в выводе `AI__BaseUrl:` с пустым значением и `BOT_DEBUG:`
   с пустым значением — это ожидаемо).
3. Проверить, что `docker-compose.develop.yml` не модифицирован
   (`git status`).

## Проверка критериев приёмки (по задаче + ADR)

- [ ] В `docker-compose.yml` (сервис `analytics`) добавлены `AI__*` и
      `CommodityCategoryCache__MaxSize` (с дефолтами для типизированных полей).
- [ ] В `docker-compose.yml` (сервис `bot`) добавлен `BOT_DEBUG=${BOT_DEBUG}`.
- [ ] В `docker-compose.yml` (сервис `collector`) `MONGO_URL`,
      `GET_RECEIPT_WORKER_INTERVAL`, `NALOGRU_BASE_ADDR` переведены на
      `:-`-проброс с прод-дефолтами.
- [ ] `docker-compose.develop.yml` не изменён.
- [ ] Контейнерные адреса бота и volume-пути не изменены.
- [ ] `docker compose config` проходит без ошибок интерполяции.
- [ ] Поведенческая проверка (на стенде): при пустом `.env` аналитик стартует,
      `POST /api/receipts/{id}/categories/suggest` отвечает 503;
      при `AI__BaseUrl=<url>` в `.env` — категоризация работает, модель/таймаут/
      concurrency берутся из `.env`.

## Риски и митигация

| Риск | Вероятность | Митигация |
|------|-------------|-----------|
| Операторы со старым `.env` (`MONGO_URL=localhost`) после деплоя | Средняя | Правка шаблона `.env.example` (Изменение 5) + упоминание в релиз-нотах/ADR |
| Дрейф дефолтов compose vs `appsettings.json` | Низкая | Правило: `:-`-дефолты синхронизировать с HEAD `appsettings.json` |
| Незакоммиченные правки `appsettings.json` (AI: `127.0.0.1:1234/v1`) | Низкая | Сверить и решить: закоммитить или откатить; в контейнере значение перекрывается compose |