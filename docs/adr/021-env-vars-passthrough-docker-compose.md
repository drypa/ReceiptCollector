# ADR-021: Проброс переменных окружения из `.env` в контейнеры (`docker-compose.yml`)

## Статус

Принято (проект). Основание — задача
[pass-env-vars-to-docker-compose](../tasks/pass-env-vars-to-docker-compose.md)
(требования и критерии приёмки).

Изменяет только файл развёртывания, в коде сервисов ничего не меняется.
Контракты, описанные в ADR 009 (опции `AI:*`), ADR 018 (env бота) и ADR 019
(опции `CommodityCategoryCache:*`), **не изменяются** — настоящий документ
закрепляет их проброс через `docker-compose.yml` (prod).

## Контекст

### Проблема

В `.env.example` описаны переменные, но в `docker-compose.yml` они не
пробрасываются или захардкожены:


| Секция | Переменные | Статус в `docker-compose.yml` |
|--------|-----------|-------------------------------|
| `analytics` | `AI__BaseUrl`, `AI__Model`, `AI__Timeout`, `AI__Concurrency`, `AI__ApiKey`, `CommodityCategoryCache__MaxSize` | **не пробрасываются** — секция `AI` из `appsettings.json` используется как есть |
| `bot` | `BOT_DEBUG` | **не пробрасывается** |
| `collector` | `MONGO_URL`, `GET_RECEIPT_WORKER_INTERVAL`, `NALOGRU_BASE_ADDR` | **захардкожены** (`mongodb://mongo:27017`, `15m`, `https://irkkt-mobile.nalog.ru:8888`) |

Это ограничивает конфигурируемость прод-запуска через `.env` без пересборки.

### Цель

Все релевантные переменные `.env.example` должны пробрасываться в контейнеры
через `docker-compose.yml` (prod); для collector — с дефолтами, сохраняющими
текущее прод-поведение. Пустой `AI__BaseUrl` в контейнере = AI-категоризация
отключена (503), как задумано в ADR 009 и `.env.example`.

## Исследование и эмпирическая проверка

Проверка выполнялась на реальном .NET SDK (временный проект + реальный
`ConfigurationBuilder` с `appsettings.json` и `AddEnvironmentVariables`,
семантика секций `AI`/`CommodityCategoryCache` скопирована из проекта).

### 1. Резолвинг пустых строк в .NET-конфигурации (`AI__*`, стиль `__`)

Ключевые факты (подтверждены запуском):

- **Env-переменная (даже со значением пустой строки) имеет приоритет над
  `appsettings.json`** и перекрывает его. Порядок провайдеров стандартный:
  `appsettings.json` → `appsettings.{Env}.json` → environment variables.
- Для строковых свойств пустая строка биндится нормально:
  `AI__BaseUrl=""` → `AI:BaseUrl = ""` → эндпоинт `suggest` отвечает **503**
  (`ReceiptCategorizationEndpoints.Suggest` использует
  `string.IsNullOrWhiteSpace(...)`). То есть **пустой `AI__BaseUrl` в контейнере
  корректно отключает категоризацию** — значение `appsettings.json` не
  «выживает».
- Для **типизированных свойств пустая строка — фатальный сбой старта**:

  ```
  System.InvalidOperationException: Failed to convert configuration value ''
  at 'AI:Timeout' to type 'System.TimeSpan'.
  ```

  Подтверждено для всех трёх типизированных полей:

  | Env-переменная | Тип | `AI__X=` (пусто) |
  |----------------|-----|-------------------|
  | `AI__BaseUrl`  | `string` | безопасно → AI отключён (503) |
  | `AI__Model`    | `string` | безопасно, но при включённом AI модель станет пустой → ошибка запроса к AI-API |
  | `AI__Timeout`  | `TimeSpan` | **краш при старте** |
  | `AI__Concurrency` | `int` | **краш при старте** |
  | `AI__ApiKey`   | `string` | безопасно → без заголовка Authorization |
  | `CommodityCategoryCache__MaxSize` | `int` | **краш при старте** |

  Сбой происходит в момент `config.GetSection("AI").Bind(options)` в
  `DependencyInjectionExtensions.ConfigureInfrastructureOptions` — **до** старта
  приложения; обработчик `PostConfigure` не спасает (падает сам биндинг).

  > Вывод: буквальные критерии задачи («все `AI__*` пробрасывать как есть,
  > без дефолтов») при незаданных в `.env` переменных привели бы к падению
  > контейнера `analytics` в самом частом сценарии (оператор скопировал
  > `.env.example`, где `#AI__Timeout` и др. закомментированы). Требуется
  > отклонение от буквы критериев — см. «Решение».

- Конфликта добавляемых `AI__*` / `CommodityCategoryCache__MaxSize` с уже
  пробрасываемыми `Infrastructure__*` **нет**: ключи с разделителем `__`
  маппятся (`__` → `:`) в разные корневые секции конфигурации
  (`AI`, `CommodityCategoryCache`, `Infrastructure`), поддеревья не пересекаются.

### 2. Базовые (прод) значения по умолчанию для `:-`

В качестве дефолтов для `:-` берутся **закоммиченные** значения
`appsettings.json` (HEAD), которые совпадают с дефолтами в коде
(`AiOptions`/`CommodityCategoryCacheOptions`) и с примерами в `.env.example`:

| Поле | HEAD `appsettings.json` | Дефолт кода | `.env.example` |
|------|------------------------|-------------|----------------|
| `AI:Model` | `qwen` | `qwen` | `#AI__Model=qwen` |
| `AI:Timeout` | `00:00:10` | `00:00:10` | `#AI__Timeout=00:00:10` |
| `AI:Concurrency` | `3` | `3` | `#AI__Concurrency=3` |
| `AI:ApiKey` | `""` | — | `#AI__ApiKey=` |
| `AI:BaseUrl` | `""` (AI выключен) | — | `#AI__BaseUrl=...` (пусто = выключен) |
| `CommodityCategoryCache:MaxSize` | `1000` | `1000` | `#CommodityCategoryCache__MaxSize=1000` |

Примечание: в рабочей копии `appsettings.json` есть **несохранённые** правки
(`AI:BaseUrl = http://127.0.0.1:1234/v1`, `Model = nvidia/nemotron-3-nano-4b`,
`Timeout = 00:01:20`) — вероятно, локальный dev-эксперимент. Они не являются
прод-базисом и не участвуют в выборе дефолтов; напомнить автору не коммитить
их без необходимости (см. «Риски»).

### 3. Bot (`BOT_DEBUG`)

`bot/options.go`: `debug, _ = strconv.ParseBool(getEnvVar("BOT_DEBUG"))`.
`strconv.ParseBool("")` возвращает ошибку и `false`; ошибка игнорируется.
Вывод: **`BOT_DEBUG=${BOT_DEBUG}` без дефолта безопасно** — пустое значение
= дебаг выключен. Прод-поведение при пустом `.env` не меняется (сейчас
переменная в контейнер вообще не передаётся → бот всегда получал пустую).

### 4. Collector (`MONGO_URL`, `GET_RECEIPT_WORKER_INTERVAL`, `NALOGRU_BASE_ADDR`)

- `MONGO_URL` читается `os.Getenv` и используется как URI напрямую
  (`mongo_client.New` → `ApplyURI(url)` + auth отдельными env). Значение по
  умолчанию `mongodb://mongo:27017` — контейнерное имя сервиса, сохраняется.
- `GET_RECEIPT_WORKER_INTERVAL` парсится `time.ParseDuration`; пустая строка →
  ошибка → **fallback на 1 минуту** (код `workers/settings.go`). Это важно:
  проброс «как есть» без дефолта при незаданном env молча сменил бы прод-интервал
  15m на dev-интервал 1m. Поэтому дефолт `:-15m` обязателен.
- `NALOGRU_BASE_ADDR` читается как есть; дефолт совпадает с `.env.example`.

### 5. Футган `MONGO_URL` в `.env.example`

В `.env.example` сейчас `MONGO_URL=mongodb://localhost:27017` (значение для
запуска backend **с хоста**). После перехода с хардкода на `${MONGO_URL:-...}`
оператор, скопировавший `.env.example` в `.env`, сломает прод-контейнер:
внутри контейнера `localhost` — это сам контейнер, а не сервис `mongo`.
Требуется **синхронное изменение `.env.example`** на контейнерное значение
`mongodb://mongo:27017` (с комментарием про хост-запуск).

## Рассмотренные варианты

### A. Пробросить все `AI__*` «как есть» (буквальные критерии задачи) — **отклонён**

`AI__Timeout=${AI__Timeout}` и т.п. без дефолтов. При незаданных в `.env`
переменных compose интерполирует пустую строку → аналитик **падает на старте**
(см. «Исследование», п. 1). Сценарий «оператор не настраивает AI» (самый
частый) превращается в недоступный сервис. Также пустой `AI__Model` при
включённом AI даёт молчаливую ошибку запросов.

### B. Проброс с `:-`-дефолтами, сохраняющими прод-базис — **выбран**

Строковые поля-«выключатели» (`AI__BaseUrl`, `AI__ApiKey`) — как есть (пусто
= отключено/без авторизации, это и есть желаемая семантика). Типизированные
поля и `AI__Model` — с `:-`-дефолтами, равными HEAD-`appsettings.json`:
- пустой `.env` → прод-поведение сохраняется (включая `GET_RECEIPT_WORKER_INTERVAL=15m`),
  крэшей нет;
- заданные в `.env` значения → корректно переопределяют дефолты
  (проверено: `AI__BaseUrl` + `Model`/`Timeout`/`MaxSize` из `.env` биндятся).

### C. Устойчивость на стороне .NET (толерантный к пустым строкам биндинг) — **отклонён**

Вариант «не передавать дефолты в compose, а научить .NET игнорировать пустые
типизированные значения». Требует смены типов опций (`string? Timeout` + парсинг
с fallback) или кастомного `IConfigurationBinder` в `Analytics` — инвазивно,
противоречит ограничению задачи («только изменение docker-compose.yml»), даёт
двойные источники дефолтов. Риск не оправдан, когда дефолты в compose решают
проблему одной строкой.

## Решение

### 1. `docker-compose.yml`, сервис `analytics`, секция `environment` — добавить

```yaml
      - AI__BaseUrl=${AI__BaseUrl}
      - AI__Model=${AI__Model:-qwen}
      - AI__Timeout=${AI__Timeout:-00:00:10}
      - AI__Concurrency=${AI__Concurrency:-3}
      - AI__ApiKey=${AI__ApiKey}
      - CommodityCategoryCache__MaxSize=${CommodityCategoryCache__MaxSize:-1000}
```

Семантика:
- `AI__BaseUrl` — без дефолта: пустой/незаданный `.env` → контейнер получает
  `AI__BaseUrl=` → секция `AI:BaseUrl` перекрывается пустой строкой →
  `IsNullOrWhiteSpace` → `suggest` отвечает **503** (категоризация отключена,
  сервис здоров) — ровно как задумано в ADR 009/.env.example;
- `AI__Timeout`, `AI__Concurrency`, `AI__Model`, `CommodityCategoryCache__MaxSize`
  — дефолты **равны прод-базису** (`00:00:10`, `3`, `qwen`, `1000`) — защищают
  от крэша пустыми строками и от молчаливо пустой модели;
- `AI__ApiKey` — без дефолта: пусто = без заголовка Authorization (совпадает с
  прод-базисом `""`).

### 2. `docker-compose.yml`, сервис `bot`, секция `environment` — добавить

```yaml
      - BOT_DEBUG=${BOT_DEBUG}
```

Без дефолта: пусто → `strconv.ParseBool("")` → `false` (проверено по коду).

### 3. `docker-compose.yml`, сервис `collector`, секция `environment` — заменить хардкод

```yaml
      - MONGO_URL=${MONGO_URL:-mongodb://mongo:27017}
      - GET_RECEIPT_WORKER_INTERVAL=${GET_RECEIPT_WORKER_INTERVAL:-15m}
      - NALOGRU_BASE_ADDR=${NALOGRU_BASE_ADDR:-https://irkkt-mobile.nalog.ru:8888}
```

Дефолты в точности повторяют текущие прод-значения.

### 4. `.env.example` — синхронно поправить `MONGO_URL`

```
MONGO_URL=mongodb://mongo:27017
```

с комментарием: контейнерное значение для `docker compose` (prod); при запуске
backend с хоста использовать `mongodb://localhost:27017`. Без этой правки
оператор, копирующий `.env.example` в `.env`, после п. 3 сломает подключение
collector к MongoDB (внутри контейнера `localhost` — сам контейнер).

### 5. Ограничения (не трогать)

- `docker-compose.develop.yml` — без изменений.
- Контейнерные адреса бота (`BACKEND_GRPC_ADDR`, `REPORTS_GRPC_ADDR`,
  `ANALYTICS_URL`) и volume-пути (`RAW_TICKET_DUMP_PATH`,
  `GET_TICKET_ERROR_PATH`, `SSL_CERTS_PATH`, `TEMPLATES_PATH`) — без изменений.
- `analytics-migrations` не получает `AI__*` — миграциям настройки категоризации
  не нужны.
- Код сервисов не изменяется.

## Последствия

### Положительные

- AI-настройки, `BOT_DEBUG` и collector-переменные становятся конфигурируемыми
  через `.env` без пересборки (цель задачи).
- Пустой/незаданный `AI__BaseUrl` в отверждении: категоризация отключена (503),
  сервис работает. Прод-побочный эффект: даже если в образ попадёт
  «dev-дефолт» `127.0.0.1:1234/v1` (как в незакоммиченной правке
  `appsettings.json`), compose передаст пустое значение — аналитик не будет
  стучаться в несуществующий локальный AI.
- Для collector пустой `.env` = ровно текущее прод-поведение (проверено
  дефолтами `:-`).
- Нет крэшей от пустых строк: дефолты для типизированных полей и `AI__Model`.

### Отрицательные

- **Типизированные дефолты «затеняют» `appsettings.json`**: контейнер всегда
  получает `AI__Timeout`/`AI__Concurrency`/`AI__Model`/`MaxSize` явно. Сейчас
  значения совпадают с HEAD-`appsettings.json`, поведение идентично; но при
  будущем изменении `appsettings.json` дефолты в compose придётся обновлять
  синхронно (иначе в контейнере останутся старые значения).
- `.env.example` для dev (`GET_RECEIPT_WORKER_INTERVAL=1m`, `BOT_DEBUG=true`)
  после перехода влияет на прод, если оператор использует шаблон как есть —
  поведение соответствует документированной конфигурации, но отличается от
  прод-дефолтов. Оставить как есть (шаблон дев-ориентированный); прод-оператор
  выставляет свои значения.
- `AI__Timeout`/`AI__Concurrency` в `.env` с невалидным значением по-прежнему
  уронят старт (передаётся мусор) — это штатное поведение валидации
  конфигурации, не регресс.

### Компромиссы

- **Дефолты в compose вместо «как есть»** (отклонение от буквы критериев
  задачи): допустимая строгость опций против крэша старта при пустом `.env`.
  Дефолты выбраны равными прод-базису, поэтому семантика «пусто = прод-как
  было» сохраняется.
- **Не трогаем код аналитика** ради толерантности к пустым строкам (вариант C):
  одна строка в compose решает проблему дешевле, чем кастомный биндинг.

### Риски

- **Незакоммиченные правки `appsettings.json`** (AI-секция переведена на
  локальный `127.0.0.1:1234/v1`, `nvidia/nemotron-3-nano-4b`, `00:01:20`).
  Если они уйдут в прод и compose-проброс будет применён — поведение в
  контейнере всё равно станет консистентным (compose перекрывает). Если же
  проброс ещё не применён, а правки закоммичены — прод получит попытки ходить
  в локальный AI. **Митигация:** перед деплоем закоммитить/откатить правки
  осознанно; в свежих образах `AI:BaseUrl` из compose всегда пуст, пока не задан
  в `.env`.
- **Дрейф дефолтов compose vs `appsettings.json`** при будущих изменениях.
  **Митигация:** правило поддержки — дефолты `:-` в compose держать равными
  HEAD-`appsettings.json`; изменение одного влечёт изменение другого.
- **`MONGO_URL=localhost` из старого `.env`** (уже скопированного оператором) —
  после перехода на проброс сломает collector. **Митигация:** предупредить
  операторов; значение в шаблоне `.env.example` исправлено на контейнерное.

## Связь с ADR

- ADR 009 (опции `AI:*`): контракты не меняются; зафиксирован проброс через
  compose и подтверждено поведение «пустой `BaseUrl` = 503».
- ADR 019 (опции `CommodityCategoryCache:*`): контракт не меняется; зафиксирован
  проброс `CommodityCategoryCache__MaxSize` через compose с дефолтом 1000.
- ADR 018 (env бота, `TG_PROXY_URL`): не затрагивается; добавлен проброс
  `BOT_DEBUG`, дебаг-логирование бота.

## Ссылки

- [Задача: pass-env-vars-to-docker-compose](../tasks/pass-env-vars-to-docker-compose.md)
- [ADR 009: Автоматическая категоризация товаров в чеке](009-auto-commodity-categorization.md)
- [ADR 018: Прокси Telegram-клиента бота без влияния на внутренний трафик](018-bot-no-proxy-internal-traffic.md)
- [ADR 019: In-memory кэш категорий товаров](019-in-memory-commodity-category-cache.md)
- [План: Проброс переменных окружения из .env в docker-compose](../plans/pass-env-vars-to-docker-compose.md)
- Фактический код: `backend/main.go`, `backend/mongo_client/mongo-client.go`,
  `backend/workers/settings.go`, `bot/options.go`,
  `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs`,
  `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs`,
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` (HEAD)