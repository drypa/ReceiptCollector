# План: Починка импорта новых чеков в Analytics (Mongo → PostgreSQL)

**Файл задачи:** [analytics-receipt-import-fix.md](../tasks/analytics-receipt-import-fix.md)
**Связанные ADR:** [ADR-007](../adr/007-skip-receipt-synchronization-flag.md) (флаг `Skip`; уточняется умолчание для продуктива), [ADR-012](../adr/012-analytics-production-deployment.md) (конфигурация прода)
**Новый ADR:** не требуется (обоснование в разделе «Тип решения»)

## Резюме решения

Поломка импорта новых чеков — это **дефект соответствия форматов данных** между слоем
синхронизации Analytics и коллекцией-источником `raw_tickets`, усугублённый двумя
конфигурационными ошибками прода (`Collection=receipt_requests` вместо `raw_tickets`,
`Skip=true` по умолчанию). Исправление целиком лежит внутри сервиса Analytics
(слой синхронизации, слой чтения Mongo, миграция его собственной БД PostgreSQL).

Границы сервисов **не меняются**: backend (Go), бот, UI, nginx не трогаем. Analytics
по-прежнему читает из Mongo (теперь — из `raw_tickets` и `receipt_requests` для владельца)
и пишет в свою PostgreSQL.

Ключевые изменения:

1. **Источник чеков** — коллекция `raw_tickets` (полный чеки от `nalogru.TicketDetails`),
   а не `receipt_requests` (где лежат только «запросы на добавление»).
2. **Формат документа** — новый слой чтения `RawTicketDocument` поверх `BsonDocument`
   с устойчивостью к регистру ключей и вложенному payload
   (`ticket.document.receipt`, а не только верхнеуровневый `receipt`).
3. **Владелец** — резолвится в Analytics через связку `raw_tickets.id` → 
   `receipt_requests.ticket_id` → `owner`. Не найден — пропуск с логом.
4. **Дедупликация** — по естественному ключу `(user_id, purchased_at, total_amount)`
   (unique-индекс в PostgreSQL) + проверка по `external_id` (id тикета) для идемпотентности
   повторных циклов.
5. **Периодичность** — `BackgroundService` с первым запуском при старте и далее каждые
   5–15 минут (настраиваемо), без падения процесса при ошибках.
6. **Прод-конфигурация** — `Collection=raw_tickets`, `Skip=false`, интервал 600 с.

## Тип решения и обоснование отсутствия ADR

По классификации это **починка/исправление соответствия данных**, а не изменение
архитектурных границ:

- Исправляется дефектный контур «Mongo → PG», спроектированный ранее (ADR-007 эпохи
  первого деплоя): источник всегда должен был быть коллекцией полных чеков, а в проде
  оказалась ошибочно настроена коллекция «запросов».
- Все изменяемые компоненты принадлежат сервису Analytics (слой синхронизации, DTO,
  конфигурация источника, миграция его БД). Межсервисных контрактов не затрагиваем.
- Принятые с пользователем решения (source = `raw_tickets`, владелец через
  `receipt_requests`, периодичность, дедупликация) фиксируются как требования задачи
  и детализируются в этом плане.

**Вывод:** новый ADR не создаём. Если позже потребуется зафиксировать решение как
прецедент (например, из-за согласований «какая коллекция является источником истины»),
допустим короткий ADR-021 с перечислением четырёх решений D1–D4 — но это не блокирует
реализацию.

## Уточнение диагностики (важно, проверено по исходникам)

В контексте диагностики указано, что `nalogru.TicketDetails` сериализуется по
json-тегам (`createdAt`, `dateTime`, `totalSum`, …). Это **механически неверно** для
используемой версии драйвера:

- Backend использует `go.mongodb.org/mongo-driver v1.17.9`
  (`backend/go.mod`), клиент создаётся с **дефолтным реестром** (`backend/mongo_client/mongo-client.go`,
  `options.Client()` без `.SetRegistry(...)`, `mgocompat` не используется).
- Дефолтный `DefaultStructTagParser` читает только тег `bson`
  (`bson/bsoncodec/struct_tag_parser.go`). Для полей без bson-тега
  `describeStruct` **приводит имя поля к нижнему регистру** (комментарий в
  `bson/bsoncodec/struct_codec.go`, декодирование: `fm[strings.ToLower(name)]`).

Следовательно, реальные ключи в `raw_tickets` — **lowercase**:
`id`, `status`, `kind`, `createdat`, `qr`, `operation`, `query`, `ticket`, `seller`;
вложенно: `ticket.document.receipt` c ключами `datetime` (int64 unix-секунды),
`totalsum`, `userinn`, `cashtotalsum`, `ecashtotalsum`, `retailplaceaddress`,
`nds10`, `nds18` (поле `Nds18` → нижний регистр, json-опечатка `nd18` на хранение
**не влияет**), `items` c `name/price/sum/quantity/nds/ndssum/paymenttype/producttype`.

Это уточнение **упрощает** задачу: существующие lowercase-имена `[BsonElement]` в
`MongoReceiptDocumentDto` уже совпадают с форматом `raw_tickets`. Реальные расхождения
только четыре: отсутствие `owner`, отсутствие верхнеуровневого `receipt`, тип
`datetime` (число вместо строки), `_id`/`id` для дедупликации.

Тем не менее дизайн делаем **устойчивым к обеим версиям реальности** (lowercase И
camelCase-ключи): нормализация ключей документа в НижнийРегистр перед привязкой +
явный alias `nd18 → nds18`. Первый шаг реализации — контрольный снимок реального
документа из прода (см. «Порядок выполнения», шаг 0).

## Область изменений

Только два модуля + конфигурация:

- `Analytics/src/ReceiptCollector.Analytics.Infrastructure` — слой синхронизации и
  источников Mongo (основной объём).
- `Analytics/src/ReceiptCollector.Analytics.Migrations` — новая SQL-миграция
  (дедупликационный unique-индекс).
- `Analytics/tests/...` — обновление и добавление тестов.
- `docker-compose.yml`, `.env`, `.env.example`, `appsettings.json` — конфигурация прода.

## Архитектурные решения

### D1. Источник чеков — `raw_tickets` (принято с пользователем)

`Infrastructure:Receipts:Mongo:Collection=raw_tickets`. `receipt_requests` перестаёт
быть источником чеков и становится источником **привязки владельца** (см. D3).
Legacy-документы 2020 года в `receipt_requests` больше не сканируются как чеки.

Обоснование: только `raw_tickets` содержит полный payload (`ticket.document.receipt`),
необходимый для корректного отображения в UI.

### D2. Чтение и нормализация документов: `BsonDocument` + `RawTicketDocument`

- `MongoReceiptBatchLoader` переходит с `IMongoCollection<MongoReceiptDocumentDto>`
  на `IMongoCollection<BsonDocument>`.
- Перед обработкой документ **нормализуется**: рекурсивно ключи приводятся к нижнему
  регистру; применяется alias-словарь (`nd18 → nds18` — на случай camelCase-реальности).
- Новый тип `RawTicketDocument` (обёртка над нормализованным `BsonDocument`) вместо
  `MongoReceiptDocumentDto` предоставляет типизированные аксессоры:
  - `MongoId` (`_id`), `ExternalId` (ключ `id` — UUID тикета), `Qr`, `QueryString`
    (ключ `query_string`, если появится), `Owner` (может отсутствовать → null),
    `Status`, `Seller` (`name`, `inn`).
  - `GetPayload()`: вложенный `ticket.document.receipt` ИЛИ верхнеуровневый `receipt` —
    возвращает единый объект `ReceiptPayload` или null («не fulfilled»).
  - `ReceiptPayload`: `DateTimeValue` (число int64 unix **или** строка),
    `TotalSum`, `UserInn`, `User`, `RetailPlaceAddress`, `Operator`, `Items[]`
    (`name/quantity/price/sum/nds/ndsSum/paymentType/productType`).
- `MongoReceiptDocumentDto` **удаляется** (внутренний тип слоя синхронизации;
  публичные контракты API не затрагиваются).

Обоснование: два формата (legacy-lowercase и потенциальный camelCase), вложенность
payload в двух местах, смешанный тип `datetime` — дешёвый способ не плодить
хрупкие `[BsonElement]`-привязки. Слой чтения становится единственным местом, где
формат Mongo известен.

### D3. Разрешение владельца: `raw_tickets.id` → `receipt_requests.ticket_id` → `owner`

Новый `MongoReceiptRequestLoader` (BsonDocument-коллекция `receipt_requests`) и
`ReceiptOwnerResolver`:

- Запрос: `{ ticket_id: <rawTicket.ExternalId>, deleted: { $ne: true } }`,
  предпочтение документу с непустым `owner` (`owner: { $ne: null }`,
  `owner != ObjectId("000000000000000000000000")`).
- Владелец = hex-строка `owner` ObjectId → PG-пользователь по
  `ExternalId` (существующий механизм `ResolveUserAsync`).
- Если по `ticket_id` ничего нет — пропуск документа с warn-логом
  («owner not resolved»), **без падения**.
- Электронные чеки (`getElectronic` создаёт запросы с `Owner = NilObjectID`,
  `backend/workers/electronic.go`, TODO owner needed) будут пропускаться:
  это известное ограничение backend, фиксируем в логе.
- In-memory-кэш `ConcurrentDictionary<string, string?>` (ticketId → ownerHex):
  пропускать повторные запросы в `receipt_requests` внутри цикла и между циклами.
  Размер ограничен числом чеков (тысячи) — допустим.

Rare case: один тикет у нескольких владельцев (один физический чек добавлен
двумя пользователями) — берём первого из найденных (single-operator система),
в лог — warn.

### D4. Дедупликация: естественный ключ + external_id

**Концепция.** Дубликаты могут возникнуть в трёх сценариях:

1. повторный цикл синхронизации по одному и тому же документу `raw_tickets`
   (идемпотентность);
2. один и тот же физический чек уже импортирован ранее из legacy-источника
   (2020 год): в PG он лежит с `external_id = legacy-_id`, а из `raw_tickets`
   придёт с `external_id = UUID тикета` — разные `external_id`, один чек;
3. один и тот же чек физически добавлен дважды (два документа).

**Решение.**

- `external_id` (в новой схеме = `id` тикета) обрабатывает сценарий 1
  (точный, быстрый): перед вставкой `GetByExternalIdAsync` (уже есть).
- Естественный ключ `(user_id, purchased_at, total_amount)` обрабатывает
  сценарии 2 и 3.
- PostgreSQL: **unique-индекс**
  `ux_receipts_user_purchased_total ON receipts(user_id, purchased_at, total_amount)`
  + обычный индекс `ix_receipts_user_external ON receipts(user_id, external_id)`
  (ускоряет проверку external_id; из-за разницы форматов `external_id`
  unique по нему **не** делаем).
- `IReceiptRepository` получает `GetByNaturalKeyAsync(Guid userId,
  DateTime purchasedAt, decimal totalAmount, CancellationToken)` —
  проверка перед вставкой; catch `ReceiptAlreadyExistsException`/
  `DbUpdateException` — страховка от гонки (один инстанс, риск минимален).
- Миграция перед созданием индекса **удаляет возможные дубликаты**
  (страховка): 
  `DELETE FROM receipts r USING receipts r2 WHERE r.user_id = r2.user_id AND r.purchased_at = r2.purchased_at AND r.total_amount = r2.total_amount AND r.id > r2.id;`
  (товары удалятся каскадно по FK `ON DELETE CASCADE`).

**Ключевое требование консистентности:** `purchased_at` и `total_amount` должны
вычисляться **одинаково** для одного и того же чека независимо от источника:

- `total_amount` = `totalsum/100` (decimal, 2 знака): legacy и новый формат дают
  одно и то же значение.
- `purchased_at` — см. D5 (канонический источник — параметр `t` из QR/query_string,
  локальное время с точностью до минуты; legacy-строки `datetime` имеют `:00`
  секунды, что совпадает).

### D5. Дата покупки: канонический источник — QR `t`, с fallback на `datetime`

Порядок в `GetPurchasedAt` (переезжает в `RawTicketDocument`):

1. **QR/query_string, параметр `t=YYYYMMDDTHHMM`** — локальное время покупки с
   точностью до минуты. Парсится из `qr` (raw-строка, формат совпадает с
   нормализованным `query_string`, см. `backend/nalogru/qr/query.go`). Это
   единственный источник, детерминированный для обоих форматов
   (legacy 2020: `datetime` = «2019-10-05T15:48:00» с `:00` секундами == `t`).
2. **`receipt.datetime` как число** (int64, unix-секунды) →
   `DateTimeOffset.FromUnixTimeSeconds(...).UtcDateTime`.
3. **`receipt.datetime` как строка** (legacy) → `DateTime.TryParse` (Unspecified → UTC).
4. Ничего из перечисленного нет — документ **пропускается** с понятным логом
   (edge case задачи).

Результат всегда нормализуется в UTC (как делает `ReceiptEntity.NormalizeUtc`).

**Обоснование приоритета QR.** Новая запись в PG для чеков 2020 года должна
совпасть с уже импортированной legacy-записью (тот же `purchased_at`), иначе
естественный ключ не сработает и возникнет дубликат. Unix-время из nalogru —
абсолютное (UTC), а legacy-строка — фактическое локальное время печати чека:
интерпретация unix как UTC даст сдвиг на часовой пояс и **сломает дедупликацию**.
QR-время из заголовка чека одинаково в обеих представлениях.

### D6. Периодическая синхронизация: `BackgroundService`

`ReceiptSynchronizationHostedService` меняет `IHostedService` → `BackgroundService`:

- Цикл: синхронизация сразу при старте, затем каждые `IntervalSeconds`
  (новое поле `ReceiptSynchronizationOptions.IntervalSeconds`, по умолчанию 600,
  допустимо 300–900).
- `Skip` сохраняется (при `true` — лог + выход из цикла).
- Защита от наложения: `SemaphoreSlim(1,1)` + non-blocking `Wait(0)` — если
  предыдущий цикл ещё выполняется, текущий пропускается.
- **Ошибки не валят процесс**: любые исключения (Mongo/Postgres/net) — в
  `ILogger.LogError`, цикл продолжается. Отдельно: стартовая проверка
  `CanConnectAsync` к PG переносится в тело цикла — при недоступности PG на
  старте API всё равно поднимается и ретраит с интервалом
  (критерий приёмки «сервис не должен падать целиком»).
- Graceful shutdown: `StopAsync` — отмена токена, ожидание текущего цикла.

Замечание: чтение — ресканом коллекции в каждом цикле (персональный масштаб,
допустимо). Идемпотентность обеспечивает D4.

### D7. Пагинация: keyset по `_id` вместо `skip+batchSize`

`IMongoReceiptBatchLoader.LoadBatchAsync(int skip, int batchSize)` → 
`LoadPageAsync(ObjectId afterId, int batchSize)`:

- фильтр `{ "_id": { "$gt": afterId }, "ticket": { "$ne": null } }`,
  сортировка по `_id`, `limit=batchSize`; первая страница — `afterId = ObjectId.Empty`;
- детерминированный обход (natural order в Mongo не гарантирован), страницы не
  «плывут» при параллельной вставке новых тикетов воркером;
- `LoadAllAsync` (использовался только тестами/логами) удаляется.

### D8. Маппинг и конфигурация прода

- `MongoReceiptMapper.Map`: `ExternalId = RawTicketDocument.ExternalId`
  (UUID тикета; fallback — `_id.ToString()` если `id` отсутствует);
  items/merchant/address — без изменений по сути (источником становится
  `ReceiptPayload`).
- `SynchronizeAsync`: проверка «не fulfilled» — через `GetPayload() == null`
  (сейчас строка 71 смотрит только в `document.Receipt`).
- docker-compose:
  - `Infrastructure__Receipts__Mongo__Collection=raw_tickets`;
  - `Infrastructure__Receipts__Synchronization__Skip=${ANALYTICS_SYNC_SKIP:-false}`;
  - `Infrastructure__Receipts__Synchronization__IntervalSeconds=${ANALYTICS_SYNC_INTERVAL:-600}`.
- `.env` (прод): добавить `ANALYTICS_SYNC_SKIP=false`,
  `ANALYTICS_SYNC_INTERVAL=600`.
- `.env.example`: `ANALYTICS_SYNC_SKIP=false` уже есть, добавить
  `ANALYTICS_SYNC_INTERVAL=600`.

## Что меняется (по файлам)

### Analytics (основной объём)

| Файл | Суть изменений |
|---|---|
| `.../DataSources/Mongo/MongoReceiptBatchLoader.cs` | Чтение `BsonDocument`, нормализация ключей (lowercase + alias `nd18→nds18`), keyset-пагинация `LoadPageAsync(afterId, batchSize)`, фильтр `ticket != null` |
| `.../DataSources/Mongo/RawTicketDocument.cs` *(новый)* | Обёртка над нормализованным `BsonDocument`: аксессоры `ExternalId/MongoId/Qr/Seller`; `GetPayload()` проверяет `ticket.document.receipt` и `receipt`; `ReceiptPayload` с `DateTimeValue` (число|строка), `TotalSum`, `UserInn`, `User`, `RetailPlaceAddress`, `Items[]`; `GetPurchasedAt` по D5 |
| `.../DataSources/Mongo/IMongoReceiptBatchLoader.cs` | Новое API пагинации, удаление `LoadAllAsync` (или перевод в приватное) |
| `.../DataSources/Mongo/MongoReceiptRequestLoader.cs` *(новый)* | Поиск владельца в `receipt_requests` по `ticket_id` (BsonDocument, фильтр `deleted != true`, непустой `owner`) |
| `.../DataSources/Mongo/ReceiptOwnerResolver.cs` *(новый)* | Интерфейс + реализация: резолвинг owner (кэш `ConcurrentDictionary<ticketId, ownerHex?>`), null → пропуск с логом |
| `.../DataSources/Mongo/MongoReceiptDocumentDto.cs` | Удаляется (заменяется `RawTicketDocument`) |
| `.../Synchronization/MongoReceiptMapper.cs` | Маппинг из `RawTicketDocument`; `ExternalId = id тикета`; `GetPurchasedAt` по D5 |
| `.../Synchronization/ReceiptSynchronizationService.cs` | `GetPayload()`-проверка «не fulfilled»; владелец через `ReceiptOwnerResolver`; дедупликация: `GetByExternalIdAsync` + новый `GetByNaturalKeyAsync`; catch-обработка |
| `.../Synchronization/ReceiptSynchronizationHostedService.cs` | `IHostedService` → `BackgroundService`: цикл (старт + интервал), `IntervalSeconds`, семафор от наложения, try/catch без падения, graceful stop |
| `.../Configuration/Options/ReceiptSynchronizationOptions.cs` | Добавить `IntervalSeconds` (default 600, `[Range(60, 3600)]`) |
| `.../Configuration/DependencyInjectionExtensions.cs` | Регистрация новых типов (`MongoReceiptRequestLoader`, `ReceiptOwnerResolver`); singleton-регистрации BsonDoc-cased лоадеров |
| `.../Domain/Modules/Receipts/IReceiptRepository.cs` | Добавить `GetByNaturalKeyAsync(userId, purchasedAt, totalAmount, ct)` |
| `.../Persistence/Postgres/ReceiptRepository.cs` | Реализация `GetByNaturalKeyAsync` (EF: `UserId == userId && PurchasedAt == purchasedAt && TotalAmount == totalAmount`) |

### Миграции

| Файл | Суть изменений |
|---|---|
| `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20260922xxxxxx_add_receipts_dedup_indexes.sql` *(новый)* | Удаление дубликатов (одна транзакция): `DELETE` по естественному ключу, оставляем `min(id)`; `CREATE UNIQUE INDEX ux_receipts_user_purchased_total`; `CREATE INDEX ix_receipts_user_external` |

### Конфигурация и инфраструктура

| Файл | Суть изменений |
|---|---|
| `docker-compose.yml` | `Collection=raw_tickets`; `Skip=${ANALYTICS_SYNC_SKIP:-false}`; `IntervalSeconds=${ANALYTICS_SYNC_INTERVAL:-600}` |
| `.env` | Добавить `ANALYTICS_SYNC_SKIP=false`, `ANALYTICS_SYNC_INTERVAL=600` |
| `.env.example` | Добавить `ANALYTICS_SYNC_INTERVAL=600` |
| `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` (+ `appsettings.Development.json`) | Блок `Infrastructure:Receipts:Synchronization` с `IntervalSeconds=600` (dev — можно `Skip=true` для быстрого старта) |

### Тесты

| Файл | Суть изменений |
|---|---|
| `.../Infrastructure/MongoReceiptBatchLoaderTests.cs` | Фикстуры → `BsonDocument` в формате `raw_tickets` (lowercase; два варианта регистра ключей — одна фикстура с camelCase для проверки нормализации); проверка keyset-пагинации и фильтра `ticket != null` |
| `.../Synchronization/ReceiptSynchronizationServiceTests.cs` | Переход на `RawTicketDocument`; сценарии: вложенный payload, отсутствие payload, отсутствие владельца→пропуск, дедупликация natural key (два цикла), внешний id |
| `RawTicketDocumentTests.cs` *(новый)* | Парсинг `datetime` числом/строкой; `t` из QR; alias `nd18→nds18`; payload из `ticket.document.receipt` и из верхнеуровневого `receipt` |
| `ReceiptOwnerResolverTests.cs` *(новый)* | Резолвинг по `ticket_id`; deleted-исключение; NilObjectID → null; кэш |
| `PostgresReceiptRepositoryTests.cs` | Покрытие `GetByNaturalKeyAsync` (постгрес-тестконтейнер) |

## Что НЕ меняется

- Backend (Go): `backend/` не трогаем (включая `InsertRawTicket`, `getElectronic`
  с `NilObjectID` — фиксируем как известное ограничение в логах Analytics).
- Bot, nginx, фронтенд Analytics, `ReceiptReadService` и все API-endpoints.
- `MongoUserLoader`, `MongoUserDocumentDto` (синхронизация пользователей работает).
- Категоризация товаров, AI, кэш категорий, merchants/manual edit.
- Затрагивается только внутренний слой синхронизации Analytics; схема его PG
  меняется аддитивно (новые индексы), существующие контракты API не меняются.
- Внешние зависимости (NuGet) не добавляются.

## Порядок выполнения

0. **Контроль фактического формата (обязательно, дёшево):**
   ```bash
   mongosh receipt_collection --eval "db.raw_tickets.findOne()"
   mongosh receipt_collection --eval "db.receipt_requests.findOne()"
   ```
   Сверить реальные ключи с допущениями D2 (lowercase vs camelCase) и сохранить
   снимок в тестовую фикстуру `raw_ticket_new_format.json` (в `Analytics/sample.json`
   уже есть legacy-формат). Структура решения от результата не зависит —
   нормализация покрывает оба варианта.

1. **Миграция PG** (отдельный маленький PR): скрипт dedup + уникальный индекс
   (D4). Прогнать миграцию на копии прода перед основным изменением, убедиться,
   что дубликатов нет/удаляются.
2. **Слой чтения**: `MongoReceiptBatchLoader` → `BsonDocument` + нормализация +
   keyset-пагинация; `RawTicketDocument` с аксессорами и `GetPurchasedAt` (D2, D5, D7).
   Тесты `RawTicketDocumentTests`.
3. **Владелец**: `MongoReceiptRequestLoader` + `ReceiptOwnerResolver` + кэш (D3).
4. **Сервис синхронизации**: `GetPayload()`-проверка, резолвинг владельца,
   дедупликация по natural key (D4, D8). Обновить существующие тесты.
5. **Периодичность**: `BackgroundService` c интервалом, семафором, try/catch (D6).
   Тест: N успешных циклов подряд + цикл при недоступном Mongo не убивает процесс.
6. **Репозиторий**: `GetByNaturalKeyAsync` (интерфейс + EF-реализация) + тест.
7. **Конфигурация**: `ReceiptSynchronizationOptions.IntervalSeconds`,
   docker-compose, `.env`, `.env.example`, `appsettings*.json` (D8).
8. **Полный прогон**: `cd Analytics && dotnet test`; `docker compose config` —
   контроль итоговых env Analytics.
9. **Прод-деплой и приёмка** по чек-листу задачи (в т.ч. live-проверка:
   добавить чек → дождаться цикла → увидеть в UI).

## Проверка критериев приёмки задачи

- [ ] Новый чек из `raw_tickets` появляется в UI Analytics после цикла синхронизации
      (live-проверка: добавить чек → интервал → UI).
- [ ] Учитывается вложенный payload `ticket.document.receipt` (тест `RawTicketDocument`).
- [ ] Дата, сумма, ИНН/название продавца, адрес корректны для новых чеков
      (тест-фикстура реального документа, шаг 0).
- [ ] Владелец резолвится через `receipt_requests`; если не найден — пропуск
      с warn-логом без падения (тест + live).
- [ ] Повторные циклы не создают дубликатов: новый формат (external_id) и
      cross-формат 2020 (natural key) — тесты `ReceiptSynchronizationServiceTests`.
- [ ] Синхронизация периодическая: старт + интервал 5–15 мин (настройка).
- [ ] В проде `Skip=false` (`ANALYTICS_SYNC_SKIP=false` в `.env`).
- [ ] `cd Analytics && dotnet test` зелёный.

## Риски и митигация

| Риск | Вероятность | Митигация |
|---|---|---|
| Фактические ключи в `raw_tickets` отличаются от ожидаемых (camelCase вместо lowercase) | Низкая (проверено по исходникам драйвера v1.17.9) | Шаг 0 — снимок реального документа; нормализация ключей + alias `nd18→nds18` |
| Несовпадение `purchased_at` между legacy-PG и новым импортом (часовой пояс) → дубликат 2020-чека | Низкая | Канонический источник времени — параметр `t` из QR (одинаков для обоих форматов), D5; тест на естественный ключ |
| Коллизия естественного ключа (две разные покупки: один пользователь, та же минута, та же сумма) → потеря чека | Очень низкая | Внешний id тикета ловит повторные циклы; при появлении реальных кейсов — усилить ключ фискальной тройкой (`fiscaldrive/fiscaldocumentnumber/fiscalsign`, есть в обоих форматах) |
| Дубликаты в существующих данных прода → падение миграции | Низкая | В миграции предварительный `DELETE` дубликатов с сохранением `min(id)` |
| `datetime` числового типа ломает строковый deserializer | Отсутствует после фикса | `RawTicketDocument` читает BsonValue напрямую (число/строка) |
| Периодический цикл накладывается сам на себя / падение БД роняет процесс | Средняя | `SemaphoreSlim` + non-blocking try; try/catch в цикле; тест «Mongo недоступна» |
| Электронные чеки (`owner = NilObjectID`) не импортируются | Высокая (известное ограничение backend) | Явный warn-лог «owner not resolved, skip»; фиксируется как отдельный беклог-бэкенд (TODO `electronic.go`) |
| Старый `MongoReceiptDocumentDto` используется в тестах/коде | Низкая | Удаляется вместе с обновлением всех ссылок; тесты на BsonDocument-фикстурах |
| Регрессия деплоя (забыли `.env`/compose) | Средняя | Чек-лист шага 9; `docker compose config` проверяет итоговые env |