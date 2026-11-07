# Задача 02 - Домен и persistence журнала + маппинг `original_proposed_category_id`

- **Задача**: docs/plans/categorization-decision-log.md#t-02-домен-и-persistence-журнала--маппинг-original_proposed_category_id
- **Дата**: 2026-11-07
- **Ревью**: раундов - 1, вердикт APPROVED (блокеров нет)

## Что сделано

Появился доменный слой журнала «предложено → принято»: событие `CategorizationDecision` и
минимальный контракт репозитория с одной операцией `AddRangeAsync`. В Infrastructure добавлены
EF-сущность, конфигурация и репозиторий журнала, которые пишут пачку событий одним
`SaveChangesAsync`; схема EF точно повторяет миграцию T-01 (колонки, типы, NOT NULL,
`DEFAULT now()`, оба CHECK, три индекса — составной с `created_at DESC, id DESC`, индекс
порядка и частичный индекс «расхождения»). Очередь предкатегоризации получила неизменяемое
исходное предложение: колонка `original_proposed_category_id` маппится в домен, пишется только
фоновой вставкой `TryAddProposalAsync`, читается в `GetByIdAsync`/`GetPageAsync` и не меняется
при `UpdateProposedCategoryAsync`. Запись журнала из save-путей, recorder и чтение — следующие
задачи (T-03/T-04…T-08); в этой задаче их нет.

## Изменённые файлы

**Domain**
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/CategorizationDecisions/CategorizationDecision.cs` - новый record события журнала (все поля FR-1).
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/CategorizationDecisions/ICategorizationDecisionRepository.cs` - новый контракт, на этом шаге только `AddRangeAsync`.
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/PrecategorizedCommodity.cs` - свойство `OriginalProposedCategory` и параметр конструктора.

**Infrastructure**
- `.../Persistence/Postgres/CategorizationDecisionEntity.cs` - новая EF-сущность журнала.
- `.../Persistence/Postgres/Configurations/CategorizationDecisionConfiguration.cs` - таблица `categorization_decisions`, колонки/длины/NOT NULL/`DEFAULT now()`, оба CHECK, три индекса (включая частичный и DESC-порядок).
- `.../Persistence/Postgres/CategorizationDecisionRepository.cs` - `AddRangeAsync` одним `SaveChangesAsync`, без транзакции и валидации.
- `.../Persistence/Postgres/ReceiptDbContext.cs` - `DbSet<CategorizationDecisionEntity>` и `ApplyConfiguration`; query-filter не ставится.
- `.../Persistence/Postgres/PrecategorizedCommodityEntity.cs` - `OriginalProposedCategoryId`.
- `.../Persistence/Postgres/Configurations/PrecategorizedCommodityConfiguration.cs` - колонка `original_proposed_category_id` как required.
- `.../Persistence/Postgres/PrecategorizedCommodityRepository.cs` - запись исходного предложения в `TryAddProposalAsync`, чтение в `GetByIdAsync`/`GetPageAsync`, `UpdateProposedCategoryAsync` колонку не трогает; именованные аргументы для двух подряд идущих категорий.
- `.../Configuration/DependencyInjectionExtensions.cs` - `AddScoped<ICategorizationDecisionRepository, CategorizationDecisionRepository>()`.

**Тесты**
- `Analytics/tests/.../Infrastructure/CategorizationDecisionRepositoryTests.cs` - новый (Testcontainers Postgres): батч-вставка с проверкой всех колонок, `created_at` и round-trip `Id`; отсутствие дедупа на записи; наличие обоих CHECK и трёх индексов (DESC, частичный фильтр); `original_proposed_category_id NOT NULL`; отказ по `SQLSTATE 23514` с именем констрейнта `ck_categorization_decisions_suggested_not_undefined`.
- `Analytics/tests/.../Infrastructure/CategorizationDecisionMappingTests.cs` - новый: domain → entity, имена/длины/типы колонок, CHECK, индексы и required-маппинг `original_proposed_category_id`.
- `Analytics/tests/.../Infrastructure/CategorizationDecisionRepositoryRegistrationTests.cs` - новый wiring-тест DI-регистрации (scoped + реальный resolve).
- `Analytics/tests/.../Infrastructure/PrecategorizedCommodityRepositoryTests.cs` - фабрика заполняет исходное предложение; добавлены проверки «фоновая вставка пишет original = proposed» и «PUT не меняет original».
- `Analytics/tests/.../PrecategorizedCommodityServiceTests.cs` - только фабрики/вызовы под новую сигнатуру конструктора, существующие утверждения не менялись.

**Docs**
- `docs/plans/categorization-decision-log.md` - статус T-02 (`pending` → `in-progress` → `review` → `approved`).
- `docs/reviews/categorization-decision-log/02-domain-persistence.md` - эта сводка.

## Как проверено

- `cd Analytics && dotnet build` - Build succeeded, 0 errors, 30 warnings (только существующие NU1902/NU1903 о пакетах, были до задачи).
- `cd Analytics && dotnet test` - Passed: 451, Failed: 0, Skipped: 0 (59 s); +9 тестов к базе T-01.
- `dotnet test --filter "FullyQualifiedName~CategorizationDecision|FullyQualifiedName~PrecategorizedCommodityRepositoryTests|FullyQualifiedName~PrecategorizedCommodityServiceTests"` - 29 passed, 0 failed.
- Ревьюер независимо прогнал `dotnet build` (0 errors) и `dotnet test` (451 passed / 0 failed) и подтвердил соответствие EF-схемы миграции T-01/ADR, семантику `original_proposed_category_id` и рамки задачи.

## Итоги ревью

- Исправлено по замечаниям ревьюера (suggestions, не блокировавшие вердикт):
  - CHECK-тест теперь проверяет `PostgresException.SqlState == 23514` и `ConstraintName` целевого констрейнта вместо общего `DbUpdateException`;
  - батч-тест дополнительно проверяет round-trip `Id` событий;
  - в `PrecategorizedCommodityRepository` для двух соседних `CommodityCategory` использованы именованные аргументы;
  - дублирование имени индекса в `HasIndex(..., name)` и `.HasDatabaseName(...)` оставлено осознанно и пояснено комментарием: `UseSnakeCaseNamingConvention` генерирует имя в БД из таблицы и колонок, игнорируя имя модели, поэтому для двух индексов на `created_at` нужны и имя модели, и явное имя в БД (иначе второй индекс получает `..._created_at1`).
- Принятые решения:
  - **Дрейф SQL ↔ EF до T-12.** Testcontainers-тест, применяющий `Scripts/*.sql` и сверяющий результат с EF-моделью, сознательно переносится в T-12: в проекте нет такого паттерна, тест потребовал бы поиска файлов в исходном дереве и прогона всей цепочки миграций, что расширяет рамки T-02. Вместо этого добавлен контейнерный тест, проверяющий CHECK/индексы/not-null созданной EF-схемы, и metadata-тест соответствия миграции.
  - **`CategorizationDecisionRepositoryRegistrationTests`** - осознанное полезное дополнение сверх перечня плана: потребитель контракта появляется только в T-03, поэтому потеря DI-регистрации иначе обнаружилась бы только в production.
  - **Имя PK.** EF-конвенция создаёт `pk_categorization_decisions`, SQL-миграция оставляет автогенерируемое `categorization_decisions_pkey`. Это общпроектный стиль (так же у остальных таблиц), семантически не значимо; имена CHECK и всех трёх индексов совпадают дословно.
  - **`GetPageAsync` тоже читает `original_proposed_category_id`** (не только `GetByIdAsync`), потому что доменная сущность теперь содержит non-null свойство. DTO страницы очереди не изменился, поведение API не затронуто.
  - **`original_proposed_category_id`** пишется ровно в одном месте - `TryAddProposalAsync`; `UpdateProposedCategoryAsync` его не трогает, что закреплено тестом.

## Заметки для коммита

- Рекомендуемое сообщение коммита: `categorization decision log domain and persistence`
  (в стиле T-01 `categorization decision log migration`; при желании - `t-02 of categorization decision log`).
- **Гигиена индекса.** Файл
  `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CategorizationDecisionRepositoryRegistrationTests.cs`
  сейчас **untracked** (`??`), остальные новые файлы T-02 уже в индексе. При коммите его нужно
  добавить, иначе тест потеряется. Не включать посторонние изменения:
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`,
  `appsettings.Development.json`, `wwwroot/index.html`.
- На что обратить внимание при ручной проверке:
  1. `dotnet test` зелёный; в контейнерных тестах схема `categorization_decisions` содержит оба CHECK и все три индекса (включая `WHERE suggested_category_id <> final_category_id` и `created_at DESC, id DESC`).
  2. В коде ровно одно место записи `original_proposed_category_id` - фоновая вставка строки очереди; `PUT /api/precategorized-commodities/{id}/category` исходное значение не меняет.
  3. Миграцию T-01 на dev/prod применять после выката T-02 (либо при выключенном фоне `Precategorization__Enabled=false`) - ограничение из T-01 сохраняется.
  4. Записей журнала из пользовательских save-путей в этом коммите ещё нет: это T-03…T-06, поведение API не менялось.
