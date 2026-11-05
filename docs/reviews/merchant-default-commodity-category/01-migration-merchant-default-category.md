# Задача 01 - Миграция схемы: колонки дефолта и источника категории

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-01-миграция-схемы-колонки-дефолта-и-источника-категории
- **Дата**: 2026-11-05
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Схема PostgreSQL подготовлена под функциональность «категория товаров по умолчанию для
магазина» (ADR-032, решения A1 и B1). Добавлены две аддитивные nullable-колонки:
`merchants.default_commodity_category_id` (код товарной категории, заданный админом как
дефолт; CHECK запрещает 0/Undefined) и `commodities.category_source` (persisted-источник
присвоения категории, единственное значение на сегодня — `'merchant-default'`).
Backfill исторических строк сознательно не выполняется (`NULL` = manual/legacy), индексы
не добавляются — дожим будет опираться на существующие `ix_receipts_merchant_id` и
`ix_commodities_receipt_id`. Обе колонки nullable и аддитивны, поэтому старый код работает
с новой схемой без изменений (rolling deploy). Никакого C#/domain-кода задача не трогала.

## Изменённые файлы
- `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261105130000_add_merchant_default_commodity_category.sql` — новый скрипт миграции: `ALTER TABLE merchants ADD COLUMN default_commodity_category_id integer NULL` + `CHECK ck_merchants_default_commodity_category (col IS NULL OR col <> 0)`, `ALTER TABLE commodities ADD COLUMN category_source varchar(32) NULL`; заголовок-комментарий с отсылками к ADR-032/ADR-027/ADR 005 и обоснованием отсутствия FK/backfill/индексов.
- `docs/plans/merchant-default-commodity-category.md` — статус задачи 01 в «Ходе выполнения» (pending → ... → approved), пункты чек-листа T-01 отмечены выполненными.

## Как проверено
- `cd Analytics && dotnet build` — успешно, 0 ошибок (30 предупреждений NU1902/NU1903 о уязвимостях пакетов — предсуществующие, не related).
- `dotnet build src/ReceiptCollector.Analytics.Migrations/ReceiptCollector.Analytics.Migrations.csproj` — успешно; скрипт копируется в `bin/Debug/net10.0/Scripts/` (правило `Scripts\**\*` в csproj).
- `cd Analytics && dotnet test` — Passed: 353, Failed: 0, Skipped: 0 (net10.0), без правок существующих тестов.
- Прогон миграции на изолированном PostgreSQL (одноразовый Docker-контейнер `postgres:16-alpine`, НЕ dev-стенд): полная цепочка скриптов `Scripts/` применена самим `MigrationRunner` (`dotnet run` с переопределением `Infrastructure:Postgres:ConnectionString`) — новый скрипт применился без ошибок.
- Идемпотентность: повторный запуск раннера — `Skipping already applied script 20261105130000_add_merchant_default_commodity_category.sql`.
- `\d merchants` / `\d commodities` в psql: колонка `default_commodity_category_id integer` (nullable) + `CHECK ck_merchants_default_commodity_category`; `category_source varchar(32)` (nullable).
- Поведение CHECK на вставках: код `0` отклонён, `5` и `NULL` приняты; `category_source` по умолчанию `NULL`, значение `'merchant-default'` записывается и читается; вставка merchant'а без указания новых колонок (путь старого кода) работает.
- Имена колонок/констрейнта не конфликтуют ни с одним существующим скриптом в `Scripts/` (grep).

## Итоги ревью
- Замечаний ревьюера не было: вердикт APPROVED с первого раунда, blockers отсутствуют.
- Исправлено по замечаниям ревьюера: ничего.
- Оставлено без изменений (с обоснованием): не требовалось.

## Заметки для коммита
- Рекомендуемое сообщение коммита: `migrations: merchants.default_commodity_category_id + commodities.category_source (ADR-032)`
- НЕ включать в коммит `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.Development.json` — там локальные
  креды и персональный admin TelegramId (изменения в рабочем дереве предсуществующие, к задаче
  отношения не имеют; в коммит T-01 идут только файл скрипта миграции и файл декомпозиции).
- Применение миграции на dev-стенде — за человеком (остаток критерия готовности T-01):
  `cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run`; затем убедиться, что
  `migration_scripts_history` содержит `20261105130000_add_merchant_default_commodity_category.sql`
  и `\d merchants` / `\d commodities` показывают новые колонки и CHECK.
- На что обратить внимание при ручной проверке: коммитится только untracked-скрипт
  `Scripts/20261105130000_...sql` (создан на этапе реализации, ещё не был в git) и правки
  `docs/plans/...`; файлы ADR/задачи (`docs/adr/032-...`, `docs/tasks/...`) — артефакты
  предыдущих шагов конвейера, включать или нет — решение человека.
