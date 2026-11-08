# Декомпозиция: журнал решений категоризации товаров (предложено → принято)

> Источники: задача [categorization-decision-log](../tasks/categorization-decision-log.md)
> (FR-1…FR-8, NFR-1…NFR-3, критерии приёмки, edge cases, «Вне объёма») и
> [ADR-033](../adr/033-categorization-decision-log.md) (ключевые решения A1, B1, C1, D1, E1, F1;
> схема и миграция — п. 1; карта слоёв — п. 2; точки фиксации — пп. 3–5; recorder — п. 6;
> API — п. 7; фронтенд — п. 8; безопасность — п. 9; тесты — п. 10;
> «Явно зафиксированные семантики»; матрица требований).
>
> **Этот документ — только декомпозиция.** Новых архитектурных решений он не содержит и не
> заменяет ADR-033. Спорные места эскалируются к @software-architect и исполнителем
> самостоятельно не решаются. Реализация — строго по одной задаче за цикл
> (skill `task-review-gate`): реализация → AI-ревью → ручное ревью → коммит человеком.
>
> Все изменения — только Analytics (.NET API, фронтенд). Go-сервисы (`backend/`, `bot/`),
> MongoDB и nginx не трогаются; новых настроек (`AGENTS.md`, `.env*`, `docker-compose*.yml`)
> не появляется (ADR-033 п. 2, «Явно зафиксированные семантики»).

## Ход выполнения

| ID | Задача | Зависимости | Статус |
| -- | ------ | ----------- | ------ |
| T-01 | Миграция PostgreSQL: журнал `categorization_decisions` + исходное предложение очереди | — | committed |
| T-02 | Домен и persistence журнала + маппинг `original_proposed_category_id` | T-01 | committed |
| T-03 | Recorder: контракт, допустимые источники, best-effort запись | T-02 | committed |
| T-04 | Точка фиксации 1: сохранение категорий чека | T-03 | committed |
| T-05 | Точка фиксации 2: пакетное сохранение страницы «Товары» | T-03 (порядок — после T-04) | committed |
| T-06 | Точка фиксации 3: подтверждение в `/precategorized` | T-02, T-03 (порядок — после T-05) | committed |
| T-07 | Чтение: read-service, дедуп anti-join, фильтры, `Total` | T-02 (порядок — после T-06) | committed |
| T-08 | API: `GET /api/categorization-decisions` (admin-only) | T-07 | committed |
| T-09 | Frontend: снимок предложения при сохранении чека | T-04 (порядок — после T-08) | committed |
| T-10 | Frontend: снимок предложения в черновике страницы «Товары» | T-05 (порядок — после T-09) | committed |
| T-11 | Frontend: страница журнала `/categorization-decisions` | T-08 (порядок — после T-10) | committed |
| T-12 | Финальная проверка контрактов и регрессии | T-01…T-11 | committed |

Строки таблицы задают единственный допустимый порядок исполнения: задача N стартует только после
того, как задача N-1 проверена человеком и **закоммичена**. Параллельного выполнения нет.
Значения `in-progress` / `review` / `approved` / `committed` выставляют исполнитель и
`project-manager`; в этом файле при старте все задачи — `pending`.

## Обозначения

| Поле | Значение |
|---|---|
| Приоритет | **P0** — без этого нет сквозного сценария; **P1** — требуется критериями приёмки; **P2** — качество, не блокирует |
| Оценка | человеко-дни одного разработчика, без учёта ревью и ручной приёмки |
| Зависит от | задачи, которые должны быть **влиты** (committed) до старта |
| Проверка | конкретные команды и/или ручные сценарии |

Общие команды:

```bash
cd Analytics && dotnet build
cd Analytics && dotnet test
cd Analytics/frontend && npm run lint && npm run build
```

## Сводка и порядок исполнения

| ID | Задача | Слой | Приор. | Оценка | Зависит от |
|---|---|---|---|---|---|
| T-01 | Миграция схемы (таблица журнала + колонка очереди) | Migrations | P0 | 0.5 | — |
| T-02 | Домен/EF/repository журнала; `original_proposed_category_id` в очереди | Domain/Infra | P0 | 1.0 | T-01 |
| T-03 | Recorder + `ICategorizationDecisionRecorder` + `CategorizationDecisionSources` | App/Infra | P0 | 1.0 | T-02 |
| T-04 | Точка фиксации 1: чек (эндпоинт + сервис) | Api/Infra | P0 | 1.0 | T-03 |
| T-05 | Точка фиксации 2: «Товары» (эндпоинт + сервис) | Api/Infra | P0 | 0.75 | T-03 |
| T-06 | Точка фиксации 3: `/precategorized` confirm | Api/Infra | P0 | 1.0 | T-02, T-03 |
| T-07 | Read-service: anti-join, фильтры, `Total`, имена/принявший | App/Infra | P0 | 1.25 | T-02 |
| T-08 | `GET /api/categorization-decisions` + регистрация | Api | P0 | 0.75 | T-07 |
| T-09 | FE: снимок предложения при сохранении чека | FE | P0 | 0.5 | T-04 |
| T-10 | FE: снимок в черновике страницы «Товары» | FE | P0 | 0.75 | T-05 |
| T-11 | FE: страница журнала (фильтры, таблица, пагинация, admin-гейт) | FE | P0 | 1.5 | T-08 |
| T-12 | Финальная проверка контрактов и регрессии, ручная приёмка | все | P0 | 0.5–1.0 | T-01…T-11 |

**Критический путь:** T-01 → T-02 → T-03 → T-04 → T-05 → T-06 → T-07 → T-08 → T-09 → T-10 →
T-11 → T-12. Суммарно последовательно ≈ 10.5–11 чел.-дн. до ручной приёмки.

## Общие инварианты для всех задач

1. **NFR-2 (контракты).** Изменения `suggest`/`apply` чека, страницы «Товары»,
   `/precategorized` — строго аддитивные опциональные поля. Ни один существующий код ответа,
   тело ответа, заголовок или число (`updated`, `updatedCommodities`) не меняется. Невалидный
   снимок предложения **не** является ошибкой запроса: `400` за него не отдаётся (ADR-033 пп. 3–4).
2. **NFR-1.** Ни одна задача не добавляет записи категории в `commodities` из журнала и не
   меняет поведение suggest / сохранения / подтверждения. Автоприменений нет.
3. **NFR-3.** Запись журнала — только **после** успешной фиксации пользовательского сохранения
   (после `UpdateCategoryAsync` / `UpdateCategoriesAsync` / commit транзакции confirm), одним
   батчем; `ICategorizationDecisionRecorder` не бросает исключений никогда (включая отмену
   токена). Сбой журнала логируется и не влияет на результат сервиса.
4. **Единственная нормализация имени** — `CommodityNameNormalizer.NormalizeName`; второго
   правила не появляется. **Время** — только `TimeProvider` (тесты — `FakeTimeProvider`).
5. **Без raw SQL** (проект его не использует, ADR-033 п. 2/п. 10); при необходимости —
   эскалация к архитектору (запасной путь E2 зафиксирован в ADR-033).
6. **Логи без ПД:** названия товаров — только `Debug`; `Warning` о пропущенных снимках —
   только количество (прецедент ADR-028 п. 11).
7. Зависимости проектов (`Architecture/ProjectDependencyTests`): Domain не знает
   Application/Infrastructure/Api; Application — только Domain; Infrastructure —
   Application + Domain; Api — все. Тест должен оставаться зелёным.
8. Каждая задача оставляет `cd Analytics && dotnet test` зелёным **без правок существующих
   тестов**, кроме случаев, явно перечисленных в задаче (например, изменение сигнатуры сервиса
   в T-04/T-06).

---

## T-01. Миграция PostgreSQL: журнал `categorization_decisions` + исходное предложение очереди

**Приоритет:** P0 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** —

### Цель
Создать схему хранения журнала (решение B1) и неизменяемую колонку исходного предложения
очереди (решение D1) ровно в объёме ADR-033 п. 1.

### Что входит
- Новый скрипт `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261106120000_add_categorization_decisions.sql`
  (имя файла — как в ADR-033; `MigrationRunner` сортирует скрипты по имени, транзакция на скрипт,
  учёт в `migration_scripts_history` — идемпотентность обеспечивается раннером).
- Таблица `categorization_decisions`: колонки `id uuid PK`, `commodity_name varchar(256) NOT NULL`,
  `normalized_name varchar(256) NOT NULL`, `suggestion_source varchar(32) NOT NULL`,
  `suggested_category_id integer NOT NULL`, `final_category_id integer NOT NULL`,
  `accepted_by uuid NOT NULL`, `accepted_by_admin boolean NOT NULL`,
  `created_at timestamptz NOT NULL DEFAULT now()`; констрейнты
  `ck_categorization_decisions_suggested_not_undefined CHECK (suggested_category_id <> 0)` и
  `ck_categorization_decisions_final_non_negative CHECK (final_category_id >= 0)`.
- Три индекса: `ix_categorization_decisions_combination`
  (`commodity_name, suggestion_source, suggested_category_id, final_category_id, created_at DESC, id DESC`),
  `ix_categorization_decisions_created_at (created_at DESC)`,
  `ix_categorization_decisions_mismatch (created_at DESC) WHERE suggested_category_id <> final_category_id`.
- `ALTER TABLE precategorized_commodities ADD COLUMN original_proposed_category_id integer` →
  backfill `= proposed_category_id` → `SET NOT NULL`.
- Комментарии в скрипте: почему `suggestion_source` без CHECK (множество — свойство кода),
  почему коды категорий без FK (справочник — enum, ADR-005), зачем backfill и почему
  исторические строки считаются «исходными как есть» (журнал начинается с момента внедрения).

### Что НЕ входит
- Любой код C# (сущности, репозитории, сервисы) — в T-02 и далее.
- Новые настройки, поля в `AGENTS.md`/`.env`, изменения `docker-compose*.yml`.
- Полноценная таблица истории правок очереди (D3 — отклонено).

### Тесты
- Юнит-тестов нет (SQL-миграции в проекте не покрыты, прецедент ADR-027/032).
- Проверка ручная на dev-стенде (см. ниже).

### Критерии готовности
- Скрипт применяется `MigrationRunner` на dev-стенде без ошибок; повторный прогон миграций
  ничего не выполняет (запись в `migration_scripts_history`).
- `\d categorization_decisions` показывает 3 индекса и 2 CHECK-констрейнта;
  `\d precategorized_commodities` — `original_proposed_category_id integer NOT NULL`, у
  существующих строк значение = `proposed_category_id`.
- `cd Analytics && dotnet test` зелёный (код не менялся).

### Проверка
```bash
cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run   # на dev-стенде
# psql: \d categorization_decisions ; \d precategorized_commodities
cd Analytics && dotnet test
```

### Ограничение порядка (важно)
После `SET NOT NULL` существующий код вставки в `precategorized_commodities`
(`PrecategorizedCommodityRepository.TryAddProposalAsync`) колонку ещё не пишет. Поэтому:
- миграция на dev-стенде применяется **после** того, как T-02 проверила и закоммичена
  (либо при временно выключенном фоне `Precategorization__Enabled=false`);
- T-02 стартует сразу после T-01, без промежуточных задач.

---

## T-02. Домен и persistence журнала + маппинг `original_proposed_category_id`

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-01

### Цель
Дать слою хранения возможность писать сырые события журнала и привести код очереди в
соответствие со схемой T-01 (единственная запись колонки — фоном при создании строки).

### Что входит
- **Domain:** `Modules/CategorizationDecisions/CategorizationDecision.cs` — запись события
  (`Id`, `CommodityName`, `NormalizedName`, `SuggestionSource` — wire-имя строкой,
  `SuggestedCategory`, `FinalCategory` (оба `CommodityCategory`), `AcceptedBy`,
  `AcceptedByAdmin`, `CreatedAt`); `Modules/CategorizationDecisions/ICategorizationDecisionRepository.cs` —
  на этом шаге **только** `AddRangeAsync(IReadOnlyCollection<CategorizationDecision>, CancellationToken)`.
- **Domain:** `PrecategorizedCommodity` += свойство `OriginalProposedCategory` (конструктор и
  порядок параметров — аддитивно, чтобы существующие вызовы читались явно; при необходимости
  обновить существующие тесты-конструкторы).
- **Infrastructure:** `Persistence/Postgres/CategorizationDecisionEntity.cs`,
  `Configurations/CategorizationDecisionConfiguration.cs` (имя таблицы, snake_case колонок,
  типы, оба CHECK и все три индекса объявлены **и в EF-конфигурации** — тесты создают схему из
  EF-модели, прецедент `PrecategorizedCommodityRepositoryTests`),
  `CategorizationDecisionRepository.cs` (`AddRangeAsync` — один `SaveChangesAsync`, без
  транзакции и без валидации; валидация — в T-03).
- `ReceiptDbContext`: `DbSet<CategorizationDecisionEntity> CategorizationDecisions` +
  `ApplyConfiguration`; **query-filter не ставится** (журнал глобальный).
- `PrecategorizedCommodityEntity` += `OriginalProposedCategoryId`;
  `PrecategorizedCommodityRepository.TryAddProposalAsync` пишет
  `OriginalProposedCategoryId = (int)category` (единственное место записи);
  `GetByIdAsync` читает колонку в домен; `UpdateProposedCategoryAsync` её **не трогает**.
- DI (`DependencyInjectionExtensions`): `AddScoped<ICategorizationDecisionRepository, CategorizationDecisionRepository>()`.

### Что НЕ входит
- Recorder, валидация draft'ов, нормализация, `TimeProvider` (T-03).
- Методы чтения журнала (`GetCombinationsAsync`/`CountCombinationsAsync`) — T-07.
- Любые вызовы из save-путей.

### Тесты
- Новый `Infrastructure/CategorizationDecisionRepositoryTests.cs` (Testcontainers Postgres, по
  образцу `PrecategorizedCommodityRepositoryTests`): `AddRangeAsync` вставляет пачку (2–3
  события) одним вызовом; значения колонок и `created_at` сохранены; повторный вызов добавляет
  новые строки (дедупа на записи нет — FR-8).
- Новый `Infrastructure/CategorizationDecisionMappingTests.cs` (EF InMemory, по образцу
  `MerchantEntityMappingTests`): маппинг `commodity_name` ↔ `CommodityName`, `suggestion_source`
  ↔ `SuggestionSource` и т.д.; CHECK-констрейнты в InMemory не проверяются — они покрыты
  контейнерным тестом/ручной проверкой схемы.
- Обновить `Infrastructure/PrecategorizedCommodityRepositoryTests.cs`: `TryAddProposalAsync`
  пишет `original_proposed_category_id == proposed_category_id`; `UpdateProposedCategoryAsync`
  исходное значение не меняет.

### Критерии готовности
- `dotnet test` зелёный; Architecture-тесты не нарушены (Domain не тянет EF и т.п.).
- В коде ровно одно место записи `original_proposed_category_id` — фоновая вставка строки очереди.
- `dotnet build` без предупреждений, новых публичных API сверх перечисленного нет.

### Проверка
```bash
cd Analytics && dotnet build && dotnet test
```

---

## T-03. Recorder: контракт, допустимые источники, best-effort запись

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-02

### Цель
Единая точка записи журнала (решение C1): валидирует/фильтрует черновики, нормализует имя,
проставляет серверное время и пишет одним батчем; **никогда не бросает**.

### Что входит
- **Application:** `Modules/CategorizationDecisions/Models/CategorizationDecisionModels.cs` —
  `CategorizationDecisionSources` (wire-имена `ai`, `precategorized`, `cache`,
  `merchant-default`; `All`; проверка принадлежности множеству — точное сравнение `Ordinal`);
  `CategorizationDecisionDraft(CommodityName, SuggestionSource, SuggestedCategory, FinalCategory,
  AcceptedByUserId, AcceptedByAdmin)`.
- **Application:** `Modules/CategorizationDecisions/Contracts/ICategorizationDecisionRecorder.cs` —
  `RecordAsync(IReadOnlyCollection<CategorizationDecisionDraft> drafts, CancellationToken ct)`;
  XML-док фиксирует контракт «не бросает ни при каких обстоятельствах (включая отмену токена)».
- **Infrastructure:** `Modules/CategorizationDecisions/CategorizationDecisionRecorder.cs`:
  1. фильтрация draft'ов по одному: источник ∈ `All`; `Enum.IsDefined` обеих категорий;
     `SuggestedCategory != Undefined`; `FinalCategory` определена (0 допустим); имя непустое и
     `≤ 256` символов после `Trim`; нормализованное имя — `CommodityNameNormalizer.NormalizeName`
     и тоже `≤ 256`; невалидные отбрасываются с `LogWarning` («пропущено N черновиков»,
     без названий/долей);
  2. `CreatedAt = _timeProvider.GetUtcNow()` для каждого события;
  3. пустой после фильтрации набор — выход без обращения к репозиторию (но с `Debug`-логом);
  4. один `AddRangeAsync` + `SaveChangesAsync` внутри репозитория;
  5. `catch (Exception ex)` → `LogError("Журнал категоризации не записан: {Count} событий")`,
     исключение не пробрасывается; `OperationCanceledException` обрабатывается тем же `catch`;
  6. `Information` — число записанных событий; имена товаров — только `Debug`.
- DI: `AddScoped<ICategorizationDecisionRecorder, CategorizationDecisionRecorder>()`.

### Что НЕ входит
- Вызовы из save-путей (T-04…T-06), чтение (T-07), API (T-08).
- Отдельный эндпоинт записи журнала (запрещено решением A4/п. 9).
- Emergency-выключатель / новые настройки.

### Тесты
Новый `CategorizationDecisionRecorderTests.cs` (NSubstitute + `FakeTimeProvider`):
- валидный черновик → одно событие с нормализованным именем, переданным `CreatedAt` и
  `AcceptedBy`/`AcceptedByAdmin`;
- источник `existing`/`undefined`/неизвестная строка → событие не пишется, остальные пишутся;
- `SuggestedCategory = Undefined` / неизвестный код / `FinalCategory` неизвестного кода →
  пропуск;
- пустое имя, имя длиннее 256 → пропуск;
- пустая коллекция и коллекция из одних невалидных → репозиторий не вызывается;
- репозиторий бросает (`InvalidOperationException` и `OperationCanceledException`) → вызов
  `RecordAsync` завершается без исключения;
- пачка из N валидных → ровно один вызов `AddRangeAsync`.

### Критерии готовности
- `dotnet test` зелёный; тесты доказывают «никогда не бросает» и правила фильтрации.
- Нормализация выполняется только через `CommodityNameNormalizer`.
- Никаких исключений наружу из `RecordAsync` (в коде нет `throw`).

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CategorizationDecisionRecorder"
cd Analytics && dotnet test
```

---

## T-04. Точка фиксации 1 — сохранение категорий чека (`PUT /api/receipts/{id}/categories`)

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-03

### Цель
При подтверждении категорий на странице чека фиксировать события по позициям, для которых
клиент прислал валидный снимок предложения (ADR-033 п. 3, FR-4/FR-5).

### Что входит
- **Application:** `ConfirmedCategoryAssignment` += `CommodityCategory? SuggestedCategory`,
  `string? SuggestionSource` (аддитивно, значения по умолчанию `null`);
  `ICommodityCategorizationService.ApplyConfirmedCategoriesAsync` += параметр `bool acceptedByAdmin`
  (ADR-033 п. 3.5: admin-флаг резолвит эндпоинт).
- **Infrastructure (`CommodityCategorizationService`):**
  - после цикла применения (и после `_cache.TryAdd`) собрать drafts по **применённым**
    assignment'ам с непустым снимком: `CommodityName = commodity.Name`,
    `SuggestionSource`, `SuggestedCategory`, `FinalCategory = assignment.Category ?? Undefined`
    (сброс в «Не указана» даёт `final = 0`), `AcceptedByUserId = userId`,
    `AcceptedByAdmin = acceptedByAdmin`;
  - один вызов `ICategorizationDecisionRecorder.RecordAsync` после цикла; исключений от
    recorder'а сервис не ждёт и не обрабатывает (контракт T-03);
  - `updated` и порядок `_cache.TryAdd` не меняются; assignment с чужим `commodityId` draft не
    создаёт.
- **Api (`ReceiptCategorizationEndpoints`):**
  - `SaveCategoryItemRequest` += `string? SuggestedCategory`, `string? SuggestionSource`
    (опциональные, camelCase в JSON);
  - парсинг снимка: оба поля пусты → снимка нет; иначе снимок валиден только если
    `suggestedCategory` непусто, не число, `Enum.TryParse(ignoreCase: true)` + `Enum.IsDefined`
    + `!= Undefined`, а `suggestionSource` ∈ `CategorizationDecisionSources.All`;
    невалидный/частичный снимок → снимок отбрасывается (`LogWarning` с количеством), запрос
    **не** получает `400`;
  - admin-флаг: `IUserRepository.GetByIdAsync(userId)` **один раз за запрос** и только если
    есть хотя бы один валидный снимок (иначе запроса в `users` нет); `user is null` → `false`;
  - существующая валидация `items`/`category` и все коды ответов — без изменений.

### Что НЕ входит
- Страница «Товары» (T-05), `/precategorized` (T-06).
- Изменение ответа `SaveCategoriesResponse` или счётчика `updated`.
- Какие-либо изменения `suggest`.

### Тесты
- `CommodityCategorizationServiceTests.cs` (обновить фабрику под новую сигнатуру; добавить
  `ICategorizationDecisionRecorder` как substitute):
  - валидный снимок + категория оставлена как предложено → событие «согласие» (`final == suggested`);
  - валидный снимок + категория изменена → событие с расхождением;
  - снимка нет → `RecordAsync` не вызывается (FR-5b);
  - сброс категории (`null`) + снимок → событие с `FinalCategory = Undefined`;
  - `commodityId` не принадлежит чеку + снимок → события нет;
  - `acceptedByAdmin` пробрасывается в черновик;
  - recorder бросает → `updated` и результат сервиса не меняются (NFR-3).
- `ReceiptCategorizationEndpointsTests.cs`: аддитивные поля принимаются; невалидный снимок не
  даёт `400` и не мешает сохранению; без снимков `IUserRepository` не вызывается; при валидном
  снимке — ровно один вызов; прежние тесты `400/401/404` без изменений.

### Критерии готовности
- `dotnet test` зелёный; существующие тесты чека меняются только в части фабрики/сигнатуры.
- Ответы и коды `PUT /api/receipts/{id}/categories` побайтово прежние при отсутствии снимков.
- Событий пишется ровно столько, сколько применённых assignment'ов с валидным снимком.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~ReceiptCategorization|FullyQualifiedName~CommodityCategorizationService"
cd Analytics && dotnet test
```

---

## T-05. Точка фиксации 2 — пакетное сохранение страницы «Товары» (`PUT /api/commodities/categories`)

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** T-03 · **Порядок:** после T-04

### Цель
Фиксировать события при пакетном assign-only сохранении: только для реально изменённых строк
(`changed`) с валидным снимком (ADR-033 п. 4).

### Что входит
- **Application:** `CommodityCategoryChange` += `CommodityCategory? SuggestedCategory`,
  `string? SuggestionSource`.
- **Api (`CommodityBatchCategorizationEndpoints`):** `SaveCommodityCategoryItemRequest` += те же
  два опциональных поля; правила парсинга/валидности/игнора — те же, что в T-04 (вынести общий
  helper разбора снимка, чтобы правила не разъехались; при этом `400` за снимок не отдаётся);
  admin-флаг = `true` без запроса в `users` (эндпоинт admin-only).
- **Infrastructure (`CommodityBatchCategorizationService.ApplyAsync`):**
  - после успешного `UpdateCategoriesAsync` и `TryAdd` в кэш собрать drafts по `changed` ∩
    снимок (имя уже есть в `current`), `FinalCategory = byCommodityId[id]` (assign-only, `0`
    невозможен), `AcceptedByAdmin = true`, `AcceptedByUserId = userId`;
  - один вызов recorder'а; `updated`, `changed`-фильтр и `409`-поведение не меняются;
  - `updated == 0` → recorder не вызывается.
- DI: recorder в конструктор сервиса.

### Что НЕ входит
- Одиночный редактор `PUT /api/commodities/{id}/category` — **не** точка фиксации (ADR-033 п. 4).
- Сброс категории этим контрактом (assign-only, C4) — событий с `final = Undefined` не бывает.
- Изменения `suggest` страницы «Товары» и фронтенд (T-10).

### Тесты
- `CommodityBatchCategorizationServiceTests.cs` (обновить фабрику, добавить recorder-substitute):
  - строка в `changed` + снимок → событие; строка вне `changed` (значение не изменилось) +
    снимок → события нет;
  - нет снимка → события нет;
  - `updated == 0` → `RecordAsync` не вызывается;
  - recorder бросает → `updated` и кэш не меняются;
  - `AcceptedByAdmin = true`, источник/категория снимка проброшены.
- `CommodityBatchCategorizationEndpointsTests.cs`: невалидный/частичный снимок не даёт `400`;
  прежние `400/401/403/409` и счётчик `updated` не изменились; «не трогаем» (`null`/пробелы)
  по-прежнему не создаёт событий и не считается изменением.

### Критерии готовности
- `dotnet test` зелёный; поведение и коды `PUT /api/commodities/categories` при отсутствии
  снимков не изменились.
- Число событий = число строк в `changed` с валидным снимком.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityBatchCategorization"
cd Analytics && dotnet test
```

---

## T-06. Точка фиксации 3 — подтверждение в `/precategorized` (решение D1)

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-02, T-03 · **Порядок:** после T-05

### Цель
При подтверждении строки очереди писать событие «предложено = исходное предложение строки,
принято = текущая категория предложения» с принявшим-админом (ADR-033 п. 5).

### Что входит
- **Application:** `IPrecategorizedCommodityService.ConfirmAsync(Guid id, Guid acceptedByUserId,
  CancellationToken)`; XML-док фиксирует, что `acceptedByUserId` — из `UserContext`.
- **Infrastructure (`PrecategorizedCommodityService`):**
  - после успешного `Applied` (и после `_cache.TryAdd`) вызвать recorder с одним черновиком:
    `CommodityName = result.Name` (точное имя строки очереди), `SuggestionSource =
    CategorizationDecisionSources.Precategorized`, `SuggestedCategory =
    proposal.OriginalProposedCategory` (читается до транзакции; колонка неизменяема),
    `FinalCategory = proposal.ProposedCategory` (значение, которое реально применено),
    `AcceptedByUserId = acceptedByUserId`, `AcceptedByAdmin = true`;
  - `Not Found`/`Conflict` → событий нет; отклонение (`DeclineAsync`) → событий нет;
  - `updatedCommodities = 0` → событие **всё равно** пишется (решение состоялось);
  - ответ эндпоинта и число `updatedCommodities` не зависят от журнала.
- **Api (`PrecategorizedCommodityEndpoints.Confirm`):** передавать `UserContext.UserId!.Value`
  (после `AdminAuthorization` пользователь гарантированно есть).

### Что НЕ входит
- Массовый дожим мерчант-дефолтом и автоприменение при импорте — не точки фиксации (NFR-1).
- Изменение транзакции/статусов confirm (ADR-027) и `PUT .../category` (он колонку не трогает —
  гарантия из T-02).
- Маппинг колонки `original_proposed_category_id` (уже в T-02).

### Тесты
- `PrecategorizedCommodityServiceTests.cs`: `Applied` → один черновик с `suggested = original`,
  `final = current`, источник `precategorized`, admin `true`; `NotFound`/`Conflict` → recorder не
  вызывается; recorder бросает → результат сервиса не меняется.
- `Infrastructure/PrecategorizedCommodityWorkflowTests.cs` (Testcontainers, по образцу текущего
  кейса confirm): «PUT предложенной категории → confirm» даёт событие `suggested = исходная`,
  `final = изменённая`; «confirm без PUT» → `suggested == final`; «confirm при 0 обновлённых
  товарах» → событие есть.
- `PrecategorizedCommodityEndpointsTests.cs`: `Confirm` передаёт `UserContext.UserId`.

### Критерии готовности
- `dotnet test` зелёный; `POST /api/precategorized-commodities/{id}/confirm` отвечает прежним
  телом и кодами.
- Для источника `precategorized` фильтр «только расхождения» становится непустым после правки
  предложения админом.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"
cd Analytics && dotnet test
```

---

## T-07. Чтение: read-service, дедуп anti-join, фильтры, `Total`

**Приоритет:** P0 · **Оценка:** 1.25 чел.-дн. · **Зависит от:** T-02 · **Порядок:** после T-06

### Цель
Реализовать серверную выборку уникальных сочетаний с фильтрами, пагинацией и `Total`
(решение E1, FR-7), с безопасным отображением категорий и именем принявшего из `users`.

### Что входит
- **Application:**
  - `Modules/CategorizationDecisions/Models/CategorizationDecisionModels.cs` +=
    `CategorizationDecisionQuery(bool OnlyMismatch, string? Source, int? SuggestedCategoryId,
    int? FinalCategoryId, string? Search, DateTimeOffset? From, DateTimeOffset? To, int Limit,
    int Offset)` и `CategorizationDecisionRow` (`CommodityName`, `NormalizedName`,
    `SuggestionSource`, `SuggestedCategoryId/Name`, `FinalCategoryId/Name`, `AcceptedById`,
    `AcceptedByName?`, `AcceptedByAdmin`, `DecidedAt`);
  - `Contracts/ICategorizationDecisionReadService.cs` — `GetPageAsync(query, ct)` →
    `(IReadOnlyCollection<CategorizationDecisionRow> Items, int Total)`.
- **Domain:** `ICategorizationDecisionRepository` += `GetCombinationsAsync(query)` и
  `CountCombinationsAsync(query)` (аддитивное расширение интерфейса из T-02).
- **Infrastructure:**
  - `CategorizationDecisionRepository` — фильтры к событиям (в этом порядке):
    `onlyMismatch` (`suggested != final`), `source`, `suggestedCategoryId`, `finalCategoryId`,
    `search` — `EF.Functions.ILike` по `commodity_name` **или** `normalized_name` (шаблон
    `%term%`, `Trim`), `from` (`created_at >= from`), `to` (`created_at < to`);
  - дедуп anti-join: событие попадает в выдачу, только если **нет более свежего события того же
    сочетания** `(commodity_name, suggestion_source, suggested_category_id, final_category_id)`;
    порядок `created_at DESC, id DESC`; `Skip/Take`; `Total` = `Count()` того же anti-join;
  - `CategorizationDecisionReadService` — маппинг кодов в отображаемые имена
    (`Enum.IsDefined` + `CommodityCategoryHelper.GetDisplayName`; неизвестный код — имя «—»,
    при этом числовой код в ответе остаётся), `AcceptedByName` — `null`, если пользователь
    удалён или имя пустое.
- DI: `AddScoped<ICategorizationDecisionReadService, CategorizationDecisionReadService>()`.

### Что НЕ входит
- Эндпоинт и HTTP-валидация (T-08), фронтенд (T-11).
- Счётчики/группировки/агрегаты (FR-7), экспорт, ретеншен (ADR-033 «Открытые вопросы»).
- Индексы «по замерам» (открытый вопрос 2) — только три индекса из T-01.

### Тесты
- Новый `CategorizationDecisionReadServiceTests.cs` (EF InMemory — LINQ переносим):
  - два одинаковых сочетания → одна строка, «кто принял»/`DecidedAt` от последнего события;
  - `total` = число уникальных сочетаний, а не число событий;
  - `onlyMismatch` отсекает согласия; `source`/`suggestedCategoryId`/`finalCategoryId` работают;
  - `from`/`to`: границы `>= from`, `< to`; период суживает «последнее».
- `Infrastructure/CategorizationDecisionRepositoryTests.cs` (Testcontainers — там, где нужен
  `ILike`/порядок): поиск по `commodity_name` и `normalized_name` без учёта регистра; порядок
  `decidedAt DESC, id DESC`; `X`-независимость пагинации; `Total` совпадает с числом строк
  выдачи при `limit` ≥ общего числа.
- Тест «имя недоступно»: события с `accepted_by` без строки в `users` → `AcceptedByName = null`,
  `AcceptedById` и `AcceptedByAdmin` заполнены.
- Тест «неизвестный код категории» → имя «—», числовой код в строке сохранён.

### Критерии готовности
- `dotnet test` зелёный; дедуп выполняется на уровне EF-запроса (не в памяти приложения после
  выборки).
- `Total` в сервисе соответствует числу уникальных сочетаний под фильтром.

### Известный риск (проверить в этой задаче)
Tie-break «свежести» по `(created_at, id)` требует сравнения `Guid`; трансляция
`Guid.CompareTo` в Npgsql EF не гарантирована (прецедент-предупреждение ADR-027: сравнение
`Guid` в EF-трансляции ненадёжно). Если выражение не транслируется или даёт неверный план —
**остановка и эскалация к @software-architect** (запасные варианты зафиксированы в ADR-033:
E2 `DISTINCT ON` либо иная детерминированная tie-break-семантика). Самостоятельно raw SQL или
монотонные `created_at` не вводятся (общий инвариант 5).

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CategorizationDecision"
cd Analytics && dotnet test
```

---

## T-08. API: `GET /api/categorization-decisions` (admin-only)

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** T-07

### Цель
Отдать администратору страницу журнала (FR-6/FR-7) в стиле админских эндпоинтов Analytics.

### Что входит
- **Api:** `Modules/CategorizationDecisions/CategorizationDecisionEndpoints.cs` —
  `MapCategorizationDecisionEndpoints()`; `GET /api/categorization-decisions`.
  - права: `AdminAuthorization.AuthorizeAsync` (нет пользователя → `401`, не админ → явный
    `Results.StatusCode(403)`); ошибки — текстовые тела, без ProblemDetails;
  - параметры (camelCase): `onlyMismatch` (bool, default `false`), `source` (wire-имя),
    `suggestedCategory` (`int?`), `finalCategory` (`int?`), `search`, `from`/`to` (строки,
    `DateTimeOffset.TryParse` c `CultureInfo.InvariantCulture` и `DateTimeStyles.RoundtripKind`),
    `limit` (default 50, `1..200`), `offset` (`>= 0`);
  - валидация: `source` вне `CategorizationDecisionSources.All` → `400`; `suggestedCategory`
    `!Enum.IsDefined || == 0` → `400`; `finalCategory` `!Enum.IsDefined` (значение `0`
    допустимо) → `400`; непарсимые `from`/`to` → `400`; `limit`/`offset` вне диапазона → `400`;
    дополнительных правил (например, `from > to`) не вводится — выдача просто пустая;
  - ответ: `200` массивом строк, `X-Total-Count` = `Total` (число уникальных сочетаний);
    JSON-поля — как в ADR-033 п. 7 (`commodityName`, `normalizedName`, `suggestionSource`,
    `suggestedCategoryId`, `suggestedCategoryName`, `finalCategoryId`, `finalCategoryName`,
    `acceptedById`, `acceptedByName`, `acceptedByAdmin`, `decidedAt`).
- `Program.cs`: `app.MapCategorizationDecisionEndpoints();`.

### Что НЕ входит
- Любой write-эндпоинт журнала (запрещён п. 9 ADR-033).
- Опции/настройки, кэширование ответа.
- Изменения существующих эндпоинтов.

### Тесты
Новый `CategorizationDecisionEndpointsTests.cs` (исполнение `IResult` на `DefaultHttpContext`,
прецедент `PrecategorizedCommodityEndpointsTests`):
- `401` без пользователя, `403` не-админу (сервис не вызывается);
- `400`: неизвестный `source`, `suggestedCategory = 0`/неизвестный код, `finalCategory`
  неизвестный код, непарсимые даты, `limit = 0`/`201`, `offset = -1`;
- `200` + `X-Total-Count` из `Total`; пустая выдача → `200` и `0`;
- `finalCategory = 0` допустим;
- параметры по умолчанию (`limit = 50`) доходят до сервиса.

### Критерии готовности
- `dotnet test` зелёный; `MapCategorizationDecisionEndpoints` зарегистрирован в `Program.cs`.
- Не-администратор не получает данных ни при каких параметрах (проверено тестом).

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CategorizationDecisionEndpoints"
cd Analytics && dotnet test
```

---

## T-09. Frontend: снимок предложения при сохранении чека

**Приоритет:** P0 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** T-04 · **Порядок:** после T-08

### Цель
Отправлять в `PUT /api/receipts/{id}/categories` аддитивный снимок того, что человек видел на
экране (решение A1, ADR-033 п. 3), не меняя интерфейс и поведение сценария.

### Что входит
- `frontend/src/api/receipts.ts`: `SaveCategoriesItem` += `suggestedCategory?: string` (ключ
  enum, как `category`) и `suggestionSource?: string` (wire-имя).
- `frontend/src/components/ReceiptDetails.tsx`, `saveSelectedCategories`:
  - по каждой позиции взять `suggestions` (уже в состоянии) и сформировать снимок **только**
    если `source ∈ {'ai','precategorized','cache','merchant-default'}` и `categoryId != null`,
    а `categoryId` маппится в `key` через существующий справочник `categories`;
  - поля снимка добавляются к элементу поверх `commodityId`/`category`; при сбросе категории
    (`selectedCategories[item.id]` пусто) снимок всё равно уходит (сервер запишет `Undefined`);
  - при отсутствии `suggestions` payload побайтово прежний (никаких новых ключей).
- Никаких изменений разметки, кнопок, тостов, режима `categorizeModeActive`.

### Что НЕ входит
- Страница «Товары» (T-10), страница журнала (T-11).
- Изменение контракта `suggest`, `SaveCategoriesResult`, текстов тостов.
- Клиентская валидация сверх «есть валидный источник и категория» (сервер валидирует форму).

### Тесты
- Автотестов фронта в проекте нет (ADR п. 8): `npm run lint && npm run build` + ручная проверка.
- Ручная проверка: suggest на чеке → сохранить без правок → в `categorization_decisions`
  появились согласия по позициям с предложением; сохранить после сброса одной позиции →
  событие с `final = 0`; suggest недоступен (пустой `AI:BaseUrl` → `503`) → сохранение работает
  как раньше и событий не создаёт.
- В DevTools проверить, что новый payload не появляется, если suggest не запускался.

### Критерии готовности
- `npm run lint` и `npm run build` зелёные; визуально страница чека не изменилась.
- Запрос сохранения без предложений идентичен прежнему по составу полей.

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
# ручной сценарий: suggest → save → проверка строк в categorization_decisions
```

---

## T-10. Frontend: снимок предложения в черновике страницы «Товары»

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** T-05 · **Порядок:** после T-09

### Цель
Хранить в черновике страницы «Товары» несменяемый снимок предложения и отправлять его в `PUT`
для изменённых строк (ADR-033 п. 4/п. 8).

### Что входит
- `frontend/src/hooks/useCommodityPageCategorization.ts`:
  - `DraftEntry` += `suggested?: { value: string; source: string }` (ключ категории + wire-имя
    источника);
  - `applySuggestionItems`: снимок заполняется **только** при `item.categoryId != null` и
    `source ∈ {'ai','precategorized','cache','merchant-default'}`; для `existing`/`undefined`
    снимка нет;
  - `setValue` (ручная правка) и `markChunkFailed` снимок **не меняют и не очищают**;
  - `start` для уже категоризированных строк снимка не создаёт (`source = existing`);
  - `save`: для `changed`-строк добавлять `suggestedCategory: entry.suggested.value` и
    `suggestionSource: entry.suggested.source`; фильтр `changed` и `value !== ''` не меняется.
- `frontend/src/api/commodities.ts`: тип элемента `saveCommodityCategories` (сейчас inline) +=
  те же опциональные поля; сигнатура вызова не меняется.
- UI (подписи `getDraftNote`, заголовки, тосты, прогресс) — без изменений.

### Что НЕ входит
- Отправка снимка для строк вне `changed` (assign-only семантика и `updated` не меняются).
- Подсказки/бейджи о журнале в UI.
- Изменение порционности suggest и дедупликации названий.

### Тесты
- Автотестов фронта нет: `npm run lint && npm run build` + ручная проверка.
- Ручная проверка: строка без категории получила предложение и сохранена без правки →
  событие «согласие»; правка после предложения → событие с расхождением; ручная установка
  категории без suggest (`retry` недоступен/ошибка порции) → события нет; строка вне `changed`
  → события нет; устаревшая страница (`409`) → черновик сохраняется, событий нет.

### Критерии готовности
- `npm run lint`, `npm run build` зелёные; поведение страницы и тосты прежние.
- События появляются ровно по строкам `changed` с валидным снимком.

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
# ручной сценарий на странице «Товары»: suggest → save → строки в categorization_decisions
```

---

## T-11. Frontend: страница журнала `/categorization-decisions`

**Приоритет:** P1 · **Оценка:** 1.5 чел.-дн. · **Зависит от:** T-08 · **Порядок:** после T-10

### Цель
Дать администратору страницу просмотра журнала с фильтрами и пагинацией (FR-6/FR-7),
по образцу `/precategorized`.

### Что входит
- `routes.ts`: `categorizationDecisionsPath()` → `/categorization-decisions`.
- `App.tsx`: маршрут `categorizationDecisionsPath()` внутри `Layout`.
- `Sidebar.tsx`: пункт «Журнал категоризации» под `{isAdmin && ...}` (стиль `/precategorized`).
- `types/categorizationDecision.ts` — строка ответа и параметры фильтра.
- `api/categorizationDecisions.ts` — `fetchCategorizationDecisions` с фильтрами и `X-Total-Count`
  (fallback `offset + items.length`, как в `precategorizedCommodities.ts`), понятные тексты
  `401/403`.
- `hooks/useCategorizationDecisions.ts` — по образцу `usePrecategorizedCommodities`
  (AbortController, debounce поиска, `refresh`, пагинация).
- `components/CategorizationDecisionsPage.tsx` — admin-гейт как в `PrecategorizedCommoditiesPage`
  (`useAdmin`: загрузка → «Доступ запрещён» без запросов), панель фильтров: поиск, checkbox
  «Только расхождения», селектор источника, селекторы предложенной и итоговой категории
  (справочник `GET /api/commodities/categories` + «Не указана» для итоговой), диапазон дат.
- `components/CategorizationDecisionsTable.tsx` — колонки: исходное название, нормализованное,
  предложено (категория + подпись источника через `utils/categorizationSource.ts`), принято,
  кто принял (имя или пусто + бейдж «админ»), дата; `Pagination` + `X-Total-Count`.
- Сброс фильтров на первую страницу при изменении любого фильтра.

### Что НЕ входит
- Счётчики, группировки, проценты, графики, экспорт (FR-7, «Вне объёма»).
- Редактирование/удаление событий, кнопки действий.
- Новые эндпоинты; скрытие страницы как «защита» (сервер уже отвечает `403`).

### Тесты
- Автотестов фронта нет: `npm run lint && npm run build` + ручная приёмка.
- Ручная приёмка: не-админ не видит пункт меню и при прямом заходе получает «Доступ запрещён»
  без сетевых запросов; админ видит страницу; каждый фильтр и их комбинации дают ожидаемую
  выдачу; переключение «только расхождения»; пагинация и `X-Total-Count` согласованы; пустой
  результат — корректное состояние; имена категорий совпадают со справочником; у события без
  пользователя имя пустое, бейдж «админ» есть.

### Критерии готовности
- `npm run lint`, `npm run build` зелёные; ручная приёмка FR-6/FR-7 пройдена.
- Никаких новых настроек и зависимостей фронтенда.

### Проверка
```bash
cd Analytics/frontend && npm run lint && npm run build
```

---

## T-12. Финальная проверка контрактов и регрессии

**Приоритет:** P0 · **Оценка:** 0.5–1.0 чел.-дн. · **Зависит от:** T-01…T-11

### Цель
Подтвердить, что сквозной сценарий работает, а существующие контракты и пользовательский поток
не изменились (NFR-1/2/3), и что реализация соответствует ADR-033 и amend-заметкам в ADR-009,
027, 028.

### Что входит
- `cd Analytics && dotnet test` — все проекты, включая `Architecture/ProjectDependencyTests`.
- `cd Analytics/frontend && npm run lint && npm run build`.
- Ручная проверка сквозных сценариев из задачи:
  1. чек: suggest → сохранение без правок (согласия) и с правками (расхождения), сброс в
     «Не указана» → событие с `final = 0`;
  2. «Товары»: пакетный suggest → сохранение → события только по `changed`;
  3. `/precategorized`: правка предложения → confirm → событие `suggested = исходное`,
     `final = изменённое`; confirm без правки → согласие; отклонение → события нет;
  4. журнал: фильтр «только расхождения», источник, обе категории, поиск, период, пагинация,
     «кто принял» от последнего события, одинаковые сочетания схлопнуты;
  5. не-админ не имеет доступа к странице и API (`403`).
- Проверка негативных сценариев NFR-2/NFR-3:
  - `suggest` чека при пустом `AI:BaseUrl` по-прежнему `503`; пакетный suggest «Товаров» —
    `200` + `aiAvailable = false`;
  - `PUT` чеков/«Товаров» при отсутствии снимков отвечают прежними телами и кодами
    (`400/401/404/409` включительно), `updated` не изменился;
  - сохранение категории успешно при недоступной записи журнала (проверить отзывом `INSERT`
    или остановкой БД-теста; ожидаемо — успешный ответ и `Error` в логах);
  - отсутствие снимка (ручная установка категории, одиночный редактор
    `PUT /api/commodities/{id}/category`) событий не создаёт; автоприменений нет.
- Сверка с ADR-033 «Явно зафиксированные семантики» и с amend-заметками ADR-009/027/028.
- Проверка отсутствия изменений вне Analytics: `git status` — нет правок в `backend/`,
  `bot/`, `docker-compose*.yml`, `.env*`, `AGENTS.md`; новых настроек нет.
- Итоговая сводка — в `docs/reviews/categorization-decision-log/`.

### Что НЕ входит
- Новые функции (ретеншен, экспорт, агрегаты, память «имя → категория»).
- «Замеры планов» anti-join на реальных объёмах как отдельное решение (ADR-033, открытые
  вопросы 1–2): фиксируется как эксплуатационная задача, не блокирует приёмку.
- Правки архитектуры на ходу: найденное несоответствие ADR — эскалация к @software-architect,
  а не самостоятельное решение.

### Тесты
- Только прогон существующих наборов; новых тестов задача не добавляет. Найденные дефекты
  исправляются в рамках соответствующей задачи (T-01…T-11) отдельным циклом, а не здесь.

### Критерии готовности
- `dotnet test` (Analytics, все проекты) зелёный; `npm run lint`/`npm run build` зелёные.
- Все пункты «Критерии приёмки» задачи отмечены и подтверждены ручной проверкой.
- Сводка с результатами и списком остаточных рисков лежит в `docs/reviews/categorization-decision-log/`.

### Проверка
```bash
cd Analytics && dotnet test
cd Analytics/frontend && npm run lint && npm run build
git status --short
```

---

## Трассировка требований → задачи

| Требование | Задачи |
|---|---|
| FR-1 (поля события) | T-01 (схема), T-02 (домен/хранение), T-03 (валидация/нормализация), T-08 (имена/принявший в выдаче) |
| FR-2 (все 4 источника) | T-03 (`CategorizationDecisionSources`), T-04/T-05 (снимок), T-06 (`precategorized`) |
| FR-3 (согласия и исправления) | T-04/T-05/T-06 (запись при любом подтверждении), T-07/T-08 (`onlyMismatch`), T-06 (D1: proposed = original) |
| FR-4 (три точки фиксации) | T-04, T-05, T-06 |
| FR-5 (а/б: нет сохранения — нет события; нет предложения — нет события) | T-04/T-05 (снимок только из save-путей; `existing`/`undefined` без снимка), T-03 (фильтрация) |
| FR-6 (admin-only, колонки, фильтры, пагинация) | T-08 (API), T-11 (страница) |
| FR-7 (уникальные сочетания, «кто принял» от последнего, без счётчиков) | T-07, T-08, T-11 |
| FR-8 (сырое хранение, дедуп только на чтении) | T-01, T-02, T-07 |
| NFR-1 (нет автопроставления) | T-04/T-05/T-06 (только запись журнала, поведение не меняется), T-12 |
| NFR-2 (аддитивные контракты, UI обычных пользователей) | T-04/T-05 (опциональные поля), T-09/T-10 (payload без UI-изменений), T-12 |
| NFR-3 (best-effort, после commit, не ломает сохранение) | T-03 (никогда не бросает), T-04/T-05/T-06 (порядок вызова), T-12 |
| Тестируемость (п. 10 ADR) | T-02…T-08 (unit/InMemory/Testcontainers), T-09…T-11 (lint/build + ручная) |

## Риски и точки эскалации

| Риск | Где | Действие |
|---|---|---|
| `SET NOT NULL` на `original_proposed_category_id` до появления кода записи | T-01 → T-02 | Миграция на dev применяется после T-02 (или при выключенном фоне); задачи идут подряд |
| Трансляция `Guid.CompareTo` в anti-join (tie-break) | T-07 | Проверить на Postgres-контейнере; при невозможности — эскалация к @software-architect (E2 `DISTINCT ON` / иная tie-break-семантика) |
| Производительность anti-join на росте таблицы | T-07, T-12 | Три индекса из T-01; замеры — по эксплуатации (открытый вопрос 2 ADR), не блокируют приёмку |
| `EF.Functions.ILike` не поддерживается EF InMemory | T-07 | Тесты поиска — на Testcontainers Postgres (прецедент `PrecategorizedCommodityRepositoryTests`) |
| Снимок от клиента можно исказить | T-04/T-05/T-09/T-10 | Только серверные save-пути, валидация формы, пропуск невалидного без ошибки (A1, ADR п. 9) |
| Изменение сигнатур сервисов ломает существующие тесты | T-04, T-06 | Правки тестов только в части фабрик/вызовов, утверждения не меняются; перечислено в задачах |
| Отсутствие автотестов фронтенда | T-09…T-11 | `npm run lint`/`build` + ручные сценарии (ADR п. 8); регрессия контрактов — T-12 |
