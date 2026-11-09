# Декомпозиция: усиление нормализации названий товаров (снятие артикулов/штрихкодов и мусорных префиксов)

> Источники: задача [commodity-name-normalization](../tasks/commodity-name-normalization.md)
> (FR-1…FR-5, NFR-1…NFR-4, критерии приёмки, edge cases, «Вне объёма») и
> [ADR-034](../adr/034-commodity-name-normalization.md) (решения A1, B1, C1, D1, E1, F1;
> каноническое правило — п. 1; схема и миграция — п. 2; карта слоёв — п. 3; потоки — п. 4;
> семантики — п. 5; тестируемость — п. 6; матрица требований — п. 7).
>
> **Этот документ — только декомпозиция.** Новых архитектурных решений он не содержит и не
> заменяет ADR-034. Спорные места эскалируются к @software-architect и исполнителем
> самостоятельно не решаются. Реализация — строго по одной задаче за цикл
> (skill `task-review-gate`): реализация → AI-ревью → ручное ревью → коммит человеком.
>
> Изменения — только Analytics (.NET: Domain, Infrastructure, Migrations, тесты).
> Go-сервисы (`backend/`, `bot/`), MongoDB, nginx, frontend и API-контракты **не трогаются**;
> новых настроек (`AGENTS.md`, `.env*`, `docker-compose*.yml`, `appsettings.json`) не появляется
> (ADR-034 п. 3, «Явно зафиксированные семантики»).

## Ход выполнения

| NN | Задача | Зависимости | Статус |
| -- | ------ | ----------- | ------ |
| 01 | Нормализатор: правило очистки + юнит-тесты | — | committed |
| 02 | Схема: SQL-миграция, EF-модель, запись `normalized_name` при вставке | 01 | committed |
| 03 | Golden corpus + Testcontainers-паритет SQL-backfill ↔ C# + контрольные запросы | 02 | committed |
| 04 | Repository: `HasBlockingQueueEntryAsync` + удаление мёртвой sibling-ветки из confirm | 02 | committed |
| 05 | Resolver: очередь по очищенному ключу, ИИ по очищенному имени, переименование lookup | 04 | approved |
| 06 | Фон предкатегоризации: pre-check очереди, ИИ и кэш по очищенному ключу | 04, 05 | pending |
| 07 | Пакетный suggest и suggest чека: проверка ключей без изменения контрактов | 05 | pending |
| 08 | Журнал решений: регресс (сырое имя + очищенный `normalized_name`) | 01 | pending |
| 09 | Финальная проверка, релизная инструкция, ручная приёмка | 01…08 | pending |

Строки таблицы задают единственный допустимый порядок исполнения: задача N стартует только после
того, как задача N-1 проверена человеком и **закоммичена**. Параллельного выполнения нет.
Значения `in-progress` / `review` / `approved` / `committed` выставляют исполнитель и
`project-manager`; при старте все задачи — `pending`.

## Обозначения

| Поле | Значение |
|---|---|
| Приоритет | **P0** — без этого нет сквозного результата; **P1** — требуется критериями приёмки, но не блокирует предыдущие |
| Оценка | человеко-дни одного разработчика, без учёта ревью и ручной приёмки |
| Зависит от | задачи, которые должны быть **влиты** (committed) до старта |
| Проверка | конкретные команды и/или ручные сценарии |

Общие команды (frontend не участвует — `npm` не нужен):

```bash
cd Analytics && dotnet build
cd Analytics && dotnet test
cd Analytics && dotnet test --filter "FullyQualifiedName~<Fragment>"
```

## Сводка и порядок исполнения

| NN | Задача | Слой | Приор. | Оценка | Зависит от |
|---|---|---|---|---|---|
| 01 | Правило очистки в `CommodityNameNormalizer` + тесты | Domain | P0 | 0.5 | — |
| 02 | SQL-миграция, EF-колонка/индексы, запись ключа при вставке, правка сидов тестов | Migrations/Infra | P0 | 1.0 | 01 |
| 03 | Golden corpus + паритетный Testcontainers-тест миграции + контрольные запросы | Tests | P0 | 0.75 | 02 |
| 04 | `HasBlockingQueueEntryAsync`, удаление мёртвой sibling-ветки из confirm | Domain/Infra | P0 | 0.5 | 02 |
| 05 | Resolver: `GetCategoriesByNormalizedNamesAsync`, очередь/ИИ по очищенному имени | Infra | P0 | 1.25 | 04 |
| 06 | Фон: pre-check, единственный `normalized`, ИИ по ключу | Infra | P0 | 1.0 | 04, 05 |
| 07 | Пакетный suggest/чек: тесты ключей, без изменения контрактов | Tests | P1 | 0.5 | 05 |
| 08 | Журнал: регресс сырого/очищенного имени, история не пересчитывается | Tests | P1 | 0.5 | 01 |
| 09 | Финальная проверка, релизная инструкция, ручная приёмка | все | P0 | 0.75–1.0 | 01…08 |

**Критический путь:** 01 → 02 → 04 → 05 → 06 → 09 (≈ 5.0–5.25 чел.-дн.).
Суммарно последовательно ≈ 6.75–7.0 чел.-дн. до ручной приёмки.

## Общие инварианты для всех задач

1. **Единственная нормализация.** Правило живёт только в `CommodityNameNormalizer.NormalizeName`
   (решение A1). Одноразовая SQL-копия существует исключительно внутри скрипта миграции и после
   его выполнения в БД не остаётся (функция в `pg_temp` удаляется; ADR-034 п. 2).
2. **Сырые данные неприкосновенны** (FR-4): `commodities.name`, `precategorized_commodities.name`,
   `categorization_decisions.commodity_name` не переписываются; история журнала не
   пересчитывается; UI показывает сырые имена.
3. **Автопроставления нет** (NFR-1): confirm обновляет `commodities` только по **точному сырому**
   имени и только строки без категории (решение D1). Ни одна задача не добавляет массового
   применения по очищенному ключу.
4. **Контракты API/UI не меняются** (NFR-2): ни новых полей/кодов, ни изменений текстов ошибок;
   `name` в ответах suggest — сырое.
5. **Время** — только `TimeProvider` (тесты — `FakeTimeProvider`); чистые функции без побочных
   эффектов (NFR-3).
6. **Регулярные выражения** — `[GeneratedRegex]` ограниченной сложности; при отсутствии
   lookaround/backreference допускается `RegexOptions.NonBacktracking`.
7. **Каждая задача оставляет `cd Analytics && dotnet test` зелёным.** Существующие утверждения не
   меняются, допустимы только механические правки, явно перечисленные в задаче (сиды, моки,
   переименование метода интерфейса).
8. **Зависимости проектов** (`Architecture/ProjectDependencyTests`): Domain не знает
   Application/Infrastructure/Api; Infrastructure — Application + Domain. Тест остаётся зелёным.
9. **Логи** не расширяются новыми полями с названиями товаров сверх существующего формата;
   агрегаты/предупреждения — только количествами (прецедент ADR-027 FR-8, ADR-028 п. 11).
10. **Новых настроек и зависимостей** нет: `AI:*`, `Precategorization:*`,
    `CommodityCategoryCache:*`, `CommodityBatchCategorization:*` не меняются.

---

## T-01. Нормализатор: правило очистки + юнит-тесты

**Приоритет:** P0 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** —

### Цель
Реализовать каноническое правило очистки (ADR-034 п. 1) в единственной функции нормализации;
все существующие call sites (кэш, resolver, пакетный сервис, фон, журнал) получают новое правило
автоматически, без правок в этих местах.

### Что входит
- **Domain:** `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityNameNormalizer.cs`
  - сохранить `WhitespaceRegex` (`\s+` → `" "`) и поведение `null → ArgumentNullException`;
  - добавить три `[GeneratedRegex]`:
    - ведущий мусор + коды: `^(?:[\s*#№]+|[0-9]{6,})+`;
    - хвостовой кластер кодов: `(?:^|\s)[0-9]{6,}(?:\s+[0-9]{6,})*\s*$`;
    - проверка наличия буквы: `[A-Za-zА-Яа-яЁё]`;
  - шаги строго как в ADR-034 п. 1: `base` → снятие ведущего → снятие хвостового → trim/collapse →
    fallback на `base`, если `cleaned` пуст **или** в нём нет ни одной буквы;
  - обновить XML-doc: правило, fallback, ссылка на ADR-034 п. 1 и NFR-3.1 ADR-009.
- **Тесты:** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizerTests.cs`
  - theory по всей таблице примеров ADR-034 п. 1 (4 примера задачи, `АИ-95`, `1000 ЛЕПЕСТКОВ`,
    `256г`, `нар.300г`, `(3сл, 4р)`, `РЖ.КРАЙ`, `*3455614`, `2417268000006`, `*345`,
    `№ 2417268000006 хлеб`, `**##№№ 2417268000006 хлеб`, `пирог 123456`, `пирог 12345`,
    `пирог 123456 789012`, `241-7268000006 хлеб`, `хлеб 241-7268000006`,
    `хлеб 123456, 789012`);
  - отдельные факты: код в середине не снимается; детерминизм (одинаковый вход → одинаковый
    выход при повторных вызовах); `null` → `ArgumentNullException`;
  - существующие кейсы (`""`, `Молоко 2.5%` vs `3.2%`, пунктуация, регистр/пробелы) остаются
    без изменения ожиданий.

### Что НЕ входит
- Любые правки call sites: resolver, фон, кэш, батч, журнал (T-05…T-08).
- Схема БД, миграции, SQL-функции (T-02, T-03).
- `ё → е`, опечатки, коды в середине, token-based эвристики (вне объёма задачи и ADR, открытый
  вопрос 5).

### Тесты / ручная проверка
- Юнит-тесты выше — единственная проверка; ручных сценариев нет.

### Критерии готовности
- Все кейсы ADR-034 п. 1 (таблица примеров) проходят; поведение `""`/`null` не ухудшено.
- В файле нет второго экземпляра правила; других изменений в репозитории нет.
- `dotnet test` зелёный (остальные наборы не менялись).

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityNameNormalizer"
cd Analytics && dotnet test
```

---

## T-02. Схема: SQL-миграция, EF-модель, запись `normalized_name` при вставке

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-01

### Цель
Ввести индексируемый очищенный ключ строки очереди (решение B1): колонка `normalized_name`
в `precategorized_commodities`, её запись при каждой вставке и миграция данных FR-5 — ровно в
объёме ADR-034 п. 2. После задачи инвариант «у каждой строки очереди есть очищенный ключ»
выполняется для новых строк; чтение по ключу ещё не вводится.

### Что входит
- **Migrations:** новый скрипт
  `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261109120000_add_precategorized_normalized_name.sql`
  (имя — как в ADR-034; `MigrationRunner` сортирует скрипты по имени, транзакция на скрипт, учёт в
  `migration_scripts_history`). Содержимое — шаги 1–9 ADR-034 п. 2 дословно:
  `ADD COLUMN normalized_name varchar(256)` → `DELETE WHERE status = 0` (FR-5) → `pg_temp`
  функция-копия правила → preflight `RAISE EXCEPTION` при `length(...) > 256` для `applied` →
  backfill только `applied` → `SET NOT NULL` → `ix_precategorized_commodities_normalized_name` →
  частичный уникальный `ux_precategorized_commodities_normalized_proposed (normalized_name)
  WHERE status = 0` → `DROP FUNCTION pg_temp...`.
  Комментарии в скрипте: зачем `pg_temp` (одноразовость, отсутствие функции в runtime), почему
  `DELETE proposed` и сохранение `applied` (FR-5), почему уникальность `proposed` глобальная
  (FR-3.4 ADR-027 по очищенному ключу), идымпотентность обеспечивает раннер, **порядок деплоя**
  (см. раздел «Порядок применения миграции»).
- **Infrastructure:** `Persistence/Postgres/PrecategorizedCommodityEntity.cs` — свойство
  `NormalizedName` (string); `Configurations/PrecategorizedCommodityConfiguration.cs` —
  `.IsRequired().HasMaxLength(256)`, оба индекса с точными именами из SQL
  (`HasFilter("status = 0")` у уникального), комментарий «индексы объявлены и в EF-модели, и в
  SQL: тесты создают схему через `EnsureCreatedAsync`».
- **Infrastructure:** `PrecategorizedCommodityRepository.TryAddProposalAsync`
  - `normalized = CommodityNameNormalizer.NormalizeName(candidate.Name)` (единственный вызов на
    строку);
  - guard: сырое имя (как сейчас) **и** `normalized` длиной ≤ 256 — иначе `LogWarning` с длинами и
    `false` (FR-4.7, риск переполнения после lowercase);
  - `entity.NormalizedName = normalized`;
  - `catch` unique-violation остаётся общим (покрывает оба индекса) — добавить комментарий.
- **Тесты (механическая правка сидов, без изменения утверждений):**
  - `Infrastructure/PrecategorizedCommodityRepositoryTests.cs`: helper `Proposal(...)` заполняет
    `NormalizedName = CommodityNameNormalizer.NormalizeName(name)`; raw-SQL `INSERT` в тесте
    `GetCategoriesByExactNamesAsync_returns_latest_row_and_skips_invalid_categories` дополняется
    колонкой `normalized_name`;
  - `Infrastructure/PrecategorizedCommodityWorkflowTests.cs`: тот же helper.
- **Новые тесты** (в том же файле `PrecategorizedCommodityRepositoryTests`):
  - `TryAddProposalAsync` пишет `normalized_name == NormalizeName(name)` (вариант с ведущим EAN);
  - имя с сырым размером ≤ 256, но очищенным/`lowercase` > 256 (например, 200 символов `İ`) →
    `false`, предупреждение, строка не вставлена;
  - сырое имя > 256 → `false` (прежнее поведение не сломано).

### Что НЕ входит
- Чтение/поиск по ключу, `HasBlockingQueueEntryAsync`, удаление мёртвой sibling-ветки из
  `ConfirmAsync` (T-04, T-05).
- Паритетный тест SQL ↔ C# и golden corpus (T-03).
- **Применение миграции** на dev/prod — это шаг эксплуатации, а не код (раздел «Порядок применения
  миграции»); в задаче только скрипт и его комментарии.
- Изменения API/UI/контрактов; backfill `commodities`/журнала.

### Тесты / ручная проверка
- Автотесты выше (Testcontainers Postgres — схема из EF-модели, колонка и индексы видны).
- Ручная проверка (dev, после применения миграции): `\d precategorized_commodities` показывает
  колонку `NOT NULL`, оба новых индекса и прежние `ux_..._name_merchant`,
  `ix_..._status`.

### Критерии готовности
- `dotnet test` зелёный; существующие тесты очереди меняются только в части сидов.
- EF-модель и SQL-скрипт описывают одинаковую схему (имена колонки/индексов совпадают).
- Поведение всех эндпоинтов и фона не изменилось: чтение по-прежнему по точному имени.
- В коде ровно одно место записи `normalized_name` — `TryAddProposalAsync`.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"
cd Analytics && dotnet test
```

---

## T-03. Golden corpus + Testcontainers-паритет SQL-backfill ↔ C# + контрольные запросы

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** T-02

### Цель
Закрыть риск дрейфа одноразовой SQL-копии правила относительно C# (ADR-034 п. 2 «Идемпотентность
и дрейф», открытый вопрос 6): единый golden corpus, постоянный тест, применяющий **реальный файл
миграции**, и зафиксированные контрольные запросы для эксплуатации. Это единственная задача, где
проверяется соответствие backfill'а правилу.

### Что входит
- **Golden corpus** — `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityNameNormalizationCorpus.cs`:
  `IReadOnlyList<(string Input, string Expected)>`, ≥ 20 кейсов: все примеры ADR-034 п. 1, границы
  (5/6 цифр, кластеры, `*#№`, fallback «только код»), зафиксированные ограничения (дефис, запятая,
  код в середине). Corpus — единственный источник кейсов для обоих тестов (никакого дублирования
  ожиданий).
- **Рефакторинг** `CommodityNameNormalizerTests`: theory по corpus + отдельные факты (null,
  детерминизм, существующие кейсы). Ожидания не меняются — только источник данных.
- **Новый тест** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/PrecategorizedNormalizedNameMigrationTests.cs`
  (Testcontainers Postgres, по образцу `PrecategorizedCommodityRepositoryTests` /
  `CategorizationDecisionRepositoryTests`):
  1. создать схему `EnsureCreatedAsync()` по EF-модели;
  2. привести таблицу к «до-миграционному» виду: `ALTER TABLE precategorized_commodities DROP COLUMN
     normalized_name` (индексы по колонке удаляются вместе с ней; имена зафиксированы в EF-конфиге
     и SQL — конфликта при повторном создании нет), комментарий почему;
  3. вставить 2 `applied` и 2 `proposed` строки (имена из corpus) через raw SQL по «старым» колонкам;
  4. выполнить **файл скрипта из репозитория** целиком (Npgsql, один сеанс — `pg_temp` работает);
     файл копируется в output тестовой сборки (`<None Include="..\..\src\ReceiptCollector.Analytics.Migrations\Scripts\20261109120000_add_precategorized_normalized_name.sql" Link="Scripts\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />` в
     `ReceiptCollector.Analytics.Api.Tests.csproj`) — путь не хардкодится в тесте;
  5. утверждения: `proposed` удалены; `applied` сохранены в прежнем количестве; для каждой
     `applied`-строки `normalized_name == CommodityNameNormalizer.NormalizeName(name)` (по corpus);
     `INSERT` без `normalized_name` падает с `23502`; оба индекса есть (`pg_indexes`);
     `pg_temp`-функция после сеанса недоступна.
  - Preflight `RAISE EXCEPTION` отдельным тестом **не покрывается**: воспроизводимое расширение
    строки при `lower()` в PostgreSQL иное, чем `ToLowerInvariant()` в .NET (см. допущения
    ADR-034); в тесте — комментарий с причиной.
- **Контрольные запросы после миграции** — текст фиксируется в этом плане (раздел «Ответ на
  открытый вопрос №6 ADR»); в задаче они проверяются на dev-стенде после применения миграции и
  вносятся в сводку `docs/reviews/commodity-name-normalization/` (постоянный runbook — T-09).
- **CI-влияние:** +1 класс Testcontainers Postgres (один контейнер `postgres:16-alpine` на класс),
  ≈ 5–10 с к общему времени `dotnet test`; в CI Docker уже доступен, классы выполняются
  параллельно — на общее время влияет незначительно (прецедент: 9 классов с Testcontainers).

### Что НЕ входит
- Изменения продуктового кода и самой миграции (только тест и corpus; найденное расхождение —
  правка скрипта в рамках этой же задачи **или** эскалация к @software-architect, если расхождение
  означает изменение правила).
- Проверка идемпотентности `MigrationRunner` (учёт в `migration_scripts_history` — его свойство,
  покрыто прецедентами; повторный прогон в тесте не моделируется).
- SQL-функция в постоянной схеме (запрещено решением A2).

### Тесты / ручная проверка
- Новый тест + существующие тесты нормализатора на corpus.
- Ручная (dev, после применения миграции): контрольные запросы из раздела «Ответ на открытый
  вопрос №6 ADR»; ожидаемые результаты там же.

### Критерии готовности
- Паритетный тест зелёный на реальном файле миграции; при умышленном изменении одного regex в
  скрипте тест падает (проверить локально и откатить).
- Corpus используется и тестами нормализатора, и паритетным тестом (одна точка правды).
- `dotnet test` зелёный; время полного прогона выросло не более чем на ~15 с.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityNameNormalizer|FullyQualifiedName~PrecategorizedNormalizedNameMigration"
cd Analytics && dotnet test
```

---

## T-04. Repository: `HasBlockingQueueEntryAsync` + удаление мёртвой sibling-ветки из confirm

> **Правка 2026-11-09:** объём сокращён ревизией ADR-034 D1 после ревью T-02 (сводка
> [`02-schema-normalized-name.md`](../reviews/commodity-name-normalization/02-schema-normalized-name.md),
> раздел «Эскалация до старта T-04»).

**Приоритет:** P0 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** T-02

### Цель
Дать слою хранения C# pre-check кандидатов фона по очищенному ключу (решение C1 — без изменений) и
**удалить** из `ConfirmAsync` ветку удаления «сестринских» `proposed`-строк по сырому имени.
Миграция T-02 (коммит `4695f69`) удалила все `proposed` и создала глобальный частичный unique
`ux_precategorized_commodities_normalized_proposed (normalized_name) WHERE status = 0`, поэтому
состояние «sibling `proposed` с одним очищенным ключом» недостижимо, а delete-ветка — мёртвый код
(ADR-034 **D1** «sibling delete удаляется как мёртвая ветка — выбран», **D4** «defense-in-depth —
отклонено», п. 4.5). Остальная семантика confirm не меняется: `commodities` — по **точному сырому**
имени и только строки без категории (NFR-1), та же транзакция, те же статусы и коды
`Applied`/`Conflict`/`NotFound`. Поиск для suggest пока **не меняется** (переключение — T-05, чтобы
промежуточное состояние не ломало очередь).

### Что входит
- **Domain:** `Modules/Commodities/IPrecategorizedCommodityRepository.cs`
  - новый метод `Task<bool> HasBlockingQueueEntryAsync(string normalizedName, Guid merchantId,
    CancellationToken ct = default)` c XML-doc: «строка с тем же `normalized_name` у этого магазина
    в любом статусе **или** любая `proposed`-строка с тем же ключом у любого магазина» (C1 — без
    изменений);
  - `ConfirmAsync`-док: убрать упоминание sibling delete и описать три шага ADR-034 п. 4.5 —
    `UPDATE commodities` по точному сырому имени без категории → `status = applied` + `applied_at`
    → commit; «сестринские `proposed` не ищутся и не удаляются: глобальный partial unique по
    `normalized_name` гарантирует, что подтверждаемая строка — единственная `proposed` своего
    очищенного ключа (D1)».
- **Infrastructure:** `Persistence/Postgres/PrecategorizedCommodityRepository.cs`
  - `HasBlockingQueueEntryAsync` — один индексируемый запрос:
    `p.NormalizedName == key && (p.MerchantId == merchantId || p.Status == Proposed)`
    (`AsNoTracking().AnyAsync`), guard на пустой ключ как у `GetKnownCategoryByNameAsync`;
  - `ConfirmAsync`: **удалить** ветку удаления sibling `proposed` по сырому имени; ветку по
    `normalized_name` **не вводить** (D1/D4). Ничего не заменять: `UPDATE commodities` по точному
    сырому имени без категории (NFR-1/решение D1), `status = applied` + `applied_at`, та же
    транзакция и статусы; отклонение (`DELETE .../{id}`) — как сейчас, ровно одна строка по `id`.
- **Тесты:** `Infrastructure/PrecategorizedCommodityRepositoryTests.cs` (Testcontainers):
  - `HasBlockingQueueEntryAsync`: блокирует `applied` того же магазина; блокирует `proposed` того же
    магазина; блокирует `proposed` другого магазина; **не** блокирует `applied` другого магазина;
    `false` при другом ключе и на пустой таблице;
  - `..._matches_by_cleaned_key`: строка хранится с ключом `туалетная бумага "сваё" (3сл, 4р)`,
    запрос — с ключом варианта `2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530`;
  - `ConfirmAsync_updates_commodities_only_by_exact_raw_name` (регресс D1): товар-вариант без кода
    не получает категорию при подтверждении строки с кодом;
  - **исключено из плана:** кейс `ConfirmAsync_deletes_sibling_proposed_by_normalized_key` —
    посев двух `proposed` с одним очищенным ключом **невыполним in principle** (вторая вставка
    отклоняется `23505` по `ux_precategorized_commodities_normalized_proposed`), а тест с
    искусственным сидом в обход собственного индекса запрещён ADR-034 D4. Регресс-контроль
    очищенного ключа уже есть в T-02 —
    `TryAddProposalAsync_returns_false_when_proposed_with_same_normalized_key_exists_at_another_merchant`
    (уникальный `proposed` на ключ); тестом T-04 не дублируется;
  - **правка существующих тестов — меняются утверждения:**
    `ConfirmAsync_updates_commodities_of_all_users_and_removes_sibling_proposals` и workflow-аналог
    `Confirm_updates_two_users_marks_applied_removes_siblings_and_fills_cache`. После удаления ветки
    «сестринская» proposed (искусственный сид T-02 с явным `normalizedName: "sibling"`, в проде
    недостижим) **остаётся** в таблице: `Assert.Null(FindAsync(siblingId))` → `Assert.NotNull(...)`
    и статус `Proposed`. Переписать оба теста на «confirm переводит в `applied` только свою строку,
    остальные не трогает» — имена без `removes_sibling(s)` (например,
    `ConfirmAsync_updates_commodities_of_all_users_and_touches_only_confirmed_row` и
    `Confirm_updates_two_users_marks_applied_and_touches_only_confirmed_row_and_fills_cache`),
    комментарии про «переключение на ключ — T-04» удалить; сид менять не нужно (индекс он проходит);
    остальные утверждения (`UpdatedCommodities = 2`, `Conflict`/`NotFound`, наполнение кэша,
    «уже категоризированный не тронут», «`Молоко 1%` не тронута») — без изменений.
- **Тесты (механическая правка):** `Infrastructure/PrecategorizationBackgroundServiceTests.cs` —
  fake `IPrecategorizedCommodityRepository` получает реализацию `HasBlockingQueueEntryAsync`
  (по умолчанию `false`), иначе сборка тестов не компилируется.

### Что НЕ входит
- Переключение lookupa на `normalized_name` и переименование `GetCategoriesByExactNamesAsync`
  (T-05), вызовы из resolver и фона (T-05, T-06).
- **Любая замена удалённой ветки:** возврат sibling delete по сырому имени или по `normalized_name`,
  «defense-in-depth» и тесты с искусственным посевом двух `proposed` одного очищенного ключа —
  ADR-034 D1/D4. Ослабление partial unique индекса — повод пересмотреть решение отдельным ADR,
  а не страховка недостижимым кодом.
- SQL в `GetCandidatesAsync` — **без изменений** (точные NOT EXISTS остаются дополнительным
  фильтром, ADR-034 C1 «безвредная избыточность»).
- Глобальный existing-шаг фона (`GetKnownCategoryByNameAsync`) — остаётся по точному имени
  (ADR-034, открытый вопрос 4).
- Массовое обновление `commodities` по очищенному ключу — запрещено (NFR-1, D2/D3 отклонены).

### Тесты / ручная проверка
- Автотесты выше. Ручной сценарий — в T-09 (подтверждение в `/precategorized`).

### Критерии готовности
- `dotnet test` зелёный; подтверждение в `/precategorized` отвечает прежними телами/кодами.
- В `ConfirmAsync` не осталось операций удаления `proposed` (кроме отклонения строки по `id`);
  в репозитории нет запросов, нормализующих имя в SQL (только C#-ключ параметром).
- Утверждения изменены ровно в двух confirm-тестах (репозиторий + workflow) со ссылкой на ADR-034
  D1/D4 в сводке задачи; других изменений утверждений нет.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorized"
cd Analytics && dotnet test
```

---

## T-05. Resolver: очередь по очищенному ключу, ИИ по очищенному имени, переименование lookup

**Приоритет:** P0 · **Оценка:** 1.25 чел.-дн. · **Зависит от:** T-04

### Цель
Сделать очищенное имя ключом шагов `precategorized` и `ai` общего конвейера (FR-2, FR-3 для чека и
страницы «Товары»): очередь ищется по ключу, в `IAiClient` уходит ключ группы (очищенное имя),
дедупликация вызовов ИИ — по нему же. Порядок `existing → merchant-default → cache → precategorized
→ ai` и контракты не меняются.

### Что входит
- **Domain:** `IPrecategorizedCommodityRepository`: `GetCategoriesByExactNamesAsync` →
  **`GetCategoriesByNormalizedNamesAsync`** (переименование, ADR-034 п. 3); XML-doc: ключ —
  `normalized_name`, поиск в любом статусе, самая свежая строка (`applied_at ?? created_at`),
  невалидные коды отбрасываются.
- **Infrastructure:** `PrecategorizedCommodityRepository`
  - фильтр `normalizedList.Contains(p.NormalizedName)`, результат — словарь
    `normalized_name → категория` (ключ результата — ключ, а не сырое имя); остальная логика
    (свежесть, `Enum.IsDefined`, пропуск `Undefined`) без изменений;
  - guard/`Distinct(StringComparer.Ordinal)` по ключам.
- **Infrastructure:** `Modules/Commodities/CommodityCategoryResolver.cs`
  - шаг 4: собрать `Distinct` ключей `CommodityNameNormalizer.NormalizeName(item.Name)` (пустые/
    пробельные — пропускать), один batch-запрос, lookup по ключу позиции;
  - шаг 5: группировка уже по `NormalizeName`; в `IAiClient.SuggestCategoryAsync` передавать
    **`g.Key`** (очищенное имя) вместо `g.First().Name`; результат и раздача позициям — по ключу;
    в ответе `name` остаётся сырым (`CreateResult` не меняется);
  - деградация при ошибке чтения очереди (Warning → ИИ) и `OperationCanceledException` —
    без изменений.
- **Тесты (механическое переименование вызовов мока/фейка):**
  `CommodityCategorizationServiceTests`, `CommodityBatchCategorizationServiceTests`,
  `CommodityCategoryResolverTests`, `Infrastructure/PrecategorizationBackgroundServiceTests`
  (fake), `Infrastructure/PrecategorizedCommodityRepositoryTests` — только имя метода; утверждения
  не меняются.
- **Новые тесты** `CommodityCategoryResolverTests`:
  - позиция `2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530` находит очередь по ключу
    без кода → источник `precategorized`, ИИ не вызывается;
  - в `IAiClient` уходит очищенное имя (захват аргумента): для
    `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` — `пирог с вишней по ре`;
  - два варианта (с кодом и без) → **один** вызов ИИ, оба результата по ключу, `name` в ответах
    сырые;
  - приоритеты `existing` → `merchant-default` → `cache` → `precategorized` не изменились
    (существующие тесты);
  - ошибка чтения очереди → Warning и переход к ИИ (существующий тест сохраняется).

### Что НЕ входит
- Фон (`PrecategorizationBackgroundService`) — T-06.
- Тесты пакетного сервиса/чека на ключи и дедупликацию — T-07.
- Изменение `MaxUniqueNames` (остаётся по сырым именам), клиентская порционность, `SuggestTimeout`,
  `aiAvailable`, коды ошибок и `503`.
- Изменение кэша: он уже ключуется `NormalizeName` (правок нет).

### Тесты / ручная проверка
- Автотесты выше. Ручная проверка (dev): suggest чека для позиции с кодом → в логах/ответе
  источник ожидаемый, повторный suggest варианта без кода даёт `cache`/`precategorized`.

### Критерии готовности
- `dotnet test` зелёный; в коде не осталось вызовов старого имени метода.
- Ни один ответ suggest не изменился по форме; меняется только качество источника/ключа.
- Нет SQL-нормализации: ключ вычисляется в C# и передаётся параметром.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryResolver|FullyQualifiedName~CommodityCategorizationService|FullyQualifiedName~CommodityBatchCategorization"
cd Analytics && dotnet test
```

---

## T-06. Фон предкатегоризации: pre-check очереди, ИИ и кэш по очищенному ключу

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-04, T-05

### Цель
Привести фон к потоку ADR-034 п. 4.3: кандидат проверяется по очищенному ключу до existing/кэша/ИИ,
ИИ получает очищенное имя, строка очереди хранит сырое имя + ключ; настройки, рабочее окно и
single-instance допущение не меняются.

### Что входит
- **Infrastructure:** `Modules/Commodities/PrecategorizationBackgroundService.cs`,
  `ProcessCandidateAsync`:
  1. существующая проверка сырого имени (`IsNullOrWhiteSpace`/`> 256`) — без изменений;
  2. `normalized = CommodityNameNormalizer.NormalizeName(name)` — **один раз** на кандидата;
  3. `HasBlockingQueueEntryAsync(normalized, candidate.MerchantId)` → `true` — кандидат
     пропускается (курсор двигает вызывающий код); ошибка pre-check'а попадает в существующий
     per-candidate `catch` → Warning, кандидат пропускается (fail-safe, без дубля предложения);
  4. глобальный existing — по **точному сырому** имени (без изменений, ограничение зафиксировано);
  5. кэш — по `normalized`;
  6. ИИ — `SuggestCategoryAsync(normalized, …)`;
  7. `TryAddProposalAsync(candidate, …)` — получает **сырое** имя и `MerchantId` (ключ пишет
     репозиторий, T-02); unique-конфликт по любому индексу — прежний no-op.
- **Тесты:** `Infrastructure/PrecategorizationBackgroundServiceTests.cs` (fake-репозиторий):
  - pre-check `true` → ИИ/кэш/TryAdd не вызываются, курсор сдвинут, сессия продолжается;
  - pre-check `false` → прежний поток;
  - pre-check бросает исключение → Warning, позиция пропущена, сессия продолжается;
  - `SuggestCategoryAsync` получает очищенное имя (захват аргумента) для имени с ведущим EAN;
  - `TryAddProposalAsync` получает **сырое** имя (аргумент);
  - applied другого магазина (`HasBlockingQueueEntryAsync == false`) не блокирует (fake-уровень;
    EF-семантика — T-04);
  - регресс: `BatchLimit`, пауза `IntervalBetweenCalls` только после вызова ИИ, рабочее окно,
    `Enabled=false`, `AI:BaseUrl` пуст — без изменений.

### Что НЕ входит
- `GetCandidatesAsync` (raw SQL) и `GetKnownCategoryByNameAsync` — без изменений.
- Глобальный existing по очищенному ключу (потребует нормализации `commodities`) — открытый
  вопрос 4, вне объёма.
- Новые настройки, распределённая блокировка, ретеншен.
- Пакетный/чек-suggest (T-05, T-07).

### Тесты / ручная проверка
- Автотесты выше.
- Ручная (dev): после миграции и старта фона в очереди появляются строки с `normalized_name`,
  у варианта с кодом повторного предложения не создаётся.

### Критерии готовности
- `dotnet test` зелёный; настройки `Precategorization:*` не менялись.
- В ИИ уходит только очищенное имя; сырое имя сохраняется в строке очереди и логах.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~Precategorization"
cd Analytics && dotnet test
```

---

## T-07. Пакетный suggest и suggest чека: проверка ключей без изменения контрактов

**Приоритет:** P1 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** T-05

### Цель
Подтвердить тестами критерии приёмки FR-2/FR-3 для страницы «Товары» и чека после T-05 (ADR-034
п. 4.1–4.2): варианты одного товара дают один ключ кэша/дедупликации, второй ответ приходит из
кэша/очереди, контракты и лимиты не меняются. Продуктовый код, скорее всего, не меняется — задача
преимущественно тестовая; если тест вскрывает расхождение, правка делается здесь же без изменения
контрактов.

### Что входит
- **Тесты:** `CommodityBatchCategorizationServiceTests.cs`:
  - два товара `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` и `ПИРОГ С ВИШНЕЙ ПО РЕ` в одной порции →
    **один** вызов `IAiClient.SuggestCategoryAsync`, оба результата одинаковы, `name` в ответах
    сырые;
  - `MaxUniqueNames` по-прежнему считается по **сырым** именам (две сырые формы = 2; проверка
    порога 20 не сломана);
  - `SuggestTimeout`/частичный `200`/`aiAvailable`/`updated`/assign-only — регресс без изменений.
- **Тесты:** `CommodityCategorizationServiceTests.cs` (чек):
  - suggest позиции с ведущим EAN/артефактом → в ИИ очищенное имя (захват аргумента), источник
    `ai`, `name` в результате сырое;
  - второй вариант без кода → `cache` на очищенном ключе, ИИ не вызывается;
  - пустой `AI:BaseUrl` → прежний `503`; ошибки/коды не меняются.
- При необходимости — правки только внутри `Infrastructure/.../*Service` для приведения к ключу
  (ожидается: не требуются), с записью в сводке «код не менялся / менялся, почему».

### Что НЕ входит
- Изменение DTO/эндпоинтов, `MaxUniqueNames`, чанкинга (клиент), `SuggestTimeout`.
- Frontend и любые UI-изменения.
- Новые тесты на нормализатор (T-01/T-03), resolver (T-05), фон (T-06).

### Тесты / ручная проверка
- Автотесты выше.
- Ручная (dev): страница «Товары» — два варианта одного товара в одной порции suggest дают
  одинаковую категорию/источник; сохранение работает как раньше.

### Критерии готовности
- `dotnet test` зелёный; ни один существующий тест не изменён по утверждениям, кроме добавленных.
- Ответы `POST /api/receipts/{id}/categories/suggest` и
  `POST /api/commodities/categories/suggest` не изменены по форме/кодам.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityBatchCategorization|FullyQualifiedName~CommodityCategorizationService"
cd Analytics && dotnet test
```

---

## T-08. Журнал решений: регресс (сырое имя + очищенный `normalized_name`)

**Приоритет:** P1 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** T-01

### Цель
Подтвердить FR-4/F1 (ADR-034 п. 4.4): код `CategorizationDecisionRecorder` не меняется, но новые
события обязаны хранить сырое `commodity_name` и очищенный `normalized_name`; накопленная история
не пересчитывается. Задача тестовая.

### Что входит
- **Тесты:** `CategorizationDecisionRecorderTests.cs`:
  - черновик с `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` → событие: `CommodityName` сырое,
    `NormalizedName == "пирог с вишней по ре"`;
  - имя из одного кода (`*3455614`) → `NormalizedName` не пустой (fallback), guard 256 не нарушен;
  - имя длиной 256 с ведущим кодом → запись проходит, `NormalizedName` ≤ 256;
  - два варианта (с кодом/без) → два события с **одинаковым** `NormalizedName` и разными
    `CommodityName` (ключ сочетания ADR-033 остаётся сырым);
  - существующие кейсы валидации/«никогда не бросает» — без изменения утверждений.
- **Проверка отсутствия пересчёта:** в задаче нет миграции/скрипта/`UPDATE`
  `categorization_decisions`; в сводке фиксируется, что история не пересчитывается (FR-4).
- **Тест (Repository/API не требуются):** достаточно unit-тестов recorder'а; новых файлов, кроме
  правок в существующем тесте, не появляется.

### Что НЕ входит
- Пересчёт истории, ретеншен, изменение ключа сочетания (ADR-033 F2 отклонено).
- Изменения схемы `categorization_decisions`.
- Точки фиксации (чек/«Товары»/confirm) — поведение не меняется.

### Тесты / ручная проверка
- Автотесты выше.
- Ручная (dev): после suggest+сохранения на чеке новое событие содержит сырое `commodity_name` и
  очищенный `normalized_name`; старые строки не изменились.

### Критерии готовности
- `dotnet test` зелёный; продуктовый код журнала не изменён (правки — только тесты).
- В диффе нет `UPDATE categorization_decisions` и нет изменений миграций журнала.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~CategorizationDecisionRecorder"
cd Analytics && dotnet test
```

---

## T-09. Финальная проверка, релизная инструкция, ручная приёмка

**Приоритет:** P0 · **Оценка:** 0.75–1.0 чел.-дн. · **Зависит от:** T-01…T-08

### Цель
Подтвердить сквозной результат и отсутствие регрессий (NFR-1…NFR-4), зафиксировать порядок
применения миграции и контрольные запросы для эксплуатации, собрать итоговую сводку.

### Что входит
- `cd Analytics && dotnet test` — все проекты, включая `Architecture/ProjectDependencyTests` и
  паритетный тест миграции.
- Ручная проверка сценариев задачи (dev-стенд):
  1. чек: позиция `2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ` → suggest, в ИИ очищенное имя; вторая
     позиция `ПИРОГ С ВИШНЕЙ ПО РЕ` → категория из кэша/очереди, ИИ не вызывается;
  2. страница «Товары»: `2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530` и
     `Туалетная бумага "СВАЁ" (3сл, 4р)` схлопываются в один ключ порции;
  3. очередь: поиск (через suggest) находит строку по очищенному ключу; новых дублей по ключу нет
     (частичный уникальный индекс работает);
  4. миграция: `proposed` отсутствуют, `applied` сохранены и backfill'ены; фон заполняет очередь
     заново;
  5. журнал: новые события — сырое `commodity_name` + очищенный `normalized_name`; история не
     пересчитана;
  6. контракты: `503` при пустом `AI:BaseUrl` (чек), `200` + `aiAvailable=false` (страница),
     `400/401/403/404/409` и `updated` не изменились; автоприменений нет.
- **Документ для эксплуатации:** `docs/runbooks/commodity-name-normalization-deploy.md` —
  пошаговый порядок (дамп → остановка Analytics → миграции → новый образ), контрольные запросы,
  откат (включая «после `SET NOT NULL` простой откат образа невозможен»), ожидаемые результаты.
- **Сводка:** `docs/reviews/commodity-name-normalization/` — результаты прогонов, ручных сценариев,
  контрольных запросов, остаточные риски и открытые вопросы ADR-034 (1–5, 7), которые не решались.
- Проверка отсутствия изменений вне Analytics: `git status --short` — нет правок в `backend/`,
  `bot/`, `docker-compose*.yml`, `.env*`, `AGENTS.md`, `Analytics/frontend/`.

### Что НЕ входит
- Новые функции (ретеншен, экспорт, агрегаты, отображение ключа в админке — открытый вопрос 7).
- Пересчёт истории журнала, массовое применение при confirm, `ё → е`, token-based эвристики.
- Замеры производительности как блокирующий критерий (только фиксация наблюдений).
- Правки архитектуры на ходу: несоответствие ADR — эскалация к @software-architect.

### Тесты
- Только прогон существующих наборов; новых тестов задача не добавляет. Найденные дефекты
  исправляются в соответствующей задаче (T-01…T-08) отдельным циклом.

### Критерии готовности
- `dotnet test` (Analytics, все проекты) зелёный; паритетный тест миграции зелёный.
- Все пункты «Критерии приёмки» задачи отмечены и подтверждены ручной проверкой.
- Runbook и сводка лежат по указанным путям; контрольные запросы выполнены на dev с ожидаемым
  результатом.

### Проверка
```bash
cd Analytics && dotnet test
git status --short
```

---

## Порядок применения миграции (эксплуатация, не код)

Порядок обязателен и переносится в runbook T-09; в скрипте миграции (T-02) он продублирован
комментарием. Источник — ADR-034 п. 2 «Порядок деплоя».

1. **Дамп:** `pg_dump -t precategorized_commodities …` (таблица небольшая) или `./backup.sh` — для
   откатa и восстановления удалённых `proposed`.
2. **Остановить Analytics:** `docker compose stop analytics` (контейнер `receipt-analytics`).
   Альтернатива для фона — `Precategorization__Enabled=false` + рестарт, но для одного инстанса
   проще остановить сервис целиком.
3. **Применить миграции:** `docker compose run --rm analytics-migrations` (или
   `docker compose up analytics-migrations`).
4. **Запустить новый образ:** `docker compose up -d analytics` — образ уже содержит код T-02…T-08,
   который пишет `normalized_name`.

Почему нельзя иначе: между `ADD COLUMN` и `SET NOT NULL` старый код вставляет `proposed`-строки с
`normalized_name = NULL`, и `SET NOT NULL` падает (миграция откатывается — безопасно, но деплой
повторяется); после `SET NOT NULL` старый код падает на вставке и «шумит» Warning'ами. На dev-стенде
миграцию применять **после** коммита T-02 (либо при выключенном фоне).

**Откат:** до миграции — вернуть прежний образ (схема не менялась). После успешной миграции простой
откат образа невозможен (старый код не заполняет NOT NULL колонку): либо восстановить дамп
`precategorized_commodities`, удалить новые индексы и колонку и вернуть прежний образ, либо
forward-fix новым образом. `commodities` и `categorization_decisions` миграция не меняет.

---

## Ответ на открытый вопрос №6 ADR: верификация SQL-backfill

**Решение:** трёхуровневая схема, постоянный Testcontainers-тест — **да**.

1. **Golden corpus (C#) — постоянный.** Один файл `CommodityNameNormalizationCorpus.cs` с
   кейсами из ADR-034 п. 1 (включая границы и зафиксированные ограничения). Используется и
   юнит-тестами нормализатора (T-01/T-03), и паритетным тестом миграции — ожидания не дублируются.
2. **Постоянный Testcontainers-тест (T-03) — добавляем.** Класс
   `PrecategorizedNormalizedNameMigrationTests` применяет **реальный файл** миграции на
   PostgreSQL 16 и сравнивает backfill'енные `normalized_name` `applied`-строк с
   `CommodityNameNormalizer.NormalizeName` по всему corpus; дополнительно проверяет удаление
   `proposed`, сохранность `applied`, `NOT NULL` и наличие обоих индексов.
   - **Совместимость с CI:** CI (`build-analytics-image.yml`) уже запускает `dotnet test` при
     доступном Docker, в наборе 9 классов с Testcontainers (прецеденты
     `PrecategorizedCommodityRepositoryTests`, `CategorizationDecisionRepositoryTests`,
     `PrecategorizedCommodityWorkflowTests`). Один дополнительный класс = один контейнер
     `postgres:16-alpine` (~5–10 с на старт/применение скрипта); xUnit выполняет классы
     параллельно, поэтому вклад в общее время CI ≈ 5–15 с. Бюджет приемлем.
   - **Почему не «только ручной запрос»:** скрипт живёт в репозитории и может быть отредактирован
     до релиза; дрейф между C# и SQL в backfill иначе обнаружится только на проде (исторические
     `applied`-строки получили бы неверный ключ, а `proposed` уже удалены — молчаливая потеря).
     Тест стоит дешевле одного инцидента и не требует новых зависимостей (Testcontainers уже в
     проекте).
   - **Ограничение:** preflight `RAISE EXCEPTION` тестом не покрывается — воспроизводимое
     расширение строки при `lower()` в PostgreSQL иное, чем `ToLowerInvariant()` в .NET
     (допущения ADR-034); проверяется только на dev контрольными запросами.
3. **Контрольные запросы после миграции (обязательный шаг эксплуатации, T-03/T-09).**

```sql
-- 1. Инвариант ключа: пустых/пропущенных ключей нет. Ожидается 0.
SELECT count(*) FROM precategorized_commodities
 WHERE normalized_name IS NULL OR btrim(normalized_name) = '';

-- 2. FR-5: неподтверждённых предложений не осталось (до старта фона). Ожидается 0.
SELECT count(*) FROM precategorized_commodities WHERE status = 0;

-- 3. applied сохранены: до/после миграции число строк status = 1 не уменьшилось.
SELECT status, count(*) FROM precategorized_commodities GROUP BY status ORDER BY status;

-- 4. Ключ не содержит ведущих/хвостовых кодов (для существующих строк). Ожидается 0.
SELECT count(*) FROM precategorized_commodities
 WHERE normalized_name ~ '^(?:[[:space:]]*[0-9]{6,})+'
    OR normalized_name ~ '(?:^|[[:space:]])[0-9]{6,}(?:[[:space:]]+[0-9]{6,})*[[:space:]]*$';

-- 5. Spot-check по примерам задачи (для строк, реально присутствующих в базе):
SELECT name, normalized_name
  FROM precategorized_commodities
 WHERE name IN ('2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ',
                '2195738 Туалетная бумага "СВАЁ" (3сл, 4р) 4810981001530',
                '*3455614 РЖ.КРАЙ Хлеб нар.300г');

-- 6. Схема: колонка NOT NULL и оба индекса на месте.
\d precategorized_commodities
```

Ожидаемые значения: п. 1, 2, 4 — `0`; п. 3 — `status = 0` отсутствует, `status = 1` не меньше
дорелизного; п. 5 — `пирог с вишней по ре`, `туалетная бумага "сваё" (3сл, 4р)`,
`рж.край хлеб нар.300г`; п. 6 — `normalized_name varchar(256) NOT NULL`,
`ix_precategorized_commodities_normalized_name`, `ux_precategorized_commodities_normalized_proposed`.

**Что НЕ делаем:** постоянную SQL-функцию нормализации (отклонено A2 — тестовая схема создаётся из
EF-модели, функции в ней не было бы), нормализацию в runtime-запросах (отклонено B2), пересчёт
истории журнала (запрещено FR-4).

---

## Трассировка требований → задачи

| Требование | Задачи |
|---|---|
| FR-1 (правило очистки, границы, fallback) | T-01 (реализация и юнит-тесты), T-03 (corpus), T-02 (ключ при вставке), T-03 (backfill) |
| FR-2 (ИИ получает очищенное имя во всех suggest) | T-05 (чек и «Товары» — resolver), T-06 (фон), T-07 (проверка), T-09 (ручная приёмка) |
| FR-3 (единый ключ: кэш, дедупликация, очередь, группировка, журнал) | T-02 (колонка/запись), T-04 (pre-check по очищенному ключу; sibling delete удалён как мёртвый код — D1), T-05 (lookup/дедуп/группировка), T-06 (фон), T-07 (кэш/дедуп страницы), T-08 (`normalized_name` журнала) |
| FR-4 (сырые данные и история не теряются) | общий инвариант 2, T-04 (confirm по точному имени), T-08 (регресс журнала), T-09 |
| FR-5 (миграция: удалить `proposed`, сохранить `applied`, пустая таблица, идемпотентность) | T-02 (скрипт + порядок), T-03 (паритетный тест + контрольные запросы), T-09 (проверка на dev) |
| NFR-1 (нет автопроставления) | T-04 (D1: `commodities` — только по точному сырому имени; sibling delete удалён как мёртвая ветка, D4 «defense-in-depth» отклонено), T-05, T-06, T-07 (поведение не меняется), T-09 |
| NFR-2 (интерфейс и контракты API не меняются) | T-05, T-06, T-07 (без правок DTO/эндпоинтов), T-09 (сверка кодов/тел) |
| NFR-3 (детерминизм, без побочных эффектов, несущественные накладные расходы) | T-01 (чистая функция, `GeneratedRegex`/`NonBacktracking`), T-04 (один индексируемый запрос на кандидата), T-05 (один batch-запрос), T-09 |
| NFR-4 (существующие тесты зелёные) | все задачи (инвариант 7), T-09 (финальный прогон) |
| Критерии приёмки задачи | T-01 (юнит-тесты и границы), T-02/T-03/T-09 (миграция), T-04/T-05 (pre-check и поиск по очищенному ключу), T-05/T-07 (ИИ и равенство ключей), T-08 (журнал), T-09 (`dotnet test`, контракты, отсутствие автоприменений) |
| Edge cases задачи | T-01 (все кейсы), T-03 (те же кейсы в backfill), T-09 (ручная проверка «только код»/короткие числа) |
| ADR-034 п. 2 «дрейф SQL ↔ C#», открытый вопрос 6 | T-03, раздел «Ответ на открытый вопрос №6 ADR» |

## Риски и точки эскалации

| Риск | Где | Действие |
|---|---|---|
| Миграция применена раньше кода, который пишет `normalized_name` (`SET NOT NULL` / «шум» фона) | T-02, T-09 | Порядок из раздела «Порядок применения миграции»: дамп → остановка Analytics → миграции → новый образ; на dev применять после коммита T-02 |
| Дрейф SQL-backfill ↔ C# | T-03 | Общий golden corpus, паритетный Testcontainers-тест на реальном файле скрипта, контрольные запросы на dev |
| `varchar(256)` переполняется после `lowercase` | T-02, T-03 | Preflight в миграции (`RAISE`), guard сырого и очищенного имени в `TryAddProposalAsync`; тест на 200×`İ` |
| Разные товары слиплись по очищенному ключу, кэш отдаёт неверную категорию (first-wins) | T-05, T-06, T-09 | Подтверждение человеком, автоприменения нет; правка категории/перезапуск; открытый вопрос 2 ADR — продуктовое решение, не блокирует |
| Один `proposed` на ключ глобально: вариант с другим кодом не получает отдельного предложения | T-02, T-06 | Осознанное следствие FR-3.4/ADR-027; админ размечает вручную; открытый вопрос 3 ADR |
| Глобальный `existing` фона остаётся по сырому имени | T-06 | Зафиксированное ограничение (открытый вопрос 4); кэш смягчает разрыв |
| Промах очереди из-за неверного ключа (перепутаны сырое/очищенное имя) | T-05, T-06 | Тесты с захватом аргументов ИИ и lookup по ключу; единая функция нормализации — единственный источник |
| Переименование метода интерфейса ломает моки в 4 тестовых файлах | T-05 | Механические правки перечислены в задаче; утверждения не меняются |
| Паритетный тест хрупок (имена индексов/`DROP COLUMN`) | T-03 | Имена индексов зафиксированы в EF-конфиге и SQL; `DROP COLUMN` удаляет индексы вместе с колонкой; причина закомментирована в тесте |
| Рост времени CI | T-03 | +1 контейнер на класс, ≈ 5–15 с; при превышении бюджета — эскалация к @software-architect (не удалять тест самостоятельно) |
| Расхождение реализации с ADR/неоднозначность семантики | любая | Остановка и эскалация к @software-architect; самостоятельные архитектурные решения запрещены |
| Открытые вопросы ADR-034 (1: буквальный `*3455614`; 2: политика коллизий кэша; 3: массовое применение при confirm; 5: разбитые коды; 7: показ ключа в админке) | — | Вне объёма этого плана; текущая трактовка — ADR-034 п. 1/п. 5; изменения — только отдельным решением |

## Вне объёма (повтор из задачи)

- Пересчёт истории журнала (старые `normalized_name` остаются).
- Унификация `ё → е` и иные лингвистические правила (пунктуация, опечатки).
- Снятие кодов в середине названия и любые эвристики сверх правила FR-1.
- Изменение UI, экспорт, счётчики, агрегаты.
- Автопроставление категорий; ретеншен журнала.
- Изменения Go-сервисов (`backend/`, `bot/`), MongoDB, nginx, frontend, API-контрактов.
- Новые настройки и новые зависимости.
