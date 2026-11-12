# Задача 01 - Схема: SQL-миграция с сидом, EF-модель, DbSet

- **Задача**: `docs/plans/commodity-categories.md` (раздел «T-01. Схема: SQL-миграция с сидом, EF-модель, DbSet», slug `01-schema-migration-seed`); архитектура — `docs/adr/035-commodity-category-keywords.md` п. 1–3
- **Дата**: 2026-11-12
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

В PostgreSQL появилась таблица `commodity_category_keywords` — хранилище boost-/exclude-слов категоризации (одна строка = одно слово в одном списке одной категории). Схема описана дважды и одинаково: SQL-скриптом миграции и EF-моделью (сущность, конфигурация, `DbSet`), поэтому тестовая схема, которую тесты создают из EF-модели, содержит те же три CHECK и тот же уникальный индекс, что и прод. Скрипт сразу наполняет таблицу содержимым прежнего захардкоженного блока «Разграничения схожих категорий» — 167 строк одним `INSERT … ON CONFLICT DO NOTHING`. Продуктовый код слова пока не читает и не пишет: репозиторий, кэш, домен и API — задачи 03–07. Содержимое блока в коде (`BuildPayload`) в этой задаче ещё остаётся — это осознанное промежуточное состояние, хардкод удаляется в задаче 06, когда источником правды станет БД.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261112120000_add_commodity_category_keywords.sql` (новый) - создание таблицы `commodity_category_keywords` (uuid PK, `category_id integer`, `list_type varchar(16)`, `word`/`normalized_word varchar(64)`), три CHECK с именами `ck_commodity_category_keywords_category_not_undefined` (`category_id <> 0`), `..._list_type` (`list_type IN ('boost', 'exclude')`), `..._word_length` (`char_length(word) BETWEEN 2 AND 64`), уникальный индекс `ux_commodity_category_keywords_category_list_word (category_id, list_type, normalized_word)`, сид 167 строк одним `INSERT … ON CONFLICT DO NOTHING` в блоке между маркерами `-- seed:begin` / `-- seed:end`; шапка объясняет, почему CHECK (закрытое множество FR-1), почему нет FK (ADR-005), почему лимита 200 нет в схеме (A5), почему сид одноразовый и удалённое слово не воскресает (B1) и зачем нужен `ON CONFLICT DO NOTHING`.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityCategoryKeywordEntity.cs` (новый) - EF-сущность `Guid Id`, `int CategoryId`, `string ListType`, `string Word`, `string NormalizedWord`; XML-doc со ссылкой на ADR-035 п. 1 и пояснением, что категория — код enum без FK (ADR-005), а тип списка хранится wire-именем, чтобы задача 01 не зависела от домена задачи 02.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/CommodityCategoryKeywordConfiguration.cs` (новый) - таблица, длины 16/64/64, три `HasCheckConstraint` с теми же именами и выражениями, что в SQL, уникальный индекс с тем же именем; комментарий «схема дублируется в SQL, потому что тесты строят её из EF-модели».
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/ReceiptDbContext.cs` - `DbSet<CommodityCategoryKeywordEntity> CommodityCategoryKeywords` (глобальные данные, query-filter не ставится) и `ApplyConfiguration(new CommodityCategoryKeywordConfiguration())`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityCategoryKeywordSchemaTests.cs` (новый) - четыре теста: уникальность в паре «категория + список» (23505) и легальность того же слова в другом списке/категории; три CHECK с проверкой имён констрейнтов и границ длины слова; применение реального файла миграции на пустой БД (таблица, `CREATE UNIQUE INDEX` из `pg_indexes`, три констрейнта из `pg_constraint`, 167 строк сида, 94 boost / 73 exclude, точечные проверки `DriedFruits`/boost «изюм», `Fruits`/exclude «изюм», `Groceries`/boost «соль», `FitnessAndSports`/boost «абонемент», словоформа «финик» вместо «финики»); идемпотентность сида (повтор блока между маркерами не падает и не меняет количество строк).
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/ReceiptCollector.Analytics.Api.Tests.csproj` - копирование нового SQL-скрипта в output тестовой сборки (`Link="Scripts/…"`, `CopyToOutputDirectory="PreserveNewest"`), по образцу скрипта задачи ADR-034; путь в тесте строится от `AppContext.BaseDirectory`.
- `docs/plans/commodity-categories.md` - только строка задачи 01 в таблице «Ход выполнения» (`pending` → `in-progress` → `review` → `approved`).

## Как проверено

- `cd Analytics && dotnet build` - Build succeeded, 0 errors (30 предупреждений NU1902/NU1903 про уязвимые транзитивные пакеты существовали до задачи и к ней не относятся).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryKeywordSchema"` - Passed: 4, Failed: 0.
- `cd Analytics && dotnet test` - Passed: 649, Failed: 0, Skipped: 0 (около 50 с).
- Дополнительная сверка (вручную, до ревью): имена всех трёх CHECK и уникального индекса встречаются ровно по одному разу и совпадают символ в символ в SQL-скрипте и в `CommodityCategoryKeywordConfiguration`; количество строк сида в файле (167), отсутствие дубликатов `(category_id, list_type, normalized_word)` и равенство `normalized_word == lower(trim(word))` проверены разбором блока сида.
- Ревьюер независимо прогнал `dotnet build` (0 errors) и `dotnet test` (Failed: 0, Passed: 649), сверил полноту сида с таблицей ADR-035 п. 2 (множество `(category_id, list_type, word)` совпало точно; коды 3, 4, 6, 8, 10, 19, 20, 26, 28, 41, 42–53 валидны), паритет имён/выражений CHECK и индекса и соблюдение границ задачи.

## Итоги ревью

Blockers не было, вердикт APPROVED с первого раунда. Необязательные suggestions ревьюера и решение по ним:

- **S1. Именование теста `..._match_sql_migration_...` заявляет больше, чем проверяет**: тест сверяет только имена констрейнтов (`conname`), а не их выражения; предложено при желании дополнительно сверять `pg_get_constraintdef(oid)`. Оставлено без изменений: выражения CHECK дословно продублированы в SQL и в EF-конфигурации и уже проверяются тестом EF-схемы по факту отказа вставки с конкретным `ConstraintName`; добавление `pg_get_constraintdef` расширило бы дифф и связало бы тест с форматированием текста констрейнта в PostgreSQL, не добавляя покрытия инварианта.
- **S2. `Regex.Matches(..., "gen_random_uuid\\(\\)")` дублирует проверку количества строк.** Оставлено без изменений: это разные утверждения — совпадение числа кортежей в файле с ожидаемым количеством (полнота сида, ловит пропущенную/задвоенную строку до выполнения) и совпадение числа строк в БД с числом кортежей (ловит потерю строки из-за `ON CONFLICT DO NOTHING`); вместе они дают то самое «ни одна строка не потерялась», которое требует критерий готовности.
- **S3. Разбивка по 11 строкам блока живёт в XML-комментарии константы, а не в отдельных константах.** Оставлено без изменений; формулировка в отчёте разработчика уточнена: в коде зафиксированы три константы (167 / 94 / 73), а разбивка по 11 строкам блока приведена в XML-комментарии к константе `SeedRowCount`.

Осознанное промежуточное состояние (зафиксировано по критерию готовности задачи): содержимое блока «Разграничения схожих категорий» пока остаётся в `OpenAiCompatibleAiClient.BuildPayload` и одновременно появляется в БД; удаление хардкода — задача 06. Кода, который бы сидировал слова при старте (upsert или файл с примерами), в репозитории нет.

## Заметки для коммита

Рекомендуемое сообщение коммита:

```text
feat(analytics): схема ключевых слов категоризации и сид из блока «Разграничения схожих категорий»

Миграция 20261112120000_add_commodity_category_keywords.sql создаёт таблицу
commodity_category_keywords (category_id без FK, list_type boost|exclude,
word/normalized_word), три CHECK и уникальный индекс
(category_id, list_type, normalized_word) и вставляет 167 строк сида
(94 boost / 73 exclude) одним INSERT ... ON CONFLICT DO NOTHING по таблице
ADR-035 п. 2. EF-сущность, конфигурация и DbSet повторяют ту же схему,
потому что Postgres-тесты создают её через EnsureCreatedAsync.

Тесты: EF-инварианты (уникальность, три CHECK, границы длины 2–64),
применение реального файла миграции на пустой БД (таблица, индекс,
три констрейнта, полнота и точечные слова сида) и идемпотентность сида.

Задача 01 декомпозиции commodity-categories, ADR-035 п. 1–2.
```

На что обратить внимание при ручной проверке:

- **Полнота и корректность сида — главное.** Сверьте блок `-- seed:begin … -- seed:end` построчно с таблицей ADR-035 п. 2: слова субъекта → boost его категории, те же слова → exclude каждой категории-цели из «не …», перечисления в скобках → boost категории-цели, словоформы приведены к форме, входящей подстрокой (финик, стул, холодильник), смысловых дублей нет. Сверьте коды категорий с `CommodityCategory` (42 DriedFruits, 43 Sauces, 44 Spices, 45 Sausages, 46 Sweets, 47 CannedFood, 48 CraftSupplies, 49 Furniture, 50 HomeAppliances, 51 Snacks, 52 FitnessAndSports, 53 HouseholdChemicals, 19 Groceries, 20 Meat, 26 Fruits, 28 Confectionery, 3 Electronics, 10 HomeGoods, 4 CosmeticsAndHygiene, 8 StationeryAndBooks, 6 SportingGoods, 41 Entertainment).
- Совпадение имён и выражений трёх CHECK и имени уникального индекса в SQL-скрипте и в `CommodityCategoryKeywordConfiguration` (это инвариант задачи: расхождение — дефект).
- Границы задачи: в диффе не должно быть репозитория, кэша, домена (тип списка, правила, матчер), сервиса, API, правок `IAiClient`/промта/фронтенда, `categorization_decisions`, `.env.example`, `docker-compose*`, `AGENTS.md`, Go-сервисов, MongoDB и nginx; новых настроек нет.
- **Важно: в рабочем дереве есть посторонние незакоммиченные изменения, не относящиеся к задаче** — `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json`, `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json` (локальная конфигурация AI) и `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` (артефакт сборки фронтенда). Их не нужно включать в коммит: добавляйте только перечисленные выше файлы задачи (`git add` по явным путям, не `git add -A`).
- После применения миграции на dev полезно выполнить `\d commodity_category_keywords` (все колонки, три CHECK, уникальный индекс) и `SELECT list_type, count(*) FROM commodity_category_keywords GROUP BY 1` — ожидаются ненулевые `boost` (94) и `exclude` (73).
