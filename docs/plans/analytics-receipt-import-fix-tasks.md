# Детализированные задачи: Починка импорта новых чеков в Analytics (Mongo → PostgreSQL)

> Декомпозиция для senior-developer. Источники:
> - Задача: [`analytics-receipt-import-fix.md`](../tasks/analytics-receipt-import-fix.md)
> - План архитектора: [`analytics-receipt-import-fix.md`](analytics-receipt-import-fix.md) (шаги 0–9, решения D1–D8)
> - Связанные ADR: [007](../adr/007-skip-receipt-synchronization-flag.md), [012](../adr/012-analytics-production-deployment.md)
>
> Новый ADR не требуется (обоснование в плане). Все работы — только в репозитории **Analytics (.NET)** + конфигурация инфраструктуры (`docker-compose.yml`, `.env*`). Backend (Go), бот, nginx, UI, `ReceiptReadService` и API-endpoints не меняются.

## Инварианты (обязательные, из плана архитектора)

1. **Источник чеков** — коллекция `raw_tickets` (D1); `receipt_requests` используется только для резолва владельца (D3).
2. **Формат не гарантирован**: keys в Mongo могут быть lowercase (факт для драйвера v1.17.9) или camelCase → слой чтения нормализует ключи в нижний регистр + alias `nd18 → nds18` (D2).
3. **Владелец не найден / NilObjectID (электронные чеки)** → пропуск документа с warn-логом «owner not resolved», **без падения процесса** (D3; известное ограничение backend, TODO `backend/workers/electronic.go` — в беклог, не чиним здесь).
4. **Дедупликация** (D4): `external_id` (= id тикета) для идемпотентности повторных циклов + естественный ключ `(user_id, purchased_at, total_amount)` для cross-формата. Unique-индекс только по естественному ключу.
5. **`purchased_at`** вычисляется одинаково для обоих форматов (D5): канонический источник — параметр `t` из QR/`query_string`, fallback `datetime` (число unix → строка), всегда нормализация в UTC.
6. **Периодичность** (D6): `BackgroundService`, цикл при старте + интервал, `Skip` сохраняется, семафор от наложения, try/catch — ошибки не валят процесс, graceful shutdown.
7. **Пагинация** — keyset по `_id` (`$gt afterId` + sort + limit), не `skip+batchSize` (D7).
8. **Прод-конфигурация** (D8): `Collection=raw_tickets`, `Skip=false`, `IntervalSeconds=600`.
9. Внешние зависимости (NuGet) не добавляются. `ProjectDependencyTests` (слои Application/Domain/Infrastructure/Api) должны остаться зелёными.

## Открытые вопросы к архитектору (не блокируют начало работ)

1. **Диапазон `IntervalSeconds`**: D6 говорит «допустимо 300–900», таблица «По файлам» — `[Range(60, 3600)]`. Согласовать перед задачей 5 (значения 600 в окружении использовать в обоих случаях).
2. **Формат `raw_tickets.id` для legacy-чеков 2020**: в `sample.json` `receipt_requests.ticket_id` — hex-строка ObjectId (`5dcad9be…`), для новых чеков `id` — UUID-строка. Подтвердить на шаге 0 (задача 0), что `raw_tickets.id` совпадает со значением `receipt_requests.ticket_id` — это критично для D3. Для cross-формата разных внешних id дедупликацию обеспечивает natural key (D4).
3. **Владелец найден, но отсутствует в PG `users`**: сейчас `ResolveUserAsync` авто-создаёт `<Unknown user>`. Подтвердить, что это поведение сохраняется при переходе на `ReceiptOwnerResolver`.

---

## Задача 0. Диагностика: снимок реальных документов из прода → фикстура (шаг 0 плана)

**Приоритет:** P0 · **Оценка:** 0.5 дня · **Зависимости:** нет

**Файлы:** нет изменений кода. Результат — фикстура `Analytics/raw_ticket_new_format.json` (рядом с существующей legacy-фикстурой `Analytics/sample.json`).

**Объём работ:**

1. Выполнить в проде (с учётом auth, например `mongosh "mongodb://${MONGO_LOGIN}:${MONGO_SECRET}@<host>:27017/receipt_collection?authSource=admin"`):
   - `db.raw_tickets.findOne()` — снять полный документ нового формата;
   - `db.receipt_requests.findOne()` — снять документ для понимания связки `ticket_id`/`owner`/`deleted`.
2. Сверить фактические ключи с допущениями D2: регистр ключей (lowercase vs camelCase), тип `receipt.datetime` (int64 unix vs строка), наличие `id` (тип: UUID-строка / hex-строка ObjectId / ObjectId), `qr`, `query_string`, вложенность payload (`ticket.document.receipt` vs верхнеуровневый `receipt`).
3. Проверить, что значение `raw_tickets.id` совпадает со значением `receipt_requests.ticket_id` (вопрос №2) — на ОДНОМ физическом чеке.
4. Сохранить снимок `raw_tickets` в фикстуру `raw_ticket_new_format.json`, **обезличив персональные данные** (ФИО, адреса, ИНН пользователя и т.п.), сохранив структуру, типы значений и взаимосвязи. Legacy-формат уже есть в `Analytics/sample.json`.
5. Зафиксировать выводы (регистр ключей, тип datetime, формат id, наличие query_string) комментарием в фикстуре или в разделе файла плана.

**Критерий готовности:** фикстура `raw_ticket_new_format.json` в репозитории; сводка фактического формата зафиксирована; подтверждено (или опровергнуто с корректировкой задач 2.1/3) соответствие допущениям D2/D3.

---

## Задача 1. Миграция PG: дедупликационные индексы (шаг 1 плана; отдельный маленький PR)

**Приоритет:** P0 · **Оценка:** 0.5–1 день · **Зависимости:** нет (может идти первой) · **Параллельно:** задачи 0, 2.x

**Файлы:** новый `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20260923XXXXXX_add_receipts_dedup_indexes.sql` (timestamp строго позже существующих скриптов, например `20260923000000`).

**Объём работ** (D4; текст SQL — из плана):

- Предварительное удаление дубликатов по естественному ключу, сохраняя `min(id)` (товары удалятся каскадно по FK `ON DELETE CASCADE` таблицы `commodities`):
  ```sql
  DELETE FROM receipts r USING receipts r2
  WHERE r.user_id = r2.user_id
    AND r.purchased_at = r2.purchased_at
    AND r.total_amount = r2.total_amount
    AND r.id > r2.id;
  ```
- `CREATE UNIQUE INDEX ux_receipts_user_purchased_total ON receipts (user_id, purchased_at, total_amount);`
- `CREATE INDEX ix_receipts_user_external ON receipts (user_id, external_id);` (unique по external_id **не** делаем — разный формат значений между legacy и новым форматом, D4).
- `MigrationRunner` сам оборачивает каждый скрипт в транзакцию (см. существующие скрипты) — `BEGIN/COMMIT` в файле не нужны.
- Прогнать миграцию на **копии прода** (обязательно, до основного изменения) и убедиться, что дубликатов нет или они корректно удаляются.

**Критерий готовности:** скрипт применён локально (`cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run`) и на копии прода без ошибок; `\d receipts` показывает оба индекса; запрос-проверка «дубликатов по natural key больше 1» возвращает 0 строк; объём удалённых строк зафиксирован в логах PR.

---

## Задача 2.1. Слой чтения: `RawTicketDocument` + `GetPurchasedAt` + юнит-тесты (шаг 2, часть 1)

**Приоритет:** P0 · **Оценка:** 1 день · **Зависимости:** задача 0 (формат фикстуры)

**Файлы:**
- новый `Analytics/src/ReceiptCollector.Analytics.Infrastructure/DataSources/Mongo/RawTicketDocument.cs`;
- новый `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/RawTicketDocumentTests.cs`.

**Объём работ** (D2, D5; тип аддитивный, никто не потребляет — сборка остаётся зелёной):

- Обёртка над нормализованным `BsonDocument` (нормализация — задача 2.2; здесь опираемся на lowercase-доступ к ключам):
  - аксессоры: `MongoId` (`_id`), `ExternalId` (ключ `id` — тикет; отсутствует → null), `Qr`, `QueryString` (`query_string`, если есть), `Owner` (может отсутствовать → null), `Status`, `Seller` (`name`, `inn`);
  - `GetPayload()`: вложенный `ticket.document.receipt` **ИЛИ** верхнеуровневый `receipt` → единый `ReceiptPayload` или null («не fulfilled»);
  - `ReceiptPayload`: `DateTimeValue` (BsonValue: int64 unix **или** строка), `TotalSum`, `UserInn`, `User`, `RetailPlaceAddress`, `Operator`, `Items[]` (`name/quantity/price/sum/nds/ndssum/paymenttype/producttype`); чтение напрямую из `BsonValue` (число/строка) — без строготипизированного deserializer (защита от mixed-типа `datetime`);
  - alias-словарь `nd18 → nds18` учитывается при чтении сумм НДС (на случай camelCase-реальности).
- `GetPurchasedAt()` по D5, строго в таком порядке:
  1. параметр `t=YYYYMMDDTHHMM` из QR/`query_string` (локальное время, одинакова для обоих форматов; парсинг по образцу `backend/nalogru/qr/query.go`);
  2. `receipt.datetime` как число (int64 unix) → `DateTimeOffset.FromUnixTimeSeconds(...).UtcDateTime`;
  3. `receipt.datetime` как строка → `DateTime.TryParse`, Unspecified → UTC;
  4. ничего не найдено → null (документ будет пропущен сервисом с логом, задача 4).
  - Результат всегда нормализуется в UTC (по образцу `ReceiptEntity.NormalizeUtc`).

**Правки тестов:** новый `RawTicketDocumentTests` — сценарии из плана: `datetime` числом; `datetime` строкой; время из `t` в QR и в нормализованном `query_string`; payload из `ticket.document.receipt`; payload из верхнеуровневого `receipt`; отсутствие payload → null; alias `nd18→nds18`; `ExternalId` fallback.

**Критерий готовности:** `cd Analytics && dotnet build && dotnet test --filter "FullyQualifiedName~RawTicketDocumentTests"` — зелёные; сборка Infrastructure компилируется (тип пока не используется в основном коде).

---

## Задача 2.2. Слой чтения: BsonDocument-лоадер + нормализация + keyset-пагинация, переключение потребителей, удаление DTO (шаг 2, часть 2)

**Приоритет:** P0 · **Оценка:** 1–1.5 дня · **Зависимости:** задача 2.1

**Файлы:**
- правка `MongoReceiptBatchLoader.cs`;
- правка `IMongoReceiptBatchLoader.cs`;
- правка `ReceiptSynchronizationService.cs` (только механическая часть, см. ниже);
- правка `MongoReceiptMapper.cs` (только механическая часть);
- удаление `MongoReceiptDocumentDto.cs`;
- правка тестов `MongoReceiptBatchLoaderTests.cs` и (механически) `ReceiptSynchronizationServiceTests.cs`.

**Объём работ:**

1. `MongoReceiptBatchLoader`: переход с `IMongoCollection<MongoReceiptDocumentDto>` на `IMongoCollection<BsonDocument>`; **нормализация** каждого документа перед обработкой (рекурсивное приведение ключей к нижнему регистру + alias `nd18 → nds18`); обёртка в `RawTicketDocument`.
2. `IMongoReceiptBatchLoader`: новый метод `LoadPageAsync(ObjectId afterId, int batchSize)`; удалить `LoadBatchAsync` и `LoadAllAsync` (использовался только тестами/логами, D7).
3. Keyset-пагинация (D7): фильтр `{ "_id": { "$gt": afterId }, "ticket": { "$ne": null } }`, сортировка по `_id` asc, `limit = batchSize`; первая страница — `ObjectId.Empty`. (В Mongo `$ne: null` так же отбрасывает документы без ключа — корректно для «не fulfilled».)
4. `ReceiptSynchronizationService` — механическое переключение (поведение — задачи 3/4):
   - цикл: страница через `LoadPageAsync`, `afterId = последний MongoId из страницы` (вместо `skip += count`), выход при пустой странице;
   - проверка «не fulfilled»: `document.Receipt == null` → `document.GetPayload() == null` (D8);
   - idempotency-проверка `GetByExternalIdAsync` переводится на новый ExternalId = id тикета (значение, которое будет сохранено в PG).
5. `MongoReceiptMapper` — механическое переключение на `RawTicketDocument`/`ReceiptPayload` (D8): `ExternalId = RawTicketDocument.ExternalId` (id тикета; fallback — `_id.ToString()` если `id` отсутствует); детерминированный `receiptId` — из того же id; items/merchant/address — без изменений по сути, источником становится `ReceiptPayload`; `GetPurchasedAt` переезжает в `RawTicketDocument` (D5).
6. Удалить `MongoReceiptDocumentDto` **вместе** с обновлением всех ссылок (риск из плана — иначе сборка красная): тесты и код не должны его упоминать.

**Правки тестов:**

- `MongoReceiptBatchLoaderTests`: фикстуры → `BsonDocument` в формате `raw_tickets` (lowercase; **одна фикстура с camelCase** — проверка нормализации); удалить тест `LoadAllAsync`; добавить проверки keyset-пагинации (несколько страниц, `afterId`, детерминированный порядок) и фильтра `ticket != null` (документы с `ticket: null` и без ключа не возвращаются); проверка alias `nd18→nds18`.
- `ReceiptSynchronizationServiceTests`: механический перевод seed-данных с DTO на `BsonDocument` в raw_tickets-формате (та же структура); семантические сценарии — задача 4.

**Критерий готовности:** `cd Analytics && dotnet build && dotnet test` — зелёные (все проекты); `grep -rn "MongoReceiptDocumentDto" Analytics/src Analytics/tests` → 0 совпадений; `LoadBatchAsync`/`LoadAllAsync` отсутствуют в интерфейсе и коде.

---

## Задача 3. Владелец: `MongoReceiptRequestLoader` + `ReceiptOwnerResolver` + кэш + интеграция (шаг 3 плана)

**Приоритет:** P0 · **Оценка:** 1–1.5 дня · **Зависимости:** 2.2, 0

**Файлы:**
- новый `MongoReceiptRequestLoader.cs` (DataSources/Mongo);
- новый `ReceiptOwnerResolver.cs` (DataSources/Mongo; интерфейс + реализация);
- правка `DependencyInjectionExtensions.cs` (регистрация новых типов, singleton);
- правка `ReceiptSynchronizationService.cs` (резолвинг владельца через resolver в цикле);
- новый тест `ReceiptOwnerResolverTests.cs`;
- правка `ReceiptSynchronizationServiceTests.cs` (инъекция resolver; сценарий «владелец не найден → пропуск»).

**Объём работ** (D3):

1. `MongoReceiptRequestLoader` — BsonDocument-коллекция `receipt_requests`:
   - запрос по `ticket_id: <rawTicket.ExternalId>` с фильтром `deleted: { $ne: true }`;
   - предпочтение документу с непустым `owner` (`owner: { $ne: null }` и `owner != ObjectId("000000000000000000000000")` / `NilObjectID`);
   - возврат hex-строки `owner`.
2. `ReceiptOwnerResolver` (интерфейс + реализация):
   - вход — `RawTicketDocument`, выход — `ownerHex: string?`;
   - in-memory кэш `ConcurrentDictionary<string, string?>` (ticketId → ownerHex), негативное кэширование null допустимо (план: масштаб тысячи, между циклами);
   - NilObjectID / невалидный `owner` → null (электронные чеки, известное ограничение backend);
   - rare case «один тикет у нескольких владельцев» → первый из найденных + warn-лог;
   - результат null → пропуск документа в сервисе с warn-логом **«owner not resolved, skip»**, без исключений.
3. `ReceiptSynchronizationService`: заменить путь `document.Owner` → `ResolveUserAsync` на `ownerResolver.ResolveAsync(document)` → `GetByExternalIdAsync(ownerHex)` (существующий механизм). Поведение авто-создания `<Unknown user>` при отсутствии пользователя в PG — по ответу на открытый вопрос №3.
4. DI: регистрация `MongoReceiptRequestLoader` (singleton), `ReceiptOwnerResolver` (singleton — кэш переживает циклы).

**Правки тестов:**

- `ReceiptOwnerResolverTests` (testcontainers Mongo): резолвинг по `ticket_id`; `deleted: true` исключается; NilObjectID → null; пустой/отсутствующий `owner` → null; кэш (повторный вызов не делает второй запрос); rare case — первый владелец.
- `ReceiptSynchronizationServiceTests`: конструктор сервиса с resolver; сценарий «нет записи в receipt_requests → документ пропущен, лог warn, сервис жив»; сценарий «электронный чек (NilObjectID) → пропуск с warn».

**Критерий готовности:** `cd Analytics && dotnet build && dotnet test --filter "FullyQualifiedName~ReceiptOwnerResolverTests|FullyQualifiedName~ReceiptSynchronizationServiceTests"` — зелёные; при отсутствии владельца в логах появляется warn «owner not resolved», процесс не падает.

---

## Задача 4. Синхронизация: дедупликация по natural key + external_id, маппинг, семантические тесты (шаг 4 плана)

**Приоритет:** P0 · **Оценка:** 1–1.5 дня · **Зависимости:** 2.2, 3, **6** (см. сводную таблицу: задача 6 выполняется до завершения этой задачи)

**Файлы:**
- правка `ReceiptSynchronizationService.cs`;
- правка `MongoReceiptMapper.cs` (окончательная доводка D8, если осталась после 2.2);
- правка `ReceiptSynchronizationServiceTests.cs` (семантические сценарии).

**Объём работ** (D4, D8):

1. Дедупликация в цикле перед вставкой, в порядке:
   - `GetByExternalIdAsync(newExternalId, user.Id, ct)` — идемпотентность повторных циклов (сценарий 1);
   - `GetByNaturalKeyAsync(user.Id, purchasedAt, totalAmount, ct)` — cross-формат 2020 и «чек добавлен дважды» (сценарии 2–3);
   - catch `ReceiptAlreadyExistsException` / `DbUpdateException` — страховка от гонки (один инстанс, риск минимален; catch уже присутствует — расширить).
2. `MongoReceiptMapper` (если не полностью закрыто в 2.2): `ExternalId = RawTicketDocument.ExternalId` (id тикета, fallback `_id.ToString()`); `purchasedAt` — через `RawTicketDocument.GetPurchasedAt()`; `totalAmount = totalsum/100` (decimal, 2 знака) — одинаково для обоих форматов (требование консистентности natural key из D4).
3. Проверка «не fulfilled» через `GetPayload() == null` — уже в 2.2; здесь убедиться, что путь пропуска логируется информативно (лог с `MongoId`, а не падение).

**Правки тестов** — `ReceiptSynchronizationServiceTests` (семантические сценарии по плану):
- вложенный payload `ticket.document.receipt` импортируется;
- отсутствие payload → пропуск;
- отсутствие владельца → пропуск без падения (сценарий из задачи 3);
- **два цикла синхронизации** по одному документу → один чек в PG (external_id);
- **cross-формат 2020**: два документа с разными external_id (legacy `_id` vs UUID тикета), но одинаковым natural key → один чек в PG;
- один и тот же физический чек добавлен дважды (два raw-документа) → один импорт;
- дата/сумма/продавец/адрес корректны для фикстуры реального документа (задача 0).

**Критерий готовности:** `dotnet test --filter "FullyQualifiedName~ReceiptSynchronizationServiceTests"` — зелёные; сценарии дедупликации (два цикла, cross-формат) покрыты тестами; `purchased_at`/`total_amount` считаются одинаково для обоих форматов.

---

## Задача 5. Периодичность: `BackgroundService` с интервалом, семафором и защитой от падений (шаг 5 плана)

**Приоритет:** P0 · **Оценка:** 1 день · **Зависимости:** задача 4

**Файлы:**
- правка `ReceiptSynchronizationHostedService.cs`;
- правка `ReceiptSynchronizationOptions.cs` (поле `IntervalSeconds`);
- новый тест `ReceiptSynchronizationHostedServiceTests.cs`.

**Объём работ** (D6):

1. `ReceiptSynchronizationHostedService`: `IHostedService` → `BackgroundService`:
   - цикл: синхронизация сразу при старте, затем каждые `IntervalSeconds`; `Skip=true` → лог + выход из цикла;
   - защита от наложения: `SemaphoreSlim(1,1)` + non-blocking `Wait(0)` — если предыдущий цикл выполняется, текущий пропускается;
   - **ошибки не валят процесс**: try/catch вокруг тела цикла → `ILogger.LogError`, цикл продолжается (Mongo/Postgres/сеть);
   - стартовая проверка `CanConnectAsync` к PG переносится в тело цикла — при недоступной PG на старте API всё равно поднимается и ретраит с интервалом (критерий «сервис не падает целиком»);
   - graceful shutdown: `StopAsync` → отмена токена + ожидание текущей итерации.
2. `ReceiptSynchronizationOptions`: `IntervalSeconds` (default 600; диапазон — по ответу на открытый вопрос №1).

**Правки тестов** — новый `ReceiptSynchronizationHostedServiceTests`: N успешных циклов подряд (два-три повторных вызова, каждый попадает в `SynchronizeAsync`); цикл при недоступном Mongo (закрытый/неверный порт) → `LogError`, процесс жив, следующий цикл успешен.

**Критерий готовности:** `dotnet test --filter "FullyQualifiedName~ReceiptSynchronizationHostedServiceTests"` — зелёные; ручной smoke: старт API без PG → сервис поднялся, в логах ошибки подключения, ретраи; с недоступным Mongo процесс не падает.

---

## Задача 6. Репозиторий: `GetByNaturalKeyAsync` (шаг 6 плана)

**Приоритет:** P0 · **Оценка:** 0.5–1 день · **Зависимости:** нет (аддитивно) · **Параллельно:** 2.x, 3 · **Важно:** должна быть завершена до задачи 4 (см. сводную таблицу)

**Файлы:**
- правка `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Receipts/IReceiptRepository.cs`;
- правка `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/ReceiptRepository.cs`;
- правка `PostgresReceiptRepositoryTests.cs`.

**Объём работ** (D4):

1. Интерфейс: `Task<Receipt?> GetByNaturalKeyAsync(Guid userId, DateTime purchasedAt, decimal totalAmount, CancellationToken cancellationToken)`.
2. EF-реализация по образцу `GetByExternalIdAsync` (`AsNoTracking().Include(r => r.Items)`, `SetCurrentUser`/`ClearCurrentUser` соблюдать как в остальных методах): `r.UserId == userId && r.PurchasedAt == purchasedAt && r.TotalAmount == totalAmount`.
3. Тест `PostgresReceiptRepositoryTests` (testcontainers Postgres): найдено; не найдено; другой `userId` → null; граничный случай совпадения по двум полям из трёх → null.

**Критерий готовности:** `dotnet test --filter "FullyQualifiedName~PostgresReceiptRepositoryTests"` — зелёные; метод доступен для задачи 4.

---

## Задача 7. Конфигурация: опции и окружение (шаг 7 плана; D8)

**Приоритет:** P0 · **Оценка:** 0.5–1 день · **Зависимости:** 5 (поле `IntervalSeconds`), 2.2 (`Collection=raw_tickets`)

**Файлы:**
- правка `docker-compose.yml`;
- правка `.env`;
- правка `.env.example`;
- правка `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`;
- правка `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json`.

**Объём работ** (D8):

1. `docker-compose.yml` (сервис `analytics`):
   - `Infrastructure__Receipts__Mongo__Collection=raw_tickets` (вместо `receipt_requests`);
   - `Infrastructure__Receipts__Synchronization__Skip=${ANALYTICS_SYNC_SKIP:-false}` (вместо `:-true`);
   - добавить `Infrastructure__Receipts__Synchronization__IntervalSeconds=${ANALYTICS_SYNC_INTERVAL:-600}`.
2. `.env` (прод): добавить `ANALYTICS_SYNC_SKIP=false`, `ANALYTICS_SYNC_INTERVAL=600`.
3. `.env.example`: добавить `ANALYTICS_SYNC_INTERVAL=600` (раздел Analytics; `ANALYTICS_SYNC_SKIP=false` уже есть — оставить).
4. `appsettings.json` (prod): добавить блок `Infrastructure:Receipts:Synchronization` c `IntervalSeconds: 600`.
5. `appsettings.Development.json`: `Collection` → `raw_tickets` (D1 применяется ко всем средам; `Skip: true` для быстрого старта dev можно сохранить); добавить `IntervalSeconds: 600`.

**Критерий готовности:** `docker compose config` показывает для сервиса `analytics` итоговые env `Collection=raw_tickets`, `Skip=false`, `IntervalSeconds=600`; локальный запуск (`dotnet run` в `Api` с dev-конфигом) читает коллекцию `raw_tickets`, а не `receipt_requests`.

---

## Задача 8. Полный прогон и сверка с критериями приёмки (шаг 8 плана)

**Приоритет:** P1 · **Оценка:** 0.5 дня · **Зависимости:** все предыдущие (1–7)

**Файлы:** без изменений кода (при необходимости — мелкие правки по результатам прогона).

**Объём работ:**

1. `cd Analytics && dotnet build && dotnet test` — все тесты зелёные, включая `ProjectDependencyTests` и не менявшиеся наборы (регрессия: категоризация, AI, кэш категорий, merchants, users).
2. `docker compose config` — контроль итоговых env Analytics (пересекается с задачей 7; здесь — финальная сверка).
3. Пройти чек-лист «Проверка критериев приёмки задачи» из плана (кроме live-пунктов, перенесённых в задачу 9): вложенный payload, дата/сумма/продавец/адрес, владелец/пропуск, отсутствие дубликатов (новый формат и cross-формат), периодичность, `Skip=false` в проде-конфиге, зелёный `dotnet test`.

**Критерий готовности:** автотесты зелёные; чек-лист плана отмечен (кроме live); различий конфигурации с заданием D8 нет.

---

## Задача 9. Прод-деплой и live-приёмка (шаг 9 плана)

**Приоритет:** P1 · **Оценка:** 0.5–1 день (по процессу) · **Зависимости:** 8, 1, 7

**Файлы:** без изменений кода; работа с окружением прода.

**Объём работ:**

1. Деплой в порядке: миграция (задача 1) → обновление `.env` (`ANALYTICS_SYNC_SKIP=false`, `ANALYTICS_SYNC_INTERVAL=600`) → `./build.sh` + `./up.sh` (или штатный процесс деплоя).
2. Live-проверка по критериям приёмки задачи:
   - добавить чек → дождаться цикла синхронизации → чек виден в UI Analytics с корректной датой, суммой, продавцом и владельцем;
   - повторный цикл (дождаться ещё одного интервала) → дубликатов нет;
   - в логах нет «owner not resolved» для QR-чеков; для электронных чеков — warn «owner not resolved, skip» (известное ограничение backend, беклог: TODO `backend/workers/electronic.go`);
   - история 2020 (если есть в `raw_tickets`) импортируется один раз без задвоения (контроль: количество чеков до/после первого полного прохода).
3. Зафиксировать результат в задаче (`docs/tasks/analytics-receipt-import-fix.md` — отметить критерии приёмки).

**Критерий готовности:** все live-критерии приёмки задачи выполнены; дубликатов нет; процесс стабилен после нескольких циклов.

---

## Сводная таблица: зависимости, приоритеты, оценки, порядок выполнения

| № | Задача | Шаги плана | Приоритет | Оценка | Зависит от | Можно параллелить с |
|---|---|---|---|---|---|---|
| 0 | Снимок формата → фикстура | 0 | P0 | 0.5 | — | 1 |
| 1 | Миграция PG (индексы) | 1 | P0 | 0.5–1 | — | 0, 2.x, 6 |
| 2.1 | `RawTicketDocument` + `GetPurchasedAt` + тесты | 2 | P0 | 1 | 0 | 1, 6 |
| 2.2 | BsonDocument-лоадер, нормализация, keyset, удаление DTO | 2 | P0 | 1–1.5 | 2.1 | 1, 3 (после 2.1), 6 |
| 3 | Владелец: loader + resolver + кэш + интеграция | 3 | P0 | 1–1.5 | 2.2, 0 | 6 |
| 6¹ | Репозиторий: `GetByNaturalKeyAsync` + тест | 6 | P0 | 0.5–1 | — | 2.x, 3 |
| 4 | Синхронизация: dedup (external_id + natural key), маппинг, семантика-тесты | 4 | P0 | 1–1.5 | 2.2, 3, **6** | 5 (после) |
| 5 | Периодичность: `BackgroundService` + опция `IntervalSeconds` | 5 | P0 | 1 | 4 | 7 (после) |
| 7 | Конфигурация: compose, `.env*`, `appsettings*` | 7 | P0 | 0.5–1 | 5, 2.2 | 8 |
| 8 | Полный прогон + чек-лист приёмки | 8 | P1 | 0.5 | 1–7 | 9 |
| 9 | Прод-деплой + live-приёмка | 9 | P1 | 0.5–1 | 8, 1, 7 | — |

¹ Нумерация сохранена по плану архитектора (шаг 6), но **по зависимостям задача 6 выполняется до завершения задачи 4** — задача 4 (дедупликация через `GetByNaturalKeyAsync`) не компилируется без неё.

**Критический путь:** 0 → 2.1 → 2.2 → 3 → 4 → 5 → 7 → 8 → 9 (задача 6 вклинивается между 3 и 4; задача 1 идёт параллельно с 0–2.x и применяется до основного деплоя).

**Рекомендуемый порядок (расписание разработчику):**

1. **Задача 0 + Задача 1** (параллельно; 1 — отдельный маленький PR, можно сразу в ревью).
2. **Задача 2.1** → **Задача 2.2** (одним PR «слой чтения» допустимо, если 2.1+2.2 укладывается в 2 дня).
3. **Задача 3** и **Задача 6** (параллельно).
4. **Задача 4** (после 3 и 6).
5. **Задача 5** → **Задача 7**.
6. **Задача 8** (прогон) → **Задача 9** (деплой, по процессу).

**Правила безопасности:**

- Каждая задача оставляет `cd Analytics && dotnet build && dotnet test` зелёным. Исключения-однодневки: 2.1 (аддитивно), 6 (аддитивно), 7 (конфиг).
- Не трогать: Backend (Go), бот, nginx, фронтенд, `ReceiptReadService`, API-endpoints, `MongoUserLoader`/`MongoUserDocumentDto`, категоризацию/AI/кэш категорий.
- Известное ограничение (не чинить в этой задаче): электронные чеки (`owner = NilObjectID`) не импортируются — фиксируется warn-логом; беклог-bаckend — TODO `backend/workers/electronic.go`.
- Перед деплоем (задача 9) снять бэкап PG (`pg_dump`) — миграция деструктивна для дубликатов (задача 1).

## Связь с документами

- Задача: [`docs/tasks/analytics-receipt-import-fix.md`](../tasks/analytics-receipt-import-fix.md)
- План архитектора: [`docs/plans/analytics-receipt-import-fix.md`](analytics-receipt-import-fix.md)
- ADR: [007](../adr/007-skip-receipt-synchronization-flag.md), [012](../adr/012-analytics-production-deployment.md)