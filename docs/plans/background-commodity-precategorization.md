# План реализации: фоновая предкатегоризация товаров в простое + админский раздел

> Декомпозиция утверждённой задачи для @senior-developer. Источники:
> [задача background-commodity-precategorization](../tasks/background-commodity-precategorization.md)
> (C1–C5, D1–D14, FR-1…FR-9, NFR-1…NFR-9),
> [ADR-027](../adr/027-background-commodity-precategorization.md) — основной документ реализации,
> а также ADR 009 (amend: `existing → cache → precategorized → ai`),
> ADR 019 (кэш, `TryAdd`, «замерзание»), ADR 023 (`routes.ts`), ADR 021 (env в docker-compose).
>
> **Новых архитектурных решений план не содержит.** Всё ниже — только порядок работ, файлы и
> проверки. Если реализация упирается в противоречие требований — эскалация к @software-architect,
> а не самостоятельное решение (см. «Замечания для реализации» в конце).

## 0. Инварианты (из ADR-027 — обязательны, не обсуждаются)

1. **Простой глобальный**, определяется по последней HTTP-активности; исключён только `/health`
   (C1, D3, FR-1). Middleware активности регистрируется **до `UseStaticFiles()`**.
2. **Данные всех пользователей** — только в новых методах, явно `IgnoreQueryFilters()` на
   `ReceiptDbContext.Receipts` **с комментарием** «доступ вне owner-scoping намеренный (ADR-027, D8)».
   Существующие owner-scoped запросы (`ReceiptRepository`, `CommodityReadService`) не меняются (NFR-5).
3. **Индексы только на `precategorized_commodities`**: unique `(name, merchant_id)` + `(status, created_at)`.
   **Индексы на `commodities` НЕ создаются** (NFR-6, риск 6).
4. **Подтверждение — одна транзакция** (D10, FR-6.5): `commodities` всех пользователей по точному
   `name`, только `category_id IS NULL OR = 0`; строка → `applied`; удаление «сестринских» `proposed`;
   `ICommodityCategoryCache.TryAdd` — **после фиксации** транзакции.
5. **Отклонение = физическое удаление** строки, `commodities` не меняются (C4).
6. **Курсор сессии (D14)** — по последнему обработанному кандидату, включая `Undefined`/ошибки ИИ;
   хранится в памяти hosted-сервиса, сбрасывается при рестарте.
7. **Обработка строго последовательная**, пауза `IntervalBetweenCalls` — только после **вызова ИИ**
   (D4, FR-2.2/2.4); остановка — после текущей позиции (FR-2.5).
8. **`source = "precategorized"`** — единственное изменение контракта suggest, аддитивное (FR-9.4,
   NFR-8). `suggest` остаётся read-only (FR-9.5), очередь читается **одним batch-запросом** (FR-9.3),
   ошибка чтения очереди → Warning + деградация к ИИ (FR-9.6), кэш из очереди **не наполняется**.
9. **Время внедряемое** (`TimeProvider`/`FakeTimeProvider`) — создаётся в этой задаче (NFR-9);
   `Task.Delay(delay, timeProvider, ct)`, без реальных задержек в тестах.
10. **Unique-конфликт `(name, merchant_id)`** (FR-4.5) — не валит сессию (upsert/`ON CONFLICT DO
    NOTHING` либо `DbUpdateException` с `SqlState = 23505` → no-op).
11. **Вне объёма**: Go-сервисы, бот, nginx, MongoDB-схема, ProblemDetails (стиль ошибок — текстовый,
    эталон `MerchantEndpoints`), пакетные операции, ретенция `applied`.

## 1. Карта изменений по слоям

| Слой / файл | Действие | Шаг |
|---|---|---|
| `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261023120000_add_precategorized_commodities.sql` | новый SQL-скрипт | 1 |
| `Domain/Modules/Commodities/PrecategorizedCommodityStatus.cs` | новый enum `Proposed = 0, Applied = 1` | 1 |
| `Infrastructure/Persistence/Postgres/PrecategorizedCommodityEntity.cs` | новая EF-сущность | 1 |
| `Infrastructure/Persistence/Postgres/Configurations/PrecategorizedCommodityConfiguration.cs` | конфигурация + индексы + FK `CASCADE` | 1 |
| `Infrastructure/Persistence/Postgres/ReceiptDbContext.cs` | `DbSet` + `ApplyConfiguration`; **query-filter на новую сущность не ставить** | 1 |
| `Domain/Modules/Commodities/PrecategorizedCommodity.cs` | доменная модель | 2 |
| `Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs` | контракт репозитория | 2 |
| `Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs` | реализация (глобальные запросы, транзакция confirm) | 2 |
| `Infrastructure/Configuration/Options/PrecategorizationOptions.cs` | новая опция + валидация | 3 |
| `Infrastructure/Configuration/DependencyInjectionExtensions.cs` | биндинг опций, `TimeProvider`, монитор, репозиторий, сервисы, hosted service | 3, 4, 5, 6 |
| `Application/Modules/Commodities/Contracts/IApiActivityMonitor.cs` | контракт монитора | 4 |
| `Infrastructure/Modules/Commodities/ApiActivityMonitor.cs` | Singleton-реализация | 4 |
| `Api/Middleware/RequestActivityMiddleware.cs` | middleware активности | 4 |
| `Api/Program.cs` | middleware до `UseStaticFiles()`; `MapPrecategorizedCommodityEndpoints()` | 4, 8 |
| `Infrastructure/Modules/Commodities/PrecategorizationBackgroundService.cs` | фоновый hosted service | 5 |
| `Application/Modules/Commodities/Contracts/IPrecategorizedCommodityService.cs`, `Models/PrecategorizedCommodityModels.cs` | контракт + DTO | 6 |
| `Infrastructure/Modules/Commodities/PrecategorizedCommodityService.cs` | админская бизнес-логика | 6 |
| `Application/Modules/Commodities/Models/CategorizationModels.cs` | `CategorizationSource.Precategorized` + wire-имя | 7 |
| `Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` | шаг очереди между cache и ai | 7 |
| `Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs` | 4 эндпоинта | 8 |
| `Analytics/frontend/src/**` | типы, api, хук, страница, таблица, маршрут, навигация, note в `ReceiptDetails` | 10 |
| `Api/appsettings.json`, `docker-compose.yml`, `.env.example`, `AGENTS.md` | конфигурация и документация | 11 |
| `tests/ReceiptCollector.Analytics.Api.Tests/**` | тесты по шагам | 2–9 |

## 2. Порядок, зависимости, параллельность

```
1 ──► 2 ──┬──► 6 ──► 8 ──► 10
          ├──► 7 ──┐
          └──► 5 ──┤
3 ──► 4 ──► 5     ├──► 9
3 ────────────────┴──► 11
```

| Шаг | Зависит от | Блокирует | Можно параллельно с | Оценка |
|---|---|---|---|---|
| 1. Миграция + EF | — | 2 | 3 | 0.5–1 д |
| 2. Домен + репозиторий | 1 | 5, 6, 7 | 3 | 1.5–2 д |
| 3. Опции + `TimeProvider` | — | 4, 5, 11 | 1, 2 | 0.5 д |
| 4. Монитор активности + middleware | 3 | 5 | 2, 6, 7 | 0.5–1 д |
| 5. Фоновый сервис | 2, 3, 4 | 9 | 6, 7 | 2 д |
| 6. Админский сервис | 2 | 8, 9 | 4, 5, 7 | 1.5 д |
| 7. FR-9 (suggest + очередь) | 2 | 9 | 4, 5, 6 | 1 д |
| 8. Админский API | 6 | 9, 10 | 5, 7 | 1 д |
| 9. Сквозные тесты + архитектура | 5, 6, 7, 8 | — | 10, 11 | 1–1.5 д |
| 10. Frontend | 8 | — | 9, 11 | 1.5–2 д |
| 11. Конфиги и документация | 3 | — | 9, 10 | 0.5 д |

**Критический путь:** 1 → 2 → 6 → 8 → 9 (≈ 6.5–7.5 д). Двумя разработчиками параллельно:
backend-ветка (1, 2, 6, 7, 8, 9) и вторая ветка (3, 4, 5) → затем frontend (10) и конфиги (11).

---

## Шаг 1. Миграция и EF-маппинг таблицы `precategorized_commodities`

**Цель:** схема БД и EF-модель очереди предложений, синхронные между собой.

**Проекты/файлы:**
- `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261023120000_add_precategorized_commodities.sql` (новый);
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/PrecategorizedCommodityStatus.cs` (новый enum);
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/PrecategorizedCommodityEntity.cs` (новый);
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/PrecategorizedCommodityConfiguration.cs` (новый);
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/ReceiptDbContext.cs` (правка).

**Что сделать:**
1. SQL-скрипт **дословно по схеме задачи / ADR-027 п.3**: таблица `precategorized_commodities`
   (`id uuid PK`, `name varchar(256) NOT NULL`, `merchant_id uuid NOT NULL`, `proposed_category_id integer NOT NULL`,
   `status smallint NOT NULL DEFAULT 0`, `created_at timestamptz NOT NULL DEFAULT now()`, `applied_at timestamptz`),
   `fk_precategorized_commodities_merchants ... ON DELETE CASCADE`, `ck_..._status CHECK (status IN (0,1))`,
   unique-индекс `ux_precategorized_commodities_name_merchant (name, merchant_id)`,
   индекс `ix_precategorized_commodities_status (status, created_at)`.
   **Индексы на `commodities` в скрипт не добавлять** (NFR-6) — оставить в скрипте комментарий-пояснение,
   как в задаче. Скрипт идемпотентностью не занимается: повторный запуск отсекает `MigrationRunner`
   по `migration_scripts_history`.
2. Enum `PrecategorizedCommodityStatus { Proposed = 0, Applied = 1 }` — в Domain (используется и EF-конфигом, и доменной моделью шага 2).
3. `PrecategorizedCommodityEntity`: `Id`, `Name`, `MerchantId`, `ProposedCategoryId`, `Status`
   (`PrecategorizedCommodityStatus`), `CreatedAt`, `AppliedAt?`, навигация `Merchant?`.
4. `PrecategorizedCommodityConfiguration`: `ToTable("precategorized_commodities")`, `HasKey`,
   `Name` — `IsRequired().HasMaxLength(256)`, `Status` — `HasConversion<short>()` + `HasDefaultValue(Proposed)`,
   `CreatedAt` — `HasDefaultValueSql("now()")`, FK на `merchants` с **`OnDelete(DeleteBehavior.Cascade)`**
   (сознательно иначе, чем `RESTRICT` у `receipts` — комментарий в коде со ссылкой на ADR-027),
   `HasIndex(e => new { e.Name, e.MerchantId }).IsUnique()` и `HasIndex(e => new { e.Status, e.CreatedAt })`.
   **Индексы объявлять в EF-конфиге обязательно:** тесты создают схему через
   `Database.EnsureCreatedAsync()` по модели EF, а не через SQL-скрипт (см. «Замечания» п. 3).
5. `ReceiptDbContext`: `DbSet<PrecategorizedCommodityEntity> PrecategorizedCommodities` +
   `modelBuilder.ApplyConfiguration(new PrecategorizedCommodityConfiguration())`.
   **Query-filter на новую сущность не ставить** (ADR-027 п.2), существующий фильтр на `ReceiptEntity` не трогать.

**Тесты:** новых автотестов на этом шаге нет (схема проверяется на шагах 2 и 9).

**Проверка:**
```bash
cd Analytics && dotnet build
# применение миграции (нужен живой PostgreSQL; строка подключения — как в appsettings.Development)
cd Analytics/src/ReceiptCollector.Analytics.Migrations && \
  RECEIPTCOLLECTOR_Infrastructure__Postgres__ConnectionString="Host=localhost;Port=5432;Database=receipts;Username=admin;Password=secret" \
  dotnet run     # запустить дважды: второй прогон не должен применять скрипт повторно
```
Ожидаемо: `\d precategorized_commodities` показывает оба индекса и FK `ON DELETE CASCADE`; индексов на
`commodities` не появилось.

**Закрывает:** C3, D2, D9, FR-4.1, NFR-6; критерий приёмки «Миграция применяется без ошибок; в БД есть
таблица … с unique-индексом `(name, merchant_id)` и индексом по `status` (индексы на `commodities` не входят)».

---

## Шаг 2. Доменная модель, контракт и EF-репозиторий очереди

**Цель:** весь доступ к данным очереди (кандидаты, страница админа, вставка предложения, confirm-транзакция)
в одном репозитории; глобальный доступ — явно и только здесь.

**Проекты/файлы:**
- `Domain/Modules/Commodities/PrecategorizedCommodity.cs` (новый);
- `Domain/Modules/Commodities/IPrecategorizedCommodityRepository.cs` (новый);
- `Infrastructure/Persistence/Postgres/PrecategorizedCommodityRepository.cs` (новый, `internal sealed`,
  как остальные репозитории; тесты видят его через существующий `InternalsVisibleTo`);
- `tests/.../Infrastructure/PrecategorizedCommodityRepositoryTests.cs` (новый, Testcontainers.PostgreSql —
  по образцу `PostgresReceiptRepositoryTests`, `EnsureCreatedAsync`).

**Что сделать (состав контракта — ориентир по ADR-027 п.2/п.3/п.4/п.6):**
- `PrecategorizedCommodity` — доменная модель: `Id`, `Name`, `MerchantId`, `MerchantName?`,
  `ProposedCategory` (`CommodityCategory`), `Status`, `CreatedAt`, `AppliedAt?`.
  **Денормализованного имени категории нет** (D9) — отображение через `CommodityCategoryHelper.GetDisplayName`.
- `IPrecategorizedCommodityRepository`:
  - `GetCandidatesAsync(CandidateCursor? after, int limit, ct)` — DISTINCT пары `(name, merchant_id)`
    по товарам **всех** пользователей с `category_id IS NULL OR = 0`, исключения FR-3.2 (пара есть в таблице
    в любом статусе — **приоритет выше FR-3.3**) и FR-3.4 (есть `proposed` с этим `name` у другого магазина),
    детерминированный порядок `(name, merchant_id)` (FR-3.5), продолжение после курсора (D14).
    Реализация: корень запроса — `_dbContext.Receipts.IgnoreQueryFilters()` с **обязательным комментарием**
    «доступ вне owner-scoping намеренный: фоновая предкатегоризация обрабатывает данные всех пользователей
    (ADR-027, D8)», далее `SelectMany(r => r.Items…)`.
  - `GetKnownCategoryByNameAsync(string exactName, ct)` — глобальный existing-шаг фона (FR-3.3/D1):
    первая непустая категория (`CategoryId != null && != 0`) среди товаров всех пользователей
    по **точному** `name`; комментарий о намеренном глобальном доступе.
  - `GetPageAsync(int limit, int offset, string? search, ct)` + `GetPageCountAsync(string? search, ct)` —
    только `status = proposed`, поиск `EF.Functions.ILike` по `name`/`merchants.name`, join магазина
    для `MerchantName`, сортировка `created_at DESC`.
  - `GetByIdAsync(Guid id, ct)`, `UpdateProposedCategoryAsync(Guid id, CommodityCategory category, ct)`.
  - `TryAddProposalAsync(...)` — вставка `proposed`; **unique-конфликт `(name, merchant_id)` не пробрасывается**
    (FR-4.5): `ON CONFLICT DO NOTHING` либо перехват `DbUpdateException` с `PostgresException.SqlState == "23505"`
    → `false` + `LogDebug`.
  - `ConfirmAsync(Guid id, CommodityCategory category, ct)` — **одна транзакция** (`BeginTransactionAsync`):
    (1) `ExecuteUpdate` по `Commodities` где `Name == row.Name && (CategoryId == null || CategoryId == 0)` —
    писать `category_id` **и** `category_name` (`CommodityCategoryHelper.GetDisplayName`), как в
    `CommodityRepository.UpdateCategoryAsync` (FR-6.2/6.3); (2) строка → `applied`, `applied_at = now()`;
    (3) `ExecuteDelete` «сестринских» `proposed` с тем же точным `name`; commit. Возвращает результат
    `{ Outcome: Applied | NotFound | Conflict, UpdatedCommodities, Name, CategoryId }`. `Conflict` — если строка
    уже `applied` (FR-5.7/5.8). Кэш **здесь не трогать** (это делает сервис шага 6, после commit — D10).
  - `DeleteProposedAsync(Guid id, ct)` — удаление только `proposed`; `applied` → `Conflict`; нет строки → `NotFound`.
  - `GetCategoriesByExactNamesAsync(IReadOnlyCollection<string> names, ct)` — batch для FR-9.3:
    словарь `name → CommodityCategory`, любая строка (`proposed`/`applied`), при дублях — самая свежая
    (`applied_at`/`created_at`), невалидные значения (`!Enum.IsDefined`) отбрасываются.
- **Курсор** (`CandidateCursor(Name, MerchantId)`) объявить в Domain рядом с контрактом; сравнение
  «после курсора» — по паре в детерминированном порядке. `Guid`-сравнение в EF транслируется ненадёжно:
  рекомендуется raw SQL с row-comparison `(name, merchant_id) > (:name, :merchantId)` через
  `FromSqlInterpolated`, либо keyset-предикат с проверкой трансляции в тесте (см. «Замечания» п. 6).
  Выбор — за реализацией, поведение фиксируется тестом.

**Тесты (`PrecategorizedCommodityRepositoryTests`, Postgres-контейнер):**
1. `GetCandidatesAsync` возвращает пары товаров **двух разных пользователей**, `category_id IS NULL` и `= 0`.
2. Исключения: пара есть в таблице (`proposed` и `applied` — оба случая); имя с `proposed` у другого магазина
   (FR-3.4); проверка приоритета FR-3.2 над FR-3.3.
3. Детерминированный порядок + `after`-курсор: вторая выборка начинается со следующего кандидата.
4. **Глобальный доступ**: `context.SetCurrentUser(userId)` выставлен, кандидаты всё равно содержат
   данные всех пользователей (проверка `IgnoreQueryFilters`).
5. `TryAddProposalAsync` дважды для одной пары → второй вызов `false`, без исключения (FR-4.5).
6. `GetPageAsync`: только `proposed`, `limit/offset`, `search` по имени и по магазину, count совпадает.
7. `GetCategoriesByExactNamesAsync`: `applied`-строка находится; при двух строках берётся свежая;
   `Enum.IsDefined`-фильтр (записать невалидный `proposed_category_id` напрямую SQL и проверить, что имя
   не попало в результат).
8. `DeleteProposedAsync`: удаляет `proposed`, `applied` → `Conflict`.
9. `ConfirmAsync`: обновляет товары двух пользователей, не трогает товары с другой категорией и с другим
   наименованием, возвращает `UpdatedCommodities`; при отсутствии некатегоризированных — `Applied` с `0`;
   повторный вызов → `Conflict` (FR-6, D10, D11).

**Проверка:**
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityRepository"
```

**Закрывает:** C2, C3, D1 (existing-запрос), D7, D8, D10, D11, D14 (часть), FR-3.1–3.5, FR-4.5, FR-5.1,
FR-6.1–6.3, FR-6.6, FR-9.2/9.3 (чтение), NFR-4, NFR-5; критерии приёмки по выборке кандидатов,
unique-конфликту, подтверждению у двух пользователей, глобальному доступу.

---

## Шаг 3. Опции `Precategorization:*`, валидация и внедряемое время

**Цель:** конфигурируемость без пересборки (NFR-7) и тестируемость времени/задержек (NFR-9).

**Проекты/файлы:**
- `Infrastructure/Configuration/Options/PrecategorizationOptions.cs` (новый, по образцу `AiOptions`/
  `CommodityCategoryCacheOptions`: `public const string SectionName = "Precategorization"`);
- `Infrastructure/Configuration/DependencyInjectionExtensions.cs` (биндинг + регистрация `TimeProvider`);
- `Api/appsettings.json` — секция с дефолтами (можно на шаге 11, но значения нужны тестам — допустимо здесь);
- `tests/.../Infrastructure/PrecategorizationOptionsTests.cs` (новый);
- `tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj` — пакет
  `Microsoft.Extensions.TimeProvider.Testing` (версия, совместимая с `net10.0`; для консистентности
  с проектом Migrations — 10.x).

**Что сделать:**
1. `PrecategorizationOptions`: `Enabled = true`, `IdleTimeout = 00:05:00`, `IdleCheckInterval = 00:00:30`,
   `BatchLimit = 5`, `IntervalBetweenCalls = 00:00:02` — **значения строго по таблице ADR-027 п.9**.
2. Биндинг `services.AddOptions<PrecategorizationOptions>().Bind(configuration.GetSection(...)).ValidateOnStart()`
   + правила: `IdleTimeout > 0`, `IdleCheckInterval > 0`, `BatchLimit >= 1`, `IntervalBetweenCalls >= 0`.
   Невалидные значения → **явная ошибка на старте** (`OptionsValidationException`), не «тихая деградация»;
   способ выбран здесь и фиксируется тестом (NFR-7).
3. `services.AddSingleton(TimeProvider.System)` — единственная точка получения времени; всё новое
   (монитор шага 4, фон шага 5) берёт `TimeProvider` из DI. `DateTimeOffset.UtcNow`/`Task.Delay` напрямую
   в новом коде **не использовать**.
4. Регистрация в DI: `services.AddScoped<IPrecategorizedCommodityRepository, PrecategorizedCommodityRepository>()`
   (после шага 2).

**Тесты:**
- валидация: `BatchLimit = 0`, `IdleTimeout <= 0`, `IntervalBetweenCalls < 0` → ошибка валидации;
  валидный набор проходит; дефолты совпадают с ADR;
- smoke: `FakeTimeProvider` из DI резолвится и `GetUtcNow()` управляется тестом (инфраструктура
  тестируемости работает).

**Проверка:** `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizationOptions"`.

**Закрывает:** NFR-7, NFR-9 (инфраструктура), FR-1.3/2.1/2.3/2.4 (значения по умолчанию).

---

## Шаг 4. Монитор активности и middleware (`/health` исключён)

**Цель:** глобальный признак последней HTTP-активности, O(1) без I/O и блокировок (FR-1.1–1.4, C1, D3).

**Проекты/файлы:**
- `Application/Modules/Commodities/Contracts/IApiActivityMonitor.cs` (новый);
- `Infrastructure/Modules/Commodities/ApiActivityMonitor.cs` (новый, `internal sealed`, Singleton);
- `Api/Middleware/RequestActivityMiddleware.cs` (новый);
- `Api/Program.cs` — регистрация middleware **сразу после `UseHttpsRedirection()` и до `UseStaticFiles()`**;
- `Infrastructure/Configuration/DependencyInjectionExtensions.cs` — `AddSingleton<IApiActivityMonitor, ApiActivityMonitor>()`;
- `tests/.../Infrastructure/ApiActivityMonitorTests.cs`, `tests/.../Middleware/RequestActivityMiddlewareTests.cs`.

**Что сделать:**
1. Контракт: `void Touch(); DateTimeOffset LastActivityUtc { get; } bool IsIdle(TimeSpan threshold);`.
2. `ApiActivityMonitor(TimeProvider timeProvider)`: хранит `long _lastActivityTicks` (`Interlocked.Exchange`/
   `Volatile.Read` — `DateTimeOffset` не атомарен, хранить тики UTC); в конструкторе инициализируется
   `timeProvider.GetUtcNow()` (FR-1.3: отсчёт от старта, если запросов не было);
   `IsIdle(threshold) => timeProvider.GetUtcNow() - LastActivityUtc >= threshold`.
3. `RequestActivityMiddleware`: на каждом запросе вызывает `Touch()`, **кроме** исключённых путей.
   Исключение — расширяемый предикат `private static bool IsExcludedPath(PathString path) =>
   path.StartsWithSegments("/health")` (FR-1.2, D3); комментарий, почему `/health` исключён
   (health-пробник иначе навсегда блокирует фон). Ничего не пишет в лог на каждый запрос.
4. `Program.cs`: `app.UseHttpsRedirection(); app.UseMiddleware<RequestActivityMiddleware>(); app.UseStaticFiles();`
   — порядок обязателен (статика и SPA считаются активностью). `MapGet("/health", …)` не менять.

**Тесты:**
- монитор: сразу после старта при `FakeTimeProvider` `IsIdle(IdleTimeout) == false`; после `Advance(IdleTimeout)`
  → `true`; `Touch()` сбрасывает; параллельные `Touch()` из N потоков не ломают инвариант (нет исключений,
  `LastActivityUtc` монотонно не меньше старта);
- middleware (через `DefaultHttpContext` + подставной монитор или реальный монитор с `FakeTimeProvider`):
  `/health` → `Touch` не вызван; `/api/receipts` → вызван; `/assets/index.js` и `/` → вызван (D3).

**Проверка:** `cd Analytics && dotnet test --filter "FullyQualifiedName~Activity"`.

**Закрывает:** C1, D3, FR-1.1–1.5 (механика), NFR-1 (O(1) обновление); критерии приёмки «запрос к `/health`
не сбрасывает таймер простоя; запрос к `/api/*` — сбрасывает».

---

## Шаг 5. Фоновый сервис `PrecategorizationBackgroundService`

**Цель:** сессии предкатегоризации в простое: последовательно, с лимитами, паузой, курсором и корректной
остановкой по активности (C5, D4–D7, D12, D14, FR-2, FR-4, FR-8).

**Проекты/файлы:**
- `Infrastructure/Modules/Commodities/PrecategorizationBackgroundService.cs` (новый, `BackgroundService`,
  паттерн `ReceiptSynchronizationHostedService`: `IServiceScopeFactory`, scope на цикл, ошибки логируются
  и не валят процесс, graceful shutdown);
- `Infrastructure/Configuration/DependencyInjectionExtensions.cs` — `services.AddHostedService<PrecategorizationBackgroundService>()`;
- `tests/.../Infrastructure/PrecategorizationBackgroundServiceTests.cs` (новый).

**Что сделать:**
1. Цикл: пока не отменён `stoppingToken` — если `!_options.Enabled` → лог Information и выход (FR-1.5);
   если `monitor.IsIdle(IdleTimeout)` → `RunSessionAsync()`; затем
   `await Task.Delay(IdleCheckInterval, _timeProvider, stoppingToken)` (без busy-loop, FR-2.1).
2. Сессия (`RunSessionAsync`): создать scope, получить репозиторий/кэш/`IAiClient`.
   - `GetCandidatesAsync(_cursor, BatchLimit, ct)`; пусто → стоп-причина «нет кандидатов».
   - Для каждого кандидата **по порядку** (D4): (а) `GetKnownCategoryByNameAsync(name)` — если есть,
     предложение с этой категорией, ИИ не вызывается (FR-4.3/D1); (б) иначе `ICommodityCategoryCache.TryGet`
     по `CommodityNameNormalizer.NormalizeName` — аналогично; (в) иначе, если `AI:BaseUrl` пуст → Warning
     один раз на сессию и **без ИИ** (D5); (г) иначе `IAiClient.SuggestCategoryAsync(name, CategoryNames, ct)`,
     затем пауза `IntervalBetweenCalls` через `TimeProvider` (**только после вызова ИИ**, D4/FR-2.4).
   - Валидная категория (`!= Undefined`, `Enum.IsDefined`) → `TryAddProposalAsync` (unique-конфликт → no-op,
     FR-4.5); `Undefined`/`AiClientException` → строки нет, Warning (D6/FR-4.4).
   - Пропуск по FR-4.7: пустое/пробельное имя или длина > 256 → Warning, кандидат не обрабатывается,
     **но курсор двигается**.
   - После каждой позиции: `_cursor = candidate` (D14, включая пропуски/ошибки) и проверка активности —
     если `!monitor.IsIdle(IdleTimeout)` (то есть появился запрос после старта сессии), сессия
     завершается **после текущей позиции** (FR-2.5), новые не берутся.
   - Ограничение `BatchLimit` позиций (FR-2.3); исключение по одной позиции логируется и сессия продолжается
     (FR-2.6); исключение выборки кандидатов → сессия завершается, процесс живёт (FR-2.7).
3. `commodities` не изменять (FR-4.6). Курсор — приватное поле `CandidateCursor? _cursor`, при рестарте `null`
   (D14).
4. Наблюдаемость (FR-8): старт/стоп сессии с причиной (`batch_limit` | `activity` | `no_candidates` | `shutdown`),
   число кандидатов, созданных предложений, ошибок ИИ, длительность; по каждому вызову ИИ — длительность
   и результат (категория/`Undefined`/ошибка); **без userId и содержимого чеков** сверх наименования (FR-8.3).
5. Single instance — комментарий в коде (D12); распределённой блокировки не добавлять.

**Тесты (fake `IApiActivityMonitor`, `FakeTimeProvider`, fake `IAiClient`, fake cache, fake репозиторий;
запуск через `StartAsync`/`StopAsync` + `Advance` времени — без реальных `Task.Delay`):**
1. Простой не истёк → сессия не начинается; после `Advance(IdleTimeout)` → начинается.
2. `BatchLimit = 2`, кандидатов 5 → обработано ровно 2, причина `batch_limit`.
3. `Touch()` (монитор перестал быть idle) во время сессии → после текущей позиции новых не берётся,
   результат текущей сохранён (FR-2.5).
4. Пауза `IntervalBetweenCalls` выдерживается между вызовами ИИ; для cache-hit/existing-hit задержки нет.
5. existing-hit (категория найдена у другого товара) → строка создана, `IAiClient` не вызван;
   cache-hit → то же; приоритет existing над cache.
6. `Undefined` и `AiClientException` → строк нет, сессия продолжается.
7. **D14:** кандидат с `Undefined`/ошибкой двигает курсор — следующая сессия начинается со следующего
   кандидата, а не с него же.
8. `DbUpdateException`/unique-конфликт при вставке → сессия не падает (FR-4.5).
9. Ошибка `GetCandidatesAsync` → сессия завершается, `ExecuteAsync` не бросает (FR-2.7).
10. `Enabled = false` → сессий нет; `AI:BaseUrl` пуст → строки из кэша/existing создаются, ИИ не вызывается,
    есть Warning (D5).
11. Отмена `stoppingToken` → выход без исключений, ранее записанные строки на месте (FR-2.8).
12. Пропуск имени > 256 символов и пробельного — с Warning, курсор двигается (FR-4.7).

**Проверка:** `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizationBackgroundService"`.

**Закрывает:** C1, C5, D4, D5, D6, D7, D12, D14, FR-1.5, FR-2.1–2.8, FR-3.6, FR-4.2–4.7, FR-8; критерии
приёмки: старт по простою, остановка по запросу, `BatchLimit`/пауза, cache-first, `Undefined`/ошибка без строки,
курсор D14, повторная вставка без дубля, `Enabled=false` отключает только фон.

---

## Шаг 6. Админский сервис `PrecategorizedCommodityService` (транзакция + кэш)

**Цель:** бизнес-логика админского workflow: список, изменение, подтверждение (транзакция + `TryAdd` в кэш),
отклонение (D2, D10, FR-5.2–5.4, FR-6).

**Проекты/файлы:**
- `Application/Modules/Commodities/Contracts/IPrecategorizedCommodityService.cs` (новый);
- `Application/Modules/Commodities/Models/PrecategorizedCommodityModels.cs` (новый: `PrecategorizedCommodityDto`,
  `ConfirmPrecategorizedResult`, `PrecategorizedOperationOutcome`);
- `Infrastructure/Modules/Commodities/PrecategorizedCommodityService.cs` (новый);
- `Infrastructure/Configuration/DependencyInjectionExtensions.cs` — `AddScoped<IPrecategorizedCommodityService, …>()`;
- `tests/.../PrecategorizedCommodityServiceTests.cs` (новый).

**Что сделать:**
1. `GetPageAsync(limit, offset, search, ct)` → `(IReadOnlyCollection<PrecategorizedCommodityDto>, int total)`.
   `PrecategorizedCommodityDto` — `Id`, `Name`, `MerchantId`, `MerchantName`, `ProposedCategoryId`,
   `ProposedCategoryName` (через `CommodityCategoryHelper.GetDisplayName`, D9), `Status` (wire `proposed`),
   `CreatedAt` — ровно контракт задачи, п.1 «Список предложений».
2. `UpdateCategoryAsync(id, category, ct)` — только `proposed`; `applied` → `Conflict`, нет строки → `NotFound`;
   `commodities` не трогает (UC-3). Валидацию значения (`Enum.IsDefined` + `!= Undefined`) выполняет
   эндпоинт шага 8 (как `MerchantEndpoints.UpdateCategory`), сервис дополнительно защищается `ArgumentException`.
3. `ConfirmAsync(id, ct)`: вызвать `repository.ConfirmAsync` (транзакция шага 2) → при `Applied`
   **после успешного commit** `_cache.TryAdd(CommodityNameNormalizer.NormalizeName(name), category)`
   (FR-6.4; first-wins/no-op при «замороженном» кэше — семантика ADR 019, не менять); вернуть
   `{ CategoryId, CategoryName, UpdatedCommodities }` (FR-6.6, D10: строка становится `applied` даже при `0`).
4. `DeclineAsync(id, ct)` — удаление только `proposed` (C4); `applied` → `Conflict` (FR-5.7).
5. Никаких `IgnoreQueryFilters` в сервисе — он работает через репозиторий (единственная точка глобального
   доступа, NFR-5).

**Тесты (fake репозиторий + fake/реальный `InMemoryCommodityCategoryCache`):**
1. `ConfirmAsync` при `Applied` кладёт категорию в кэш по **нормализованному** имени (последующий `TryGet`
   по имени в другом регистре/с лишними пробелами находит категорию) — FR-6.4.
2. При `Conflict`/`NotFound` кэш не меняется.
3. `UpdatedCommodities = 0` всё равно даёт `Applied` (D10).
4. `UpdateCategoryAsync` не вызывает репозиторные методы обновления `commodities` (read-only по отношению к товарам).
5. `DeclineAsync` на `applied` → `Conflict`, на отсутствующей → `NotFound`.
6. `GetPageAsync` пробрасывает `limit/offset/search` и возвращает total.

**Проверка:** `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityService"`.

**Закрывает:** D2, D9, D10, D11, FR-5.2–5.4, FR-5.7, FR-5.8, FR-6.4–6.6; критерий приёмки «подтверждение
переводит строку в `applied`, удаляет одноимённые `proposed`-строки и добавляет категорию в in-memory кэш».

---

## Шаг 7. FR-9: очередь в ручной категоризации чека (`source = "precategorized"`)

**Цель:** между кэшем и ИИ добавить поиск по очереди; контракт suggest меняется только аддитивно.

**Проекты/файлы:**
- `Application/Modules/Commodities/Models/CategorizationModels.cs` — `CategorizationSource.Precategorized`,
  `CategorizationSourceNames.Precategorized = "precategorized"`, ветка в `ToWireName` (**не сломать
  существующие wire-имена `existing`/`cache`/`ai`/`undefined`**);
- `Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` — новый шаг + зависимость
  `IPrecategorizedCommodityRepository` в конструкторе;
- `tests/.../CommodityCategorizationServiceTests.cs` — обновить фабрику `CreateService` (14 существующих
  тестов конструируют сервис напрямую) и добавить новые тесты.

**Что сделать:**
1. В `SuggestForReceiptAsync` после шага кэша и **до** `SuggestByAiAsync`: собрать различные **точные**
   наименования позиций, оставшихся без категории, и выполнить **один** batch-вызов
   `GetCategoriesByExactNamesAsync` (FR-9.3, N+1 запрещён). Найденное — результат с
   `CategorizationSource.Precategorized` и `CategoryName` из `CommodityCategoryHelper`; ИИ для этих позиций
   не вызывается (FR-9.4).
2. Порядок строго `existing → cache → precategorized → ai` (D13); шаг `existing` (категория самого товара)
   и шаг `cache` не менять.
3. Ошибка чтения очереди → `LogWarning` и продолжение обработки через ИИ, без `500` (FR-9.6).
4. `suggest` остаётся read-only: **не** писать в `commodities`, **не** менять `precategorized_commodities`,
   **не** наполнять кэш из очереди (FR-9.5, D13 — кэш обновляется только подтверждением админа/пользователем).
5. `Precategorization:Enabled` не влияет на этот шаг — очередь читается всегда (FR-9.7).

**Тесты (добавить в `CommodityCategorizationServiceTests`):**
1. Наименование есть в очереди → `source == "precategorized"`, `IAiClient` не вызван, категория и имя из очереди.
2. Порядок: `existing` побеждает очередь и кэш; `cache` побеждает очередь; очередь побеждает ИИ.
3. Batch: чек с 3 различными наименованиями → ровно один вызов репозитория, в аргументах — различные имена
   (без уже найденных на `existing`/`cache`).
4. Read-only: после `SuggestForReceiptAsync` нет вызовов записи репозитория и `cache.TryAdd`.
5. Ошибка репозитория (исключение) → результат приходит из ИИ, исключения нет, есть Warning.
6. `Undefined`-значение из очереди отброшено репозиторием → позиция идёт в ИИ (проверяется на уровне сервиса
   через fake с пустым словарём — репозиторный фильтр покрыт шагом 2).

**Проверка:** `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategorizationService"`.

**Закрывает:** D13, FR-9.1–9.7, NFR-3 (ИИ не вызывается при попадании в очередь), NFR-8 (аддитивность);
критерий приёмки «ручной `suggest` … `source = "precategorized"`, один batch-запрос, read-only, деградация».

---

## Шаг 8. Админский API `/api/precategorized-commodities`

**Цель:** 4 эндпоинта с правами по эталону `MerchantEndpoints` и кодами по FR-5.

**Проекты/файлы:**
- `Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs` (новый);
- `Api/Program.cs` — `app.MapPrecategorizedCommodityEndpoints();`;
- `tests/.../PrecategorizedCommodityEndpointsTests.cs` (новый, по образцу `MerchantEndpointsTests`:
  прямой вызов handler'ов, `UserContext.SetUserId`, NSubstitute).

**Что сделать:**
1. `GET /api/precategorized-commodities?limit=50&offset=0&search=` — только `proposed`, `X-Total-Count`
   через `CultureInfo.InvariantCulture`, `limit` 1..200 (иначе `400`), `offset >= 0` (иначе `400`).
   Обратите внимание: у `MerchantEndpoints.GetAll` верхняя граница `limit` не проверяется — здесь её
   **добавить** по ADR-027 п.7 (это требование нового контракта, а не рефакторинг старого).
2. `PUT /{id:guid}/category` — тело `{ categoryId }`, валидация `Enum.IsDefined(typeof(CommodityCategory), id)`
   и `id != 0` → иначе `400`; ответ `{ id, categoryId, categoryName }`.
3. `POST /{id:guid}/confirm` — ответ `{ id, categoryId, categoryName, updatedCommodities }`.
4. `DELETE /{id:guid}` — `204` при успехе.
5. Права (FR-5.5, эталон `MerchantEndpoints`): `UserContext.UserId` пуст → `Results.Unauthorized()`;
   `IUserRepository.GetByIdAsync(...).IsAdmin == false` → `Results.Forbid()`; проверка на сервере.
6. Ошибки — **текстовые** (`Results.BadRequest("...")`, `NotFound`, `Conflict`), **без ProblemDetails** (ADR-027 п.7).
   Маппинг: `NotFound` → `404`, `Conflict` → `409` (FR-5.7/5.8).
7. Никакой бизнес-логики в эндпоинтах сверх валидации и авторизации — всё в `IPrecategorizedCommodityService`.

**Тесты (`PrecategorizedCommodityEndpointsTests`):**
1. `GET`: без пользователя → `401`; не-админ → `403`; `limit = 0` / `limit = 201` / `offset = -1` → `400`;
   успешный ответ содержит `X-Total-Count` и DTO из контракта.
2. `PUT`: невалидный `categoryId` (`0`, `999`) → `400`; `applied` → `409`; нет строки → `404`; успех → `200`
   с `categoryName`.
3. `POST confirm`: успех → `200` с `updatedCommodities` (в т.ч. `0`); повторный → `409`; нет строки → `404`.
4. `DELETE`: успех → `204`; `applied` → `409`; нет строки → `404`; без прав → `401`/`403`.
5. `MapPrecategorizedCommodityEndpoints` регистрирует группу с тегом `PrecategorizedCommodities`
   (smoke, чтобы маршрут не потерялся в `Program.cs`).

**Проверка:** `cd Analytics && dotnet test --filter "FullyQualifiedName~PrecategorizedCommodityEndpoints"`.

**Закрывает:** FR-5.1–5.9, NFR-2, NFR-8; критерий приёмки «`GET/PUT/POST/DELETE` без аутентификации → `401`;
не-админ → `403`; невалидная категория → `400`; несуществующая строка → `404`; повторный confirm/изменение
`applied`-строки → `409`» и «список возвращает только `proposed`, учитывает `limit/offset/search` и `X-Total-Count`».

---

## Шаг 9. Сквозные тесты, архитектура и проверка миграции

**Цель:** закрыть интеграционные критерии приёмки, которые не покрываются unit-тестами, и убедиться,
что слои не нарушены.

**Проекты/файлы:**
- `tests/.../Infrastructure/PrecategorizedCommodityWorkflowTests.cs` (новый, Postgres-контейнер,
  реальные `PrecategorizedCommodityRepository` + `PrecategorizedCommodityService` +
  `InMemoryCommodityCategoryCache`);
- `tests/.../Architecture/ProjectDependencyTests.cs` — без изменений (проверить, что проходит);
- при необходимости — `ClearDatabaseAsync`-подобная очистка с учётом новой таблицы.

**Что сделать / проверить:**
1. **Сквозной confirm:** два пользователя, у каждого товар с одинаковым точным именем и без категории,
   у третьего — тот же товар с категорией, у четвёртого — похожее имя; подтверждение предложения обновляет
   ровно два товара, возвращает `updatedCommodities = 2`, строка `applied`, «сестринская» `proposed` удалена,
   кэш содержит категорию (FR-6.1–6.4, D10). Отдельно — случай `updatedCommodities = 0` (все уже
   категоризированы) при переводе строки в `applied`.
2. **Отклонение:** строка удалена, `commodities` не изменились; после отклонения кандидат снова попадает
   в выборку `GetCandidatesAsync` (C4, FR-5.4).
3. **Глобальный доступ при выставленном owner-scope:** `context.SetCurrentUser(userId)` + вызов
   `GetCandidatesAsync`/`GetKnownCategoryByNameAsync` → данные всех пользователей (D8, NFR-5).
   Дополнительно: grep-проверка при ревью — `IgnoreQueryFilters` встречается только в
   `PrecategorizedCommodityRepository` с комментарием.
4. **Идемпотентность рестарта:** повторный `TryAddProposalAsync` той же пары после «рестарта» (новый scope)
   не создаёт дубль (NFR-4, D14).
5. **Architecture:** `ProjectDependencyTests` зелёные без ослабления правил; новые типы лежат в ожидаемых
   пространствах имён (`Domain`, `Application`, `Infrastructure`, `Api`).
6. **Миграция:** прогон скрипта на чистой БД (шаг 1) и повторный прогон `MigrationRunner` — идемпотентность
   за счёт `migration_scripts_history`; сверить фактическую схему с EF-моделью (индексы/FK/типы).

**Проверка:**
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test          # полный прогон; для Testcontainers нужен Docker
```

**Закрывает:** NFR-4, NFR-5, NFR-2; критерии приёмки: подтверждение у двух пользователей, отклонение,
глобальный запрос при `CurrentUserId`, Architecture-тесты, `cd Analytics && dotnet test`.

---

## Шаг 10. Frontend: раздел «Предкатегоризация» и `source = precategorized`

**Цель:** админский раздел webUI и отображение нового источника в карточке чека (FR-7, ADR-023).

**Проекты/файлы (все — `Analytics/frontend/src`):**
- `types/precategorizedCommodity.ts` (новый: `PrecategorizedCommodityItem`, `PaginatedPrecategorizedCommodities`);
- `api/precategorizedCommodities.ts` (новый: `fetchPrecategorizedCommodities({ limit, offset, search, signal })`,
  `updatePrecategorizedCategory(id, categoryId)`, `confirmPrecategorized(id)`, `declinePrecategorized(id)`;
  все — `credentials: 'include'`, разбор `X-Total-Count` как в `api/commodities.ts`/`api/merchants.ts`);
- `hooks/usePrecategorizedCommodities.ts` (новый, по образцу `useMerchants`: page-based состояние,
  `totalItems` из `X-Total-Count`, `AbortController`, `refresh`; поиск — по прототипу `api/merchants.ts`
  (параметр `search`), т.к. в существующих хуках его нет);
- `components/PrecategorizedCommoditiesPage.tsx`, `components/PrecategorizedCommodityTable.tsx` (новые);
- `routes.ts` — `precategorizedPath()` → `/precategorized`;
- `App.tsx` — `<Route path="/precategorized" element={<PrecategorizedCommoditiesPage />} />`;
- `components/Sidebar.tsx` — новый пункт внутри существующего условия `{isAdmin && (...)}`;
- `api/receipts.ts` — `CategorizationSource` += `'precategorized'`;
- `components/ReceiptDetails.tsx` — ветка в `getSuggestionNote` (например, «из очереди предкатегорий»).

**Что сделать:**
1. Страница: гейт по `useAdmin()` по образцу `MerchantsPage` (загрузка → «Доступ запрещён» → контент);
   для не-админа данные **не запрашиваются** (FR-7.1).
2. Таблица: наименование, магазин, предложенная категория (выпадающий список из `fetchCategories()`
   с фильтром `key !== 'Undefined'`, см. `ReceiptDetails.tsx`), дата создания, действия
   «Подтвердить»/«Отклонить»; `Pagination` — на уровне страницы (как `MerchantsPage`, не внутри таблицы).
3. Поиск: поле ввода + сброс на первую страницу (по образцу `api/merchants.ts`), debounce допустим;
   пустое состояние («Нет предложений»), состояние загрузки и ошибки (FR-7.4).
4. «Отклонить» — `CustomDialog` с подтверждением (паттерн `ReceiptDetails.tsx`); после действий — toast
   (`Toasts`), после подтверждения — текст с числом обновлённых товаров (`updatedCommodities`) и `refresh()`.
5. Смена категории в строке сохраняется через `PUT .../category`; ошибки `401/403/409` показываются
   понятным сообщением, страница не падает (FR-7.6).
6. Доступность: `aria-label` на кнопках, `disabled` во время запроса; новые CSS — минимально, переиспользуя
   существующие классы; `Sidebar.css` не дублировать.

**Тесты:** автотестов фронтенда в репозитории **нет** (в `package.json` отсутствует скрипт `test`
и тест-раннер) — см. «Замечания» п. 1. Проверка ручная + сборка/линт.

**Проверка:**
```bash
cd Analytics/frontend && npm run lint && npm run build
```
Ручная проверка в браузере (после шагов 8 и 11): пункт виден только админу; прямой заход не-админом
не показывает данные; список/поиск/пагинация/смена категории/подтверждение/отклонение работают;
после подтверждения видно число обновлённых товаров; в карточке чека для позиции из очереди отображается
пометка «из очереди предкатегорий».

**Закрывает:** FR-7.1–7.6, FR-9.4 (UI-часть); критерий приёмки по frontend и частично ручную проверку.

---

## Шаг 11. Конфигурация и документация

**Цель:** включение/настройка фона без пересборки и фиксация параметров (NFR-7, ADR-021).

**Файлы:**
- `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` — секция `Precategorization`
  (`Enabled: true`, `IdleTimeout: "00:05:00"`, `IdleCheckInterval: "00:00:30"`, `BatchLimit: 5`,
  `IntervalBetweenCalls: "00:00:02"`);
- `docker-compose.yml` (сервис `analytics`, рядом с `CommodityCategoryCache__MaxSize`) —
  `Precategorization__Enabled=${Precategorization__Enabled:-true}`,
  `Precategorization__IdleTimeout=${Precategorization__IdleTimeout:-00:05:00}`,
  `Precategorization__IdleCheckInterval=${Precategorization__IdleCheckInterval:-00:00:30}`,
  `Precategorization__BatchLimit=${Precategorization__BatchLimit:-5}`,
  `Precategorization__IntervalBetweenCalls=${Precategorization__IntervalBetweenCalls:-00:00:02}`
  (обязательно `:-`-дефолты — крэш при пустой типизированной строке, как у `AI__Timeout`);
- `.env.example` — прокомментированный блок в стиле `AI__*`/`CommodityCategoryCache__MaxSize` (что делает
  `Enabled=false`: отключает **только фон**, раздел админа и поиск по очереди продолжают работать — FR-1.5/9.7);
- `AGENTS.md` — строки в таблице настроек Analytics рядом с `AI__*` и `CommodityCategoryCache__*`.

**Что сделать:** ничего не «изобретать» сверх таблицы ADR-027 п.9; `appsettings.Development.json`
не менять (дефолты наследуются из базового файла). В комментариях явно указать, что
`Enabled=false` не выключает админский раздел и очередь в ручном suggest.

**Проверка:**
```bash
docker compose config >/dev/null          # валидность compose
cd Analytics && dotnet build              # опции биндятся, старт не падает
```
Ручная: `Precategorization__Enabled=false` в окружении → в логах нет сессий, раздел `/precategorized` работает.

**Закрывает:** NFR-7, FR-1.5 (документирование), FR-9.7; критерий приёмки «`Enabled=false` полностью
отключает фоновую запись; админский раздел продолжает работать».

---

## 3. Финальная проверка (перед передачей на приёмку)

```bash
# 1. Backend: сборка и все тесты (нужен Docker для Testcontainers)
cd Analytics && dotnet build
cd Analytics && dotnet test

# 2. Миграция на чистой БД + повторный прогон (идемпотентность)
cd Analytics/src/ReceiptCollector.Analytics.Migrations && \
  RECEIPTCOLLECTOR_Infrastructure__Postgres__ConnectionString="Host=localhost;Port=5432;Database=receipts;Username=admin;Password=secret" dotnet run

# 3. Frontend (тест-раннера в проекте нет — только линт и сборка)
cd Analytics/frontend && npm run lint && npm run build

# 4. Проверка маркеров глобального доступа (ожидаем только новый репозиторий)
grep -rn "IgnoreQueryFilters" Analytics/src --include=*.cs
```

**Ручной smoke (критерии приёмки):**
1. Запустить `./up.dev.sh`; 5 минут без запросов → в логах `receipt-analytics` появляется старт сессии
   и число кандидатов/предложений; открытие страницы в браузере останавливает сессию.
2. `curl http://localhost:<port>/health` в течение простоя — сессия **не** прерывается (D3).
3. В разделе «Предкатегоризация» появились строки; подтверждение массово меняет категории в «Товарах»
   у двух разных пользователей и показывает `updatedCommodities`.
4. Открыть чек с тем же наименованием у другого пользователя → «Категоризировать» отдаёт `source = cache`
   без обращения к ИИ; для позиции из неподтверждённой очереди — `source = precategorized`.
5. Отклонение удаляет строку; при следующем простое ИИ может снова предложить это наименование (C4).
6. `Precategorization__Enabled=false` → новых строк нет, раздел и очередь в suggest работают.

---

## 4. Замечания для реализации

Фактические наблюдения по репозиторию. **Требования и ADR не меняются** — пункты ниже либо уточняют
способ реализации, либо требуют решения @software-architect/PM.

1. **Тестов фронтенда нет.** `Analytics/frontend/package.json` содержит только `dev`, `build`, `lint`,
   `preview` — тест-раннера (vitest/jest) и скрипта `test` нет. Критерий «тесты фронтенда» проверяется
   как `npm run lint && npm run build` + ручной smoke (шаг 10). Вводить тест-раннер в этой задаче —
   расширение объёма, требует отдельного решения.
2. **`suggest` возвращает `503` при пустом `AI:BaseUrl`** (`ReceiptCategorizationEndpoints.Suggest`,
   проверка стоит **до** вызова сервиса). Значит при выключенном ИИ шаг `precategorized` (FR-9) недостижим.
   Требования этого не отменяют (FR-9 не говорит о работе suggest без ИИ, D5 касается только фона) —
   поведение **не менять**, но зафиксировать как известное ограничение. Если ожидалось, что очередь
   должна работать без ИИ — вопрос к архитектору/PM.
3. **Тесты создают схему из EF-модели, а не из SQL-миграции.** `PostgresReceiptRepositoryTests` вызывает
   `Database.EnsureCreatedAsync()` (как и все Postgres-тесты). Поэтому unique-индекс и FK `CASCADE`
   обязаны быть объявлены в `PrecategorizedCommodityConfiguration` — иначе тесты unique-конфликта (FR-4.5)
   будут «зелёными» на пустом месте, а схема в проде (SQL-скрипт) разойдётся с тестовой. Синхронность
   скрипта и EF-конфига проверяется вручную на шаге 9.
4. **`TimeProvider`/`FakeTimeProvider` в проекте отсутствуют** (ADR-027, факт): их нужно создать и добавить
   пакет `Microsoft.Extensions.TimeProvider.Testing` в тестовый проект. Существующие тесты hosted-сервисов
   (`ReceiptSynchronizationHostedServiceTests`) используют реальные `Task.Delay` — **не рефакторить** их
   в этой задаче.
5. **`AI:Timeout` расходится** (`appsettings.json` — `00:01:20`, `AGENTS.md`/docker-compose — `00:00:10`).
   Расхождение уже в бэклоге задачи, на реализацию влияет только как граница задержки остановки сессии
   (риск 1). Не исправлять попутно.
6. **Курсор D14 и сравнение `Guid`.** Ключ порядка — `(name, merchant_id)`, где `merchant_id` — `uuid`.
   Трансляция `Guid.CompareTo`/`>` в EF/Npgsql ненадёжна. Рекомендуется raw SQL с row-comparison
   `(name, merchant_id) > (:n, :m)` (Postgres поддерживает нативно) либо keyset-предикат с обязательным
   тестом на Postgres-контейнере, подтверждающим, что следующая страница начинается ровно со следующего
   кандидата. Способ — на усмотрение реализации, поведение — по D14.
7. **`IgnoreQueryFilters()` применим только к запросам, где есть `ReceiptEntity`.** Фильтр объявлен
   только на `ReceiptEntity`; у `Commodities` фильтра нет. Поэтому маркер обязателен в выборке кандидатов
   (корень — `Receipts.IgnoreQueryFilters()`), а в запросах только по `Commodities` достаточно комментария
   о намеренном глобальном доступе. Вызов `IgnoreQueryFilters()` на `Commodities` допустим как маркер,
   но семантически ничего не меняет.
8. **Конструктор `CommodityCategorizationService` меняется** (добавляется репозиторий очереди) — 14
   существующих тестов в `CommodityCategorizationServiceTests` создают сервис напрямую, фабрику
   `CreateService` нужно обновить (шаг 7). DI-регистрация остаётся `AddScoped`.
9. **`MerchantEndpoints.GetAll` не ограничивает `limit` сверху.** Новый эндпоинт обязан проверять `1..200`
   (ADR-027 п.7) — не копировать существующую проверку и не «улучшать» `MerchantEndpoints` (вне объёма).
10. **`AddProblemDetails()` зарегистрирован, `UseProblemDetails()` в pipeline нет** — стиль ошибок остаётся
    текстовым (эталон `MerchantEndpoints`). Не включать ProblemDetails попутно.
11. **`Sidebar.tsx` использует литеральные пути**, а не функции из `routes.ts` (небольшое расхождение
    с ADR-023). Новый маршрут добавляется функцией `precategorizedPath()` и используется в `App.tsx` и
    `Sidebar.tsx`; существующие литералы не рефакторить.
12. **Очистка БД в тестах** (`ClearDatabaseAsync` в Postgres-тестах) удаляет `commodities`/`receipts`/`merchants`
    в фиксированном порядке. Новая таблица ссылается на `merchants` с `ON DELETE CASCADE` — при добавлении
    сквозных тестов (шаг 9) убедиться, что очистка не падает и не оставляет строк между тестами.
13. **Оценки шагов** — ориентировочные (1 разработчик, без учёта ревью). Общая оценка backend ≈ 7–9 д,
    frontend ≈ 1.5–2 д; при работе двух разработчиков параллельно критический путь ≈ 7–8 д.
