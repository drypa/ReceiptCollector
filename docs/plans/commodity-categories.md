# Декомпозиция: категории товаров — админские boost/exclude-списки ключевых слов

> Источники: задача [commodity-categories](../tasks/commodity-categories.md)
> (FR-1…FR-10, NFR-1…NFR-4, UC-1…UC-5, критерии приёмки, edge cases) и
> [ADR-035](../adr/035-commodity-category-keywords.md) (решения A1, B1, C1, D1, E1, F1, G1, H1;
> схема и миграция — п. 1; сид — п. 2; карта слоёв — п. 3; сборка подсказок — п. 4;
> формат промта — п. 5; кэш — п. 6; контракты API — п. 7; фронтенд — п. 8;
> безопасность — п. 9; тестируемость — п. 10; матрица требований).
>
> **Этот документ — только декомпозиция.** Новых архитектурных решений он не содержит и не
> заменяет ADR-035. Спорные места эскалируются к @software-architect и исполнителем
> самостоятельно не решаются. Реализация — строго по одной задаче за цикл
> (skill `task-review-gate`): реализация → AI-ревью → ручное ревью → коммит человеком.
>
> Изменения — Analytics (.NET: Domain / Application / Infrastructure / Api / Migrations / тесты)
> и `Analytics/frontend`. Go-сервисы (`backend/`, `bot/`), MongoDB, nginx, `docker-compose.yml`,
> `.env.example`, `AGENTS.md`, `categorization_decisions` — **не трогаются** (ADR-035, «Вне области»).

## Ход выполнения

| № | Задача | Slug | Зависит от | Сложность | Статус |
| -- | ------ | ---- | ---------- | --------- | ------ |
| 01 | Схема: SQL-миграция с сидом, EF-модель, DbSet | `01-schema-migration-seed` | — | M (1.5) | committed |
| 02 | Домен: тип списка, правила слова, матчер, контракт репозитория | `02-domain-keywords-matcher` | 01 (мягкая) | M (1.0) | committed |
| 03 | Хранение: реализация репозитория, кэш снимка, hosted-прогрев | `03-storage-cache-warmup` | 01, 02 | M (1.5) | committed |
| 04 | Сервис ключевых слов + админский API | `04-service-admin-api` | 03 | M (1.5) | committed |
| 05 | Доставка подсказок: провайдер, смена сигнатуры `IAiClient`, resolver и фон | `05-hints-pipeline` | 02, 03 | M (1.0) | committed |
| 06 | Промт: удаление хардкода, блок подсказок, замена теста хардкода | `06-prompt-hints-block` | 01, 05 | M (1.0) | committed |
| 07 | Фронтенд: раздел «Категории товаров» | `07-frontend-page` | 04 | M (1.5) | approved |
| 08 | Amend-заметки ADR 009/022/027/028 и сквозная ручная приёмка | `08-adr-notes-acceptance` | 01…07 | S (0.5) | pending |

Строки таблицы задают единственный допустимый порядок исполнения: задача N стартует только после
того, как задача N-1 проверена человеком и **закоммичена** (протокол skill `task-review-gate`).
Параллельного выполнения нет. Значения `in-progress` / `review` / `approved` / `committed`
выставляют исполнитель и `project-manager`; при старте все задачи — `pending`.

Сводка по каждой задаче пишется разработчиком после вердикта APPROVED в
`docs/reviews/commodity-categories/<NN>-<slug>.md`, где `<NN>-<slug>` — ровно значения колонок
«№» и «Slug» этой таблицы (например, `docs/reviews/commodity-categories/06-prompt-hints-block.md`).

## Обозначения

| Поле | Значение |
|---|---|
| Приоритет | **P0** — без этого нет сквозного результата; **P1** — требуется критериями приёмки, но не блокирует предыдущие |
| Сложность | S ≈ 0.5, M ≈ 1.0–1.5 чел.-дн. одного разработчика, без учёта ревью и ручной приёмки |
| Зависит от | задачи, которые должны быть **закоммичены** до старта; «(мягкая)» — зависимость по семантике (имена/лимиты), а не по компиляции |
| Проверка | конкретные команды и/или ручные сценарии |

Общие команды (для задач 01–06 и 08):

```bash
cd Analytics && dotnet build
cd Analytics && dotnet test
cd Analytics && dotnet test --filter "FullyQualifiedName~<Fragment>"
```

Для задачи 07:

```bash
cd Analytics/frontend && npm run build
cd Analytics/frontend && npm run lint
```

## Сводка и критический путь

| № | Задача | Слой | Приор. | Сложность |
|---|---|---|---|---|
| 01 | SQL-миграция + сид + EF-модель + DbSet | Migrations / Infrastructure | P0 | M (1.5) |
| 02 | `KeywordListType`, запись слова, правила, матчер, контракт репозитория | Domain | P0 | M (1.0) |
| 03 | Репозиторий, singleton-кэш снимка, hosted-прогрев, DI | Application / Infrastructure | P0 | M (1.5) |
| 04 | Сервис валидации/лимита + `GET/POST/DELETE` под `AdminAuthorization` | Application / Infrastructure / Api | P0 | M (1.5) |
| 05 | `ICommodityCategoryHintProvider`, обязательный `hints` в `IAiClient`, resolver и фон | Application / Infrastructure | P0 | M (1.0) |
| 06 | Блок подсказок в промте, удаление хардкода, замена устаревшего теста | Infrastructure / Tests | P0 | M (1.0) |
| 07 | Раздел «Категории товаров» (маршрут, сайдбар, страница, чипы) | Frontend | P1 | M (1.5) |
| 08 | Amend-заметки в ADR-009/022/027/028 + сквозная ручная приёмка | Docs | P1 | S (0.5) |

**Критический путь:** 01 → 03 → 04 → 05 → 06 → 08 (≈ 8.0 чел.-дн.).
Задача 02 стоит на пути по порядку исполнения, но её результат нужен 03 и 05; 07 зависит только
от 04 и может быть выполнена после него, однако в таблице зафиксирован единый последовательный
порядок. Итого до ручной приёмки ≈ 9.5 чел.-дн.

## Общие инварианты для всех задач

1. **Схема описана дважды и одинаково** (ADR-035 п. 1, прецедент ADR-033/034): три CHECK
   (`ck_commodity_category_keywords_category_not_undefined`, `..._list_type`, `..._word_length`)
   и уникальный индекс `ux_commodity_category_keywords_category_list_word`
   (`category_id, list_type, normalized_word`) объявлены **и** в SQL-скрипте, **и** в
   `IEntityTypeConfiguration`, потому что Postgres-тесты создают схему из EF-модели
   (`EnsureCreatedAsync`). Расхождение имён/состава инвариантов — дефект задачи.
2. **Одна логика подсказок** (FR-8, решение C1): матчинг существует только в
   `CategoryKeywordMatcher`, доставка — только в `ICommodityCategoryHintProvider`; resolver и фон
   делают по одному вызову провайдера. Дублирования матчинга в двух call site быть не может.
3. **Слово хранится как введено** (FR-5), нормализуется только `normalized_word`
   (`CommodityCategoryKeywordRules.NormalizeWord`: trim + lowercase invariant + collapse пробелов).
   `CommodityNameNormalizer` к словам **не** применяется (ADR-035, «Явно зафиксированные семантики»).
4. **Нормализованное название товара** — только `CommodityNameNormalizer.NormalizeName`
   (ADR-034); новых нормализаций названия не появляется.
5. **Новых настроек нет** (`AGENTS.md`, `.env.example`, `docker-compose*.yml`, `appsettings*.json`
   не расширяются). `MaxPromptHints = 50` — константа в `OpenAiCompatibleAiClient` (ADR-035 F1).
6. **Вне области и не реализуется ни в одной задаче:** ретроактивный пересчёт (`commodities`,
   `precategorized_commodities`, `categorization_decisions`), аудит правок слов, телеметрия
   срабатываний, справочник категорий в БД, per-user/per-merchant слова, веса/синонимы/стемминг,
   regex/границы слов, импорт/экспорт, редактируемые настройки ИИ, синхронизация кэша между
   инстансами.
7. **Существующие контракты не меняются** (NFR-3): ни одно поле, код ответа или текст ошибки
   `suggest`/`categories`/`/api/precategorized-commodities*`/`/api/categorization-decisions`
   не меняется. Меняется только **внутренняя** сигнатура `IAiClient` (задача 05).
8. **Каждая задача оставляет `cd Analytics && dotnet test` зелёным.** Допустимы только
   механические правки существующих тестов, явно перечисленные в задаче (новая сигнатура
   фейкового `IAiClient`, обновление вызовов); утверждения существующих тестов не ослабляются.
9. **Зависимости проектов** (`Architecture/ProjectDependencyTests`): Domain не знает
   Application/Infrastructure/Api; Infrastructure — Application + Domain; Api — Application.
   Тест остаётся зелёным.
10. **Логи** не расширяются текстами слов и названий товаров: только количества совпавших
   подсказок на уровне Debug (ADR-035 п. 9, практика ADR-027/028).
11. **Ни один агент не делает `git add/commit/push`.** Коммит — только человек после ручного
    ревью сводки.

## Границы изменений (сводно)

| Разрешено менять | Запрещено менять |
|---|---|
| `Analytics/src/**` (Domain, Application, Infrastructure, Api, Migrations) | `backend/`, `bot/` (Go-сервисы), MongoDB |
| `Analytics/tests/**` | `docker-compose.yml`, `docker-compose.develop.yml`, `.env.example`, `AGENTS.md` |
| `Analytics/frontend/src/**` (только задача 07) | `docker/nginx`, конфиги nginx, `docs/adr/033*`, формат `categorization_decisions` |
| `docs/reviews/commodity-categories/**` (сводки) | `appsettings*.json` и любые новые настройки |
| `docs/adr/009,022,027,028` (только задача 08, только amend-заметки) | продуктовая логика вне рамок FR-1…FR-10 |

---

## T-01. Схема: SQL-миграция с сидом, EF-модель, DbSet

**Приоритет:** P0 · **Сложность:** M (1.5 чел.-дн.) · **Зависит от:** —
· **Сводка:** `docs/reviews/commodity-categories/01-schema-migration-seed.md`

### Цель
Ввести хранилище слов (решения A1 и B1): таблица `commodity_category_keywords` с тремя CHECK и
уникальным индексом case-insensitive дубликата, одноразовый сид содержимого блока
«Разграничения схожих категорий» (FR-2), EF-сущность/конфигурация и `DbSet`, выражающие ту же
схему. Продуктовый код слова пока не читает и не пишет.

### Что входит
- **Migrations:** новый скрипт
  `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261112120000_add_commodity_category_keywords.sql`
  (имя — как в ADR-035 п. 1; раннер применяет транзакцией, учитывает имя в
  `migration_scripts_history`, сортирует по имени файла):
  - `CREATE TABLE commodity_category_keywords (id uuid PRIMARY KEY, category_id integer NOT NULL,
    list_type varchar(16) NOT NULL, word varchar(64) NOT NULL, normalized_word varchar(64) NOT NULL)`;
  - три CHECK с точными именами из ADR-035 п. 1: `category_id <> 0`,
    `list_type IN ('boost','exclude')`, `char_length(word) BETWEEN 2 AND 64`;
  - `CREATE UNIQUE INDEX ux_commodity_category_keywords_category_list_word
    ON commodity_category_keywords (category_id, list_type, normalized_word)`;
  - сид: `INSERT … VALUES (gen_random_uuid(), <category_id>, '<boost|exclude>', '<word>',
    '<normalized_word>') … ON CONFLICT DO NOTHING` — **полный** перенос таблицы ADR-035 п. 2
    (все 11 строк блока: boost субъекта, exclude категорий-целей, boost пояснений в скобках,
    словоформы по правилу 5); `normalized_word` = `lower(trim(word))` соответствующим значением;
  - шапка скрипта с комментариями: зачем CHECK (закрытое множество FR-1), почему нет FK
    (ADR-005), почему лимит 200 не в схеме (A5), почему сид одноразовый и удалённое слово
    не воскресает (B1), почему нужен `ON CONFLICT DO NOTHING`;
  - блок сида обрамляется комментариями-маркерами (`-- seed:begin` / `-- seed:end`) — только
    для воспроизведения сида в тесте идемпотентности; на выполнение скрипта маркеры не влияют.
- **Infrastructure:** `Persistence/Postgres/CommodityCategoryKeywordEntity.cs` — `Guid Id`,
  `int CategoryId`, `string ListType`, `string Word`, `string NormalizedWord` (XML-doc со ссылкой
  на ADR-035 п. 1 и на то, что категория — код enum без FK).
- **Infrastructure:** `Persistence/Postgres/Configurations/CommodityCategoryKeywordConfiguration.cs`
  — `ToTable("commodity_category_keywords")`, `HasKey(Id)`, `IsRequired`/`HasMaxLength(16/64/64)`,
  **три `HasCheckConstraint` с теми же именами и выражениями, что в SQL**, `HasIndex(CategoryId,
  ListType, NormalizedWord).IsUnique().HasDatabaseName("ux_commodity_category_keywords_category_list_word")`,
  комментарий «схема дублируется в SQL, потому что тесты строят её из EF-модели».
- **Infrastructure:** `Persistence/Postgres/ReceiptDbContext.cs` — `DbSet<CommodityCategoryKeywordEntity>
  CommodityCategoryKeywords` (XML-doc: глобальные данные, query-filter не ставится) и
  `ApplyConfiguration(new CommodityCategoryKeywordConfiguration())` в `OnModelCreating`.
- **Тесты** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityCategoryKeywordSchemaTests.cs`
  (Testcontainers Postgres, по образцу `PrecategorizedCommodityRepositoryTests` / ...`MigrationTests`):
  - EF-схема: дубликат `normalized_word` в одной паре `(category_id, list_type)` → `23505`;
    то же слово в другом списке или другой категории — успешно; `category_id = 0` → CHECK;
    `list_type = 'other'` → CHECK; `word` длиной 1 и 65 → CHECK, длиной 2 и 64 — успешно;
  - миграция: применить **реальный файл скрипта** на пустой БД (файл копируется в output
    тестовой сборки через `<None Include=… CopyToOutputDirectory="PreserveNewest" />` в
    `ReceiptCollector.Analytics.Api.Tests.csproj`, путь в тесте не хардкодится; если схема уже
    создана EF-моделью — сначала `DROP TABLE commodity_category_keywords`, прецедент —
    `PrecategorizedNormalizedNameMigrationTests`): таблица и индекс
    существуют (`pg_indexes`), три констрейнта есть (`pg_constraint`), строк сида ровно столько,
    сколько в таблице ADR-035 п. 2, плюс точечные проверки (`DriedFruits`/boost содержит `изюм`,
    `Fruits`/exclude содержит `изюм`, `Groceries`/boost содержит `соль`, `FitnessAndSports`/boost
    содержит `абонемент`);
  - идемпотентность сида: повторное выполнение фрагмента между `-- seed:begin`/`-- seed:end`
    не падает и не меняет количество строк (`ON CONFLICT DO NOTHING`).
- **Приёмка сида** (сверка при ревью, без кода): каждая строка таблицы ADR-035 п. 2 разложена
  ровно по правилам 1–5 того же пункта; пропусков и «смысловых дублей» нет.

### Что НЕ входит
- Чтение/запись слов из кода: репозиторий, кэш, сервис, API (задачи 03–04).
- Домен (тип списка, правила, матчер) — задача 02; EF-сущность намеренно не использует
  `KeywordListType`/`CommodityCategory`, чтобы задача 01 не зависела от домена.
- Удаление хардкода из `BuildPayload` и любые правки промта (задача 06) — в этой задаче блок
  остаётся в коде, а его копия появляется в БД (это ожидаемое промежуточное состояние).
- Применение миграции на dev/prod — шаг эксплуатации (раздел «Порядок применения миграции»), не код.
- Новые настройки, таблица-справочник категорий, `created_at`/`created_by`, маркер «сид».

### Тесты / ручная проверка
- Автотесты выше (EF-схема + реальный файл миграции + повтор сида).
- Ручная (dev, после применения миграции): `\d commodity_category_keywords` показывает все
  колонки, три CHECK, уникальный индекс; `SELECT list_type, count(*) … GROUP BY 1` даёт
  ненулевые `boost` и `exclude`.

### Критерии готовности
- `dotnet test` зелёный; новые тесты проверяют и EF-схему, и файл миграции.
- Состав и имена CHECK/индекса в SQL и в `IEntityTypeConfiguration` совпадают символ в символ.
- После миграции в БД есть все слова блока; в коде **пока** блок ещё есть (удаляется в 06) —
  это фиксируется в сводке как осознанное промежуточное состояние.
- В репозитории нет кода, который сидирует слова при старте (никакого upsert/файла с примерами).

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryKeywordSchema"
cd Analytics && dotnet test
```

---

## T-02. Домен: тип списка, правила слова, матчер, контракт репозитория

**Приоритет:** P0 · **Сложность:** M (1.0 чел.-дн.) · **Зависит от:** 01 (мягкая — wire-имена,
длины и `normalized_word` фиксируются схемой задачи 01)
· **Сводка:** `docs/reviews/commodity-categories/02-domain-keywords-matcher.md`

### Цель
Дать домену чистые, тестируемые без БД правила (решения A1, C1): тип списка с wire-именами,
запись слова, нормализацию/валидацию слова и единственную функцию матчинга с детерминированным
порядком подсказок. Никаких обращений к БД, кэшу и промту.

### Что входит
- **Domain:** `Modules/Commodities/KeywordListType.cs` — `enum KeywordListType { Boost, Exclude }`
  + `KeywordListTypeNames` (wire-имена `boost`/`exclude`, `ToWireName`, `TryParse`) по образцу
  `CategorizationSourceNames`.
- **Domain:** `Modules/Commodities/CommodityCategoryKeyword.cs` —
  `record CommodityCategoryKeyword(Guid Id, CommodityCategory Category, KeywordListType ListType,
  string Word, string NormalizedWord)`.
- **Domain:** `Modules/Commodities/CommodityCategoryKeywordSnapshot.cs` — неизменяемый снимок
  (`IReadOnlyList<CommodityCategoryKeyword> Items`, пустой снимок как значение по умолчанию).
  Тип нужен матчеру (ADR-035 п. 4.1) и кэшу (п. 6); размещение в Domain — уточнение плана,
  без него Domain-матчер зависел бы от Application.
- **Domain:** `Modules/Commodities/CommodityCategoryKeywordRules.cs` — `MinWordLength = 2`,
  `MaxWordLength = 64`, `MaxWordsPerList = 200`, `NormalizeWord(string)` (trim + lowercase
  invariant + collapse пробелов), `IsValidWord(string)` (длина после trim в границах).
- **Domain:** `Modules/Commodities/CategoryKeywordMatcher.cs` — `static GetHints(snapshot,
  normalizedProductName)`:
  1. пустое/пробельное название **или** название без единой буквы → пустой список;
  2. `normalizedProductName.Contains(e.NormalizedWord)` (обе стороны уже в нижнем регистре);
  3. порядок: exclude-подсказки первыми, затем boost; внутри группы — `Category` (int-код) asc,
     затем `NormalizedWord` (ordinal) asc;
  4. возвращает **все** совпадения (усечение до 50 — только на рендере промта, задача 06).
- **Domain:** `Modules/Commodities/ICommodityCategoryKeywordRepository.cs` — `GetAllAsync(ct)`,
  `ExistsAsync(category, listType, normalizedWord, ct)`, `CountAsync(category, listType, ct)`,
  `AddAsync(CommodityCategoryKeyword, ct)` (bool — `false` при конфликте уникальности),
  `DeleteAsync(Guid id, ct)` (bool — `false`, если строки нет). XML-doc: категория без FK,
  `Exists`/`Count` адресны по паре «категория + список».
- **Тесты** `CommodityCategoryKeywordRulesTests.cs`:
  - нормализация: `"  Курага  "` → `курага`; `"СОЕВЫЙ   СОУС"` → `соевый соус`;
    регистр в `Word` не меняется (проверяется на уровне сервиса в 04, здесь — только `NormalizeWord`);
  - длина 1/2/64/65; пробельное слово; эмодзи и спецсимволы (проходят, если длина 2–64);
  - константы равны 2/64/200.
- **Тесты** `CategoryKeywordMatcherTests.cs`:
  - совпадение подстрокой при разном регистре с обеих сторон (`фасоль` ↔ `соль`);
  - пустое и цифровое название (`"12345"`) → пусто;
  - слово в boost двух категорий → обе подсказки;
  - слово в boost и в exclude одной категории → обе, exclude первой;
  - детерминированный порядок при перемешанном входе (снимок в обратном порядке → тот же вывод);
  - 200 совпавших слов × N категорий не теряются и не усекаются матчером;
  - снимок пуст → пусто.

### Что НЕ входит
- Репозиторий, кэш, hosted-прогрев, DI (03); сервис и API (04); провайдер и промт (05–06).
- Лимит `MaxPromptHints` и любое усечение — только в 06.
- Стемминг/морфология/границы слов/regex (ADR-035, открытый вопрос 1).
- Любые изменения `CommodityNameNormalizer` и `CommodityCategoryHelper`.

### Тесты / ручная проверка
- Юнит-тесты выше; ручных сценариев нет.

### Критерии готовности
- Матчер — чистая статическая функция: в файле нет `DbContext`, `ILogger`, DI-зависимостей.
- Поведение матчинга соответствует ADR-035 п. 4.1 дословно (включая порядок и «без усечения»).
- В репозитории нет второго экземпляра логики матчинга/нормализации слова.
- `dotnet test` зелёный, `ProjectDependencyTests` зелёный.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CategoryKeywordMatcher|FullyQualifiedName~CommodityCategoryKeywordRules"
cd Analytics && dotnet test
```

---

## T-03. Хранение: реализация репозитория, кэш снимка, hosted-прогрев

**Приоритет:** P0 · **Сложность:** M (1.5 чел.-дн.) · **Зависит от:** 01, 02
· **Сводка:** `docs/reviews/commodity-categories/03-storage-cache-warmup.md`

### Цель
Реализовать слой хранения и in-memory снимок слов (решение E1, NFR-1/NFR-4): один `SELECT`
наполняет неизменяемый снимок, читатели берут его без блокировок и без БД, прогрев — hosted
service, перечитывание — по вызову `RefreshAsync` и лениво по «грязному» флагу.

### Что входит
- **Application:** `Modules/Commodities/Contracts/ICommodityCategoryKeywordCache.cs` —
  `CommodityCategoryKeywordSnapshot GetSnapshot()` (при «грязном» флаге перечитывает),
  `Task RefreshAsync(CancellationToken)`; XML-doc с семантикой деградации (сбой перечитывания
  не бросает на горячий путь, старый снимок продолжает использоваться).
- **Infrastructure:** `Persistence/Postgres/CommodityCategoryKeywordRepository.cs` —
  `GetAllAsync` (один `AsNoTracking` `SELECT`, проекция в `CommodityCategoryKeyword`, безопасное
  чтение кода категории: `Enum.IsDefined` + пропуск `Undefined`, как ADR-032),
  `ExistsAsync`/`CountAsync` по паре `(CategoryId, ListType)`,
  `AddAsync` (`Id = Guid.NewGuid()`, `SaveChangesAsync`, `catch` unique violation `23505` → `false`),
  `DeleteAsync` по `id` (одна строка, `false` если нет).
- **Infrastructure:** `Modules/Commodities/InMemoryCommodityCategoryKeywordCache.cs` (singleton):
  ссылка на снимок через `Volatile.Read`/`Volatile.Write`, `SemaphoreSlim(1,1)` только на запись,
  `volatile bool` «грязный»; `RefreshAsync` — полное перечитывание и атомарная подмена ссылки,
  при ошибке — `LogWarning`, флаг «грязный» остаётся; `GetSnapshot` при «грязном» — double-check
  под тем же семафором.
- **Infrastructure:** `Synchronization/CommodityCategoryKeywordCacheInitializationHostedService.cs`
  — прогрев при старте через `IServiceScopeFactory` (стиль
  `CommodityCategoryCacheInitializationHostedService`), ошибка прогрева не валит процесс: кэш
  остаётся «грязным» и перечитается на первом чтении.
- **Infrastructure:** `Configuration/DependencyInjectionExtensions.cs` — `AddSingleton<
  ICommodityCategoryKeywordCache, InMemoryCommodityCategoryKeywordCache>()`,
  `AddScoped<ICommodityCategoryKeywordRepository, CommodityCategoryKeywordRepository>()`,
  `AddHostedService<CommodityCategoryKeywordCacheInitializationHostedService>()`.
- **Тесты** `Infrastructure/CommodityCategoryKeywordRepositoryTests.cs` (Testcontainers):
  - `GetAllAsync` возвращает все строки, включая одинаковые слова в разных категориях/списках;
    строки с невалидным кодом категории отбрасываются;
  - `ExistsAsync`/`CountAsync` адресны по паре (слово в другой категории/списке не мешает);
  - `AddAsync` пишет `word` и `normalized_word` как передано; повтор в той же паре → `false`
    (не исключение); другой регистр того же `normalized_word` → `false`;
  - `DeleteAsync` по существующему `id` → `true` и строка исчезла; повтор → `false`.
- **Тесты** `Infrastructure/InMemoryCommodityCategoryKeywordCacheTests.cs` (фейковый репозиторий):
  - `GetSnapshot` после прогрева не обращается к репозиторию (счётчик вызовов);
  - `RefreshAsync` подменяет снимок: ссылка, взятая до refresh, остаётся прежней и валидной;
  - сбой refresh → предупреждение, старый снимок отдаётся, следующий `GetSnapshot` снова пробует
    перечитать (сценарий «упал один раз, потом успех»);
  - конкурентные чтения во время `RefreshAsync` не падают и не видят «полуснимок» (каждый снимок —
    либо старый, либо новый целиком);
  - hosted service при недоступном репозитории не бросает и оставляет кэш «грязным».

### Что НЕ входит
- Сервис валидации/лимита, API (04); провайдер подсказок, `hints` в `IAiClient` (05).
- Инкрементальные правки `ConcurrentDictionary` (вариант E2 отклонён) и чтение таблицы на каждый
  запрос (E3 отклонён).
- Распределённая инвалидация/Redis/`LISTEN/NOTIFY` (E4, вне области).
- Новые настройки (ни `MaxSize`, ни TTL у этого кэша нет).

### Тесты / ручная проверка
- Автотесты выше.
- Ручная (dev): после старта Analytics `SELECT count(*) FROM commodity_category_keywords` совпадает
  с количеством слов в снимке (косвенно — по счётчикам на странице задачи 07).

### Критерии готовности
- На горячем пути (`GetSnapshot`) нет обращений к БД и нет блокировок.
- Кэш — singleton; репозиторий — scoped; hosted-прогрев зарегистрирован ровно один раз.
- Сбой БД при прогреве/refresh не роняет процесс и не ломает категоризацию.
- `dotnet test` зелёный; `ProjectDependencyTests` зелёный.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryKeywordRepository|FullyQualifiedName~CommodityCategoryKeywordCache"
cd Analytics && dotnet test
```

---

## T-04. Сервис ключевых слов + админский API

**Приоритет:** P0 · **Сложность:** M (1.5 чел.-дн.) · **Зависит от:** 03
· **Сводка:** `docs/reviews/commodity-categories/04-service-admin-api.md`

### Цель
Дать администратору контракты FR-3/FR-4/FR-5/FR-10: сервис с валидацией и лимитом, полный снимок
по всем категориям и адресные `POST`/`DELETE` под `AdminAuthorization`, с инвалидацией кэша после
commit (NFR-4).

### Что входит
- **Application:** `Modules/Commodities/Contracts/ICommodityCategoryKeywordService.cs` —
  `GetAllAsync(ct)`, `AddAsync(categoryId, listType, word, ct)`, `DeleteAsync(id, ct)`.
- **Application:** `Modules/Commodities/Models/CommodityCategoryKeywordModels.cs` —
  `CategoryKeywordsDto(CategoryId, CategoryKey, CategoryName, Group, Boost, Exclude)`,
  исходы `AddKeywordOutcome { Added, Duplicate, LimitReached }`,
  `DeleteKeywordOutcome { Deleted, NotFound }` и результат добавления, несущий созданный элемент
  (`id`, `categoryId`, `listType`, `word`) для тела ответа `POST` (ADR-035 п. 7).
- **Infrastructure:** `Modules/Commodities/CommodityCategoryKeywordService.cs`:
  - порядок проверок ровно как в таблице ошибок ADR-035 п. 7: категория (`Enum.IsDefined` +
    запрет `Undefined`) → `listType` (`KeywordListTypeNames.TryParse`) → слово после trim
    (2–64) → дубликат (`NormalizedWord` + `ExistsAsync`) → лимит (`CountAsync` ≥ 200);
  - запись: `AddAsync` с `NormalizedWord = CommodityCategoryKeywordRules.NormalizeWord(word)` и
    `Word = word.Trim()`; `false` из репозитория (гонка, `23505`) → `Duplicate` (409, не 500);
  - удаление по `id`; отсутствие строки → `NotFound`;
  - **после успешного commit** — `cache.RefreshAsync`; ошибка перечитывания не меняет исход
    мутации (возвращается успех, кэш остаётся «грязным»; ADR-035 п. 6);
  - `GetAllAsync`: все категории из `CommodityCategoryHelper.GetAll()` кроме `Undefined`
    (включая пустые списки; порядок — порядок объявления), `Group` из
    `CommodityCategoryHelper.GetGroup`, слова — по `word` `OrdinalIgnoreCase`, затем `Ordinal`.
- **Api:** `Modules/CommodityCategoryKeywords/CommodityCategoryKeywordEndpoints.cs` —
  `MapGroup("/api/commodity-category-keywords").WithTags("CommodityCategoryKeywords")`,
  `MapGet("")`, `MapPost("")`, `MapDelete("/{id:guid}")`; `AdminAuthorization.AuthorizeAsync` в
  каждом; текстовые тела ошибок без ProblemDetails; коды: `200` на `GET`, `200` на `POST`
  (не `201` — ADR-035 п. 7), `400`/`409` с точными текстами из таблицы ADR-035 п. 7,
  `204`/`404` (`word not found.`) на `DELETE`.
- **Api:** `Program.cs` — `app.MapCommodityCategoryKeywordEndpoints();`.
- **Infrastructure:** DI — `AddScoped<ICommodityCategoryKeywordService, CommodityCategoryKeywordService>()`.
- **Тесты** `Infrastructure/CommodityCategoryKeywordServiceTests.cs`:
  - добавление сохраняет регистр в `Word` и сворачивает `NormalizedWord`; трим обрамляющих пробелов;
  - дубликат case-insensitive → `Duplicate`; гонка (репозиторий вернул `false`) → `Duplicate`;
  - 199/200 слов → `Added`/`LimitReached`; лимит считается по паре «категория + список»;
  - удаление по `id` → `Deleted`; отсутствие → `NotFound`;
  - `RefreshAsync` вызван после успешного добавления и после удаления, но **не** при `Duplicate`/
    `LimitReached`/`NotFound`;
  - падение `RefreshAsync` → мутация успешна, кэш помечен «грязным» (фейковый кэш).
- **Тесты** `CommodityCategoryKeywordEndpointsTests.cs` (исполнение `IResult`, практика ADR-027/028):
  - `401` без сессии, `403` не-админу на всех трёх;
  - `GET` содержит все категории из `GetAll()` без `Undefined` (включая пустые `boost`/`exclude`)
    и группу из `GetGroup`;
  - `POST`: `400 Invalid category.` (`categoryId = 0`, неизвестный код), `400 listType must be
    'boost' or 'exclude'.`, `400 word must be between 2 and 64 characters after trimming.`,
    `409 word already exists in this list.`, `409 list limit of 200 words reached.`,
    `200` с телом `{ id, categoryId, listType, word }` (слово после trim, регистр исходный);
  - `DELETE`: `204`, повтор → `404 word not found.`.

### Что НЕ входит
- Провайдер подсказок, `IAiClient`, resolver и фон (05); промт (06); фронтенд (07).
- `X-Total-Count`, пагинация, `GET` по `id`, `PUT` списка целиком, восстановление сида
  (варианты G2/G3, B3 отклонены).
- Аудит правок, `created_at`/`created_by`, телеметрия (вне области).
- Изменения существующих suggest/categories-эндпоинтов (NFR-3).

### Тесты / ручная проверка
- Автотесты выше.
- Ручная (dev, после миграции): `curl` под админом — `GET` возвращает снимок; `POST` добавляет
  слово, повтор даёт `409`; `DELETE` убирает слово; без сессии — `401`, не-админ — `403`.

### Критерии готовности
- Все тексты и коды ошибок совпадают с таблицей ADR-035 п. 7 символ в символ.
- Ни один существующий эндпоинт и его DTO не изменены.
- Мутация не откатывается из-за сбоя инвалидации кэша; кэш не «залипает» (грязный флаг).
- `dotnet test` зелёный; `ProjectDependencyTests` зелёный (Api может ссылаться на
  Application/Infrastructure/Domain — новых зависимостей задача не добавляет).

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryKeyword"
cd Analytics && dotnet test
```

---

## T-05. Доставка подсказок: провайдер, смена сигнатуры `IAiClient`, resolver и фон

**Приоритет:** P0 · **Сложность:** M (1.0 чел.-дн.) · **Зависит от:** 02, 03
· **Сводка:** `docs/reviews/commodity-categories/05-hints-pipeline.md`

### Цель
Выполнить FR-6/FR-8 целиком на уровне доставки: единственный провайдер считает подсказки по
нормализованному названию из снимка кэша, оба call site передают их в `IAiClient`, а обязательный
параметр `hints` (решение D1) не даёт забыть подсказки в новом вызове. Промт в этой задаче ещё
не меняется — подсказки доходят до клиента и проверяются тестами аргумента.

### Что входит
- **Application:** `Modules/Commodities/Contracts/ICommodityCategoryHintProvider.cs` —
  `IReadOnlyList<CommodityCategoryKeywordHint> GetHints(string normalizedName)`.
- **Application:** `Modules/Commodities/Models/CommodityCategoryKeywordModels.cs` (тот же файл,
  что в 04) — `record CommodityCategoryKeywordHint(CommodityCategory Category,
  KeywordListType ListType, string Word)`.
- **Infrastructure:** `Modules/Commodities/CommodityCategoryHintProvider.cs` — `cache.GetSnapshot()`
  + `CategoryKeywordMatcher.GetHints(...)`; ничего не знает про БД и про промт.
- **Application:** `Modules/Commodities/Contracts/IAiClient.cs` — **обязательный** параметр
  `IReadOnlyCollection<CommodityCategoryKeywordHint> hints` перед `CancellationToken` (пустая
  коллекция = совпадений нет, `null` не допускается); XML-doc: подсказки — данные запроса,
  решение остаётся за моделью.
- **Infrastructure:** `AI/OpenAiCompatibleAiClient.cs` — новая сигнатура; параметр принимается и
  пока **не влияет** на тело запроса; в коде явный комментарий «рендеринг блока подсказок — T-06».
  Хардкод «Разграничения схожих категорий» и существующий тест остаются (удаляются в 06).
- **Infrastructure:** `Modules/Commodities/CommodityCategoryResolver.cs` — в `SuggestByAiAsync`
  для каждого уникального ключа группы: `var hints = _hintProvider.GetHints(item.Key);` →
  `_aiClient.SuggestCategoryAsync(item.Key, CategoryNames, hints, aiCancellationToken)`.
  Один расчёт на уникальное очищенное имя, дедупликация ADR-034 сохраняется; порядок шагов
  `existing → merchant-default → cache → precategorized → ai`, коды ответов, `503`/`aiAvailable`
  и бюджет `SuggestTimeout` — без изменений.
- **Infrastructure:** `Modules/Commodities/PrecategorizationBackgroundService.cs` — в
  `ProcessCandidateAsync`: `GetHints(normalized)` и передача в `SuggestCategoryAsync`; окно,
  пауза, `BatchLimit`, курсор, проверки имени — без изменений.
- **Infrastructure:** DI — `AddScoped<ICommodityCategoryHintProvider, CommodityCategoryHintProvider>()`.
- **Тесты (механическая правка всех call site — иначе компиляция красная):**
  `CommodityCategorizationServiceTests`, `CommodityBatchCategorizationServiceTests`,
  `CommodityCategoryResolverTests`, `Infrastructure/PrecategorizationBackgroundServiceTests`,
  `OpenAiCompatibleAiClientTests` — добавить пустую коллекцию подсказок в вызовы; утверждения
  существующих тестов не меняются.
- **Тесты (новые)** `CommodityCategoryResolverTests`:
  - в `IAiClient` уходит подсказка, собранная по **нормализованному** имени (mock/NSubstitute
    захватывает аргумент): снимок `DriedFruits/boost/изюм` → для `"Изюм 250 г"` подсказка есть;
  - при отсутствии совпадений — **пустая** коллекция (не `null`);
  - две позиции с одним очищенным именем → один вызов ИИ с одним и тем же набором подсказок;
  - подсказки не меняют маршрутизацию: приоритеты `existing/merchant-default/cache/precategorized`
    и дедупликация — регресс существующих тестов.
- **Тесты (новые)** `PrecategorizationBackgroundServiceTests`:
  - подсказки считаются по очищенному имени (`"*3455614 Курага"` → по `курага`);
  - `Undefined` по-прежнему не создаёт строку; пауза/окно/лимит — регресс без изменений.

### Что НЕ входит
- Рендеринг блока подсказок, `MaxPromptHints`, `EscapeHintWord`, удаление хардкода и удаление
  теста `SuggestCategoryAsync_prompt_contains_new_category_heuristics` — **задача 06** (жёсткая
  связка: блок и тест удаляются только вместе).
- Изменения `CategoryNames`, system-сообщения, парсера ответа, ретраев, `AI:*`.
- Сбор подсказок в кэше/сервисе, второй источник подсказок (варианты C2–C4 отклонены).
- Фронтенд и админский API (задачи 04, 07).

### Тесты / ручная проверка
- Автотесты выше. Ручная проверка ограничена: в логах Debug на каждый вызов ИИ — количество
  совпавших подсказок (без текстов слов); визуально промт проверяется в 06/08.

### Критерии готовности
- `dotnet test` зелёный; ни одного call site `SuggestCategoryAsync` без `hints` не осталось
  (это гарантирует компилятор).
- Подсказки собираются ровно одним провайдером; в resolver и фоне нет собственного матчинга.
- Промт в этой задаче байт-в-байт прежний при пустых подсказках (существующие тесты промта зелёные).
- `ProjectDependencyTests` зелёный.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryResolver|FullyQualifiedName~Precategorization"
cd Analytics && dotnet test
```

---

## T-06. Промт: удаление хардкода, блок подсказок, замена теста хардкода

**Приоритет:** P0 · **Сложность:** M (1.0 чел.-дн.) · **Зависит от:** 01, 05
· **Сводка:** `docs/reviews/commodity-categories/06-prompt-hints-block.md`

### Цель
Закрыть FR-2 и FR-7 (решение F1): убрать из `BuildPayload` статический блок «Разграничения схожих
категорий» (единственный источник правды — БД с сидом задачи 01) и рендерить в user-сообщение
только фактически совпавшие подсказки, детерминированно, с приоритетом исключений и защитой от
управляющих символов. Формат ответа ИИ остаётся строгим JSON и последней инструкцией.

### Что входит
- **Infrastructure:** `AI/OpenAiCompatibleAiClient.BuildPayload` —
  - **удалить** заголовок и все 11 строк блока «Разграничения схожих категорий»;
  - сохранить порядок сообщений и строк: `Товар: "…"` → `Выбери ОДНУ … из реестра: <catalog>.` →
    **[опциональный блок подсказок]** → `Ответь строго в формате JSON: …` (последняя строка);
  - блок подсказок добавляется **только** при `hints.Count > 0`:
    заголовок `Подсказки по ключевым словам (совпали в названии; это только подсказки, решение
    принимай сам):` и строки `- в названии есть «<word>» → скорее не <DisplayName> (<EnumName>)`
    для exclude, `- в названии есть «<word>» → вероятно <DisplayName> (<EnumName>)` для boost
    (формулировки — ADR-035 F1 / п. 5); `DisplayName` — `CommodityCategoryHelper.GetDisplayName`,
    `EnumName` — имя enum (парсер принимает только его);
  - **порядок**: exclude первыми, затем boost (порядок приходит уже отсортированным из матчера;
    клиент не пересортировывает по-своему, только усекает);
  - `MaxPromptHints = 50` — приватная константа; усечение **после** сортировки, при переполнении
    отбрасываются только boost-подсказки;
  - `EscapeHintWord`: `char.IsControl(c)` → пробел, схлопывание пробелов, обрезка до 64 символов;
  - JSON-тело по-прежнему собирается `JsonSerializer.Serialize(body, JsonOptions)`; ручного
    склеивания JSON нет; `TryExtractCategory`/`ParseCategoryName`, ретраи и `SendAsync` не меняются.
- **Тесты:** **удалить** `OpenAiCompatibleAiClientTests.SuggestCategoryAsync_prompt_contains_new_category_heuristics`
  (он утверждает удаляемый хардкод) и заменить его набором тестов блока подсказок:
  - пустая коллекция подсказок → в теле запроса нет ни заголовка блока, ни формулировок
    «вероятно»/«скорее не» (промт равен сегодняшнему без удалённого блока);
  - непустая коллекция → заголовок и обе формулировки присутствуют; `DisplayName` и имя enum
    в скобках на месте;
  - порядок: все exclude-строки идут раньше boost;
  - лимит 50: 60 boost-подсказок → в теле ровно 50 boost-строк; при 40 exclude + 40 boost →
    все 40 exclude и только 10 boost;
  - `\n`, `\r`, `\t` в слове не создают новую строку блока (число строк блока не растёт,
    подсказка остаётся одной строкой);
  - тело запроса остаётся валидным JSON: `JsonDocument.Parse(requestBody)` не падает;
  - инструкция формата ответа — последняя содержательная строка user-сообщения;
  - регресс: разбор ответа (`{"category": "<EnumName>"}`), отклонение числовых id/`Undefined`,
    ретрай на 5xx — существующие тесты без изменений утверждений.
- **Приёмка FR-2 (проверяется в этой задаче):**
  - `grep -rn "Разграничения схожих категорий" Analytics/src` → 0 совпадений;
  - в БД после миграции 01 присутствуют слова блока (точечные `SELECT` из T-01 повторяются в
    сводке этой задачи как подтверждение «в коде нет — в БД есть»);
  - в коде нет ни файла с примерами, ни стартового upsert'а слов.

### Что НЕ входит
- Любое изменение сигнатуры `IAiClient`, resolver и фона (сделано в 05).
- Новые настройки (`MaxPromptHints` — константа), изменения `temperature`, модели, system-сообщения.
- Изменения хранения слов (регистр, `normalized_word`) и валидации (04).
- Правки `categorization_decisions`, телеметрия подсказок, аудит (вне области).

### Тесты / ручная проверка
- Автотесты выше.
- Ручная (dev): с `AI:BaseUrl`, указывающим на тестовый/логгирующий прокси, выполнить suggest для
  товара с добавленным словом и без него; убедиться, что блок подсказок появляется только во втором
  случае и содержит правильные строки.

### Критерии готовности
- Хардкод удалён из кода, его содержимое присутствует в БД после миграции (FR-2).
- Устаревший тест удалён, новые тесты блока подсказок покрывают все пункты ADR-035 п. 10
  (строка `OpenAiCompatibleAiClient`).
- Промт при пустых подсказках короче прежнего и не содержит следов удалённого блока (NFR-2).
- `dotnet test` зелёный; ни один тест не «подогнан» под новое поведение ослаблением утверждений
  (кроме явно перечисленных выше).

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~OpenAiCompatibleAiClient"
cd Analytics && dotnet test
grep -rn "Разграничения схожих категорий" Analytics/src || echo "OK: хардкода нет"
```

---

## T-07. Фронтенд: раздел «Категории товаров»

**Приоритет:** P1 · **Сложность:** M (1.5 чел.-дн.) · **Зависит от:** 04
· **Сводка:** `docs/reviews/commodity-categories/07-frontend-page.md`

### Цель
Дать администратору UI (FR-9/FR-10, UC-1…UC-3, решение H1): новый пункт сайдбара, страница с
группами категорий и двумя списками чипов, добавление/удаление слова, рефетч после мутации,
inline-ошибки сервера и состояния загрузки/пустоты.

### Что входит
- **Маршрут:** `Analytics/frontend/src/routes.ts` — `commodityCategoryKeywordsPath()` →
  `/commodity-category-keywords` (по образцу `categorizationDecisionsPath`);
  `App.tsx` — импорт страницы и `<Route>` рядом с админскими.
- **Сайдбар:** `components/Sidebar.tsx` — пункт «Категории товаров» под `{isAdmin && …}` **после**
  «Журнал категоризации», путь из `routes.ts` (не строкой).
- **Типы/API:** `types/commodityCategoryKeyword.ts` (форма `GET`-ответа и `POST`-тела по ADR-035 п. 7);
  `api/commodityCategoryKeywords.ts` — `fetchCategoryKeywords(signal)`, `addKeyword(...)`,
  `deleteKeyword(id, signal)`; ошибки — с текстом тела ответа (как в
  `api/precategorizedCommodities.ts` / `categorizationDecisions.ts`).
- **Хук:** `hooks/useCommodityCategoryKeywords.ts` — `data`, `isLoading`, `error`, `reload`,
  `add`, `delete`; `AbortController`; после успешной мутации — рефетч `GET` **с сохранением уже
  отрисованных данных** (страница не мигает в пустоту); на время запроса блокируется только
  затронутое поле/чип.
- **Страница:** `components/CommodityCategoryKeywordsPage.tsx` — гейт `useAdmin()`
  (`loading` → спиннер, `!isAdmin` → «Доступ запрещён.» **без** запросов к API), заголовок,
  пояснение про подстрочное совпадение и «исключение сильнее boost», состояния загрузки/ошибки;
  секции по `group` в порядке «Продукты», «Транспорт», «Для дома», «Прочее», затем категории с
  пустой группой (плоский список).
- **Компоненты:** `CategoryKeywordCard.tsx` (название категории, два `KeywordChipList`),
  `KeywordChipList.tsx` («Повышают» / «Исключают», счётчик `N / 200`, плейсхолдер «Список пуст»,
  чип с `×`, поле ввода + кнопка «Добавить»); стили — по образцу существующих страниц
  (CSS-файл рядом с компонентом, как `Sidebar.css`).
- **Поведение ошибок:** `400`/`409` — inline под соответствующим списком текстом сервера;
  `401` — «Сессия истекла. Войдите снова.»; `403` — «Недостаточно прав…»; сеть/`5xx` — общий
  баннер с кнопкой «Повторить» (стиль `usePrecategorizedCommodities`).
- **Лимит:** при 200 словах поле ввода блокируется с подсказкой (чтобы `409` не был сюрпризом).

### Что НЕ входит
- Любые правки backend/API (сделано в 04); новые эндпоинты, пагинация, `X-Total-Count`.
- Тестовая инфраструктура фронтенда (в проекте тестов нет — ADR-011/E1); `npm run build` +
  `npm run lint` + ручная приёмка.
- Оптимистичное обновление чипов (H2 отклонено), drag&drop, массовый ввод, импорт/экспорт.
- Скрытие `Other` и прочие UX-решения из открытых вопросов ADR-035.

### Тесты / ручная проверка
- Автотестов нет; проверки:
  ```bash
  cd Analytics/frontend && npm run build
  cd Analytics/frontend && npm run lint
  ```
- Ручная приёмка (UC-1…UC-3, FR-10): не-админ не видит пункт и получает «Доступ запрещён.» при
  прямом заходе; админ видит все категории с группами; добавление сохраняет регистр и сразу
  показывает чип; удаление убирает чип; дубликат в другом регистре даёт inline `409`; слово из
  1 символа — inline `400`; счётчики и «Список пуст» отображаются.

### Критерии готовности
- `npm run build` и `npm run lint` зелёные, `--max-warnings 0` не нарушен.
- При `!isAdmin` страница не делает ни одного запроса к API.
- После мутации данные рефетчатся, экран не «прыгает» в пустое состояние; ошибки показываются
  текстом сервера (без переизобретения сообщений).
- Ни один существующий маршрут/пункт сайдбара/контракт API не изменён.

### Проверка
```bash
cd Analytics/frontend && npm run build
cd Analytics/frontend && npm run lint
```

---

## T-08. Amend-заметки ADR 009/022/027/028 и сквозная ручная приёмка

**Приоритет:** P1 · **Сложность:** S (0.5 чел.-дн.) · **Зависит от:** 01…07
· **Сводка:** `docs/reviews/commodity-categories/08-adr-notes-acceptance.md`

### Цель
Зафиксировать изменение источников и точек применения подсказок в связанных ADR (без отмены их
решений) и провести сквозную ручную приёмку критериев задачи на реальных данных.

### Что входит
- **Docs:** короткая amend-заметка (раздел вида «Дополнение (ADR-035, дата)») в каждом ADR —
  **только** констатация изменения, без переписывания решений:
  - `docs/adr/009-auto-commodity-categorization.md` — уточнения промта приходят из БД
    (`commodity_category_keywords`), статический блок удалён; алгоритм
    `existing → … → ai`, контракты suggest/apply, `503` при пустом `AI:BaseUrl` и формат ответа
    не менялись;
  - `docs/adr/022-commodity-categories-food-detailing.md` — введённый этим ADR блок
    «Разграничения схожих категорий» перенесён в БД сидом и удалён из кода; содержимое сохранено
    (таблица ADR-035 п. 2);
  - `docs/adr/027-background-commodity-precategorization.md` — фон передаёт подсказки в промт;
    приоритеты, окно, лимиты, single-instance и стиль админских эндпоинтов без изменений;
  - `docs/adr/028-commodities-page-categorization.md` — пакетный suggest страницы «Товары»
    получает подсказки автоматически через общий resolver; `SuggestTimeout`, `aiAvailable`,
    assign-only без изменений.
  - **amend к самому ADR-035 (п. 7, пример `GET`)** — выявлено в T-07: пример п. 7 показывал
    `boost`/`exclude` как массивы строк, тогда как решения G1/G3 того же ADR требуют адресное
    удаление по `id` и прямо утверждают «`id` уже есть в выдаче `GET`». Фактическая реализация
    T-04 примера не соответствовала, и UC-3 (удаление чипа) был нереализуем. Исправлено в T-07:
    элементы списков — объекты `{ "id": "…", "word": "…" }`. Пример п. 7 привести в соответствие
    (это констатация фактической формы ответа, а не новое решение):

    ```json
    "boost": [{ "id": "6f1c…", "word": "изюм" }],
    "exclude": [{ "id": "9a2b…", "word": "изюм" }]
    ```

    Отдельно зафиксировать: эндпоинт новый, добавлен в T-04 этой же фичи и на момент правки не
    был выпущен, поэтому NFR-3 (неизменяемость контрактов существующих эндпоинтов) не затронут;
    порядок сортировки слов (`word`, `OrdinalIgnoreCase` → `Ordinal`) не изменился.
- **Ручная приёмка (сквозная, по критериям задачи):**
  1. миграция применена, в БД есть слова сида; админ добавляет новое слово в UI;
  2. на 2–3 реальных товарах из «Журнала категоризации» выполняется suggest (страница чека и
     страница «Товары»): в теле запроса к ИИ присутствует блок подсказок ровно по совпавшим словам;
     при отсутствии совпадений блока нет;
  3. конфликт (слово в boost одной категории и exclude другой) — в промте обе подсказки, exclude
     раньше;
  4. удаление слова в UI → со следующего запроса подсказки нет; рестарт не требуется (NFR-4);
  5. фоновая прекатегоризация получает те же подсказки (проверка по логам Debug/числу подсказок);
  6. `AI:BaseUrl` пуст → прежние `503`/`aiAvailable = false`.
- **Контроль границ:** `git diff --stat` по ветке не содержит `backend/`, `bot/`, `docker-compose*`,
  `.env.example`, `AGENTS.md`, `docker/nginx`, `categorization_decisions`; новых настроек нет.
- Зафиксировать в сводке фактические результаты (что проверено, на каких товарах, что осталось
  открытым из «Открытых вопросов» ADR-035 — без реализации).

### Что НЕ входит
- Изменение решений ADR-035 и связанных ADR (только amend-заметки).
- Новая функциональность, «допил» найденных мелочей: дефекты оформляются отдельной задачей
  или эскалируются к @software-architect.
- Правки разделов «Открытые вопросы»/«Допущения» ADR-035 как решений.

### Тесты / ручная проверка
- Полный прогон `cd Analytics && dotnet test` и `cd Analytics/frontend && npm run build && npm run lint`.
- Ручные сценарии 1–6 выше.

### Критерии готовности
- Четыре ADR содержат amend-заметку со ссылкой на ADR-035; ни одно прежнее решение не отменено.
- В ADR-035 п. 7 пример `GET` приведён к фактической форме (`{ id, word }`) и содержит пометку об
  amend из T-07 (см. выше); противоречие п. 7 с G1/G3 устранено.
- Все критерии приёмки задачи проверены и отражены в сводке (или явно отмечены как непроверенные
  с причиной).
- Границы соблюдены: нет изменений вне Analytics/.NET, frontend и документации.
- Сводка 08 содержит итоговый чек-лист по FR-1…FR-10, NFR-1…NFR-4.

### Проверка
```bash
cd Analytics && dotnet test
cd Analytics/frontend && npm run build && npm run lint
git diff --stat
```

---

## Порядок применения миграции и откат (эксплуатация, не код)

- Деплой: `analytics-migrations` применяет скрипт 01 (таблица + сид) → затем новый образ
  `analytics`. Порядок уже обеспечен зависимостью `service_completed_successfully` в
  `docker-compose.yml`; простоя не требуется (миграция аддитивна, старый образ таблицу не читает).
- Откат кода без схемы: прежний образ работает с новой таблицей (не использует её); слова
  остаются в БД и снова начнут действовать при повторной выкатке.
- Откат схемы (при необходимости): `DROP TABLE commodity_category_keywords;` после выката прежнего
  образа; предварительно снять дамп (`pg_dump -t commodity_category_keywords` или `./backup.sh`);
  строку в `migration_scripts_history` удалять только вместе с решением о повторном применении.
- Восстановление удалённых сид-слов без отката кода: повторно выполнить `INSERT`-блок между
  маркерами `-- seed:begin` / `-- seed:end` из файла миграции (`ON CONFLICT DO NOTHING`).

## Риски, на которые смотрит ревью

| Риск | Где закрывается |
|---|---|
| Неполнота/сдвиг сида относительно таблицы ADR-035 п. 2 | T-01 (сверка при ревью + тест количества/точечных слов), T-06 (приёмка FR-2), T-08 (ручная) |
| Расхождение схемы SQL ↔ EF-модель | T-01 (инварианты дублируются; тесты на EF-схеме) |
| Забытый call site без подсказок | T-05 (обязательный параметр `hints` — ошибка компиляции) |
| Старый тест хардкода падает после удаления блока | T-06 (удаляется в той же задаче, заменяется тестами блока) |
| Дублирование логики матчинга в resolver/фоне | T-05 (единственный провайдер; тесты аргумента) |
| Промт-инъекция/управляющие символы в слове | T-06 (`EscapeHintWord`, лимит 50, тесты `\n`/`\r`/`\t`) |
| «Залипание» кэша после сбоя refresh | T-03 (грязный флаг + double-check), T-04 (мутация успешна при сбое refresh) |
| Изменение существующих контрактов suggest/categories | T-04/T-05 (регресс существующих тестов, NFR-3) |
