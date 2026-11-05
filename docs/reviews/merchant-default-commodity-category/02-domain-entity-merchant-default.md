# Задача 02 - Домен и entity: дефолтная категория, безопасное чтение, batch-чтение дефолтов

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-02-домен-и-entity-дефолтная-категория-безопасное-чтение-batch-чтение-дефолтов
- **Дата**: 2026-11-05
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Магазины получили товарную категорию по умолчанию на уровне домена и persistence (ADR-032,
A1/B1): `Merchant.DefaultCommodityCategory` с методом `UpdateDefaultCommodityCategory`
(null = сброс, без enum-инварианта в домене), колонка `default_commodity_category_id` в
`MerchantEntity` и **безопасное чтение** — код вне справочника или 0 трактуется доменом как
«не задана» (edge case «категория удалена из справочника»). Добавлен batch-метод
`IMerchantRepository.GetDefaultCommodityCategoriesAsync` для будущего resolver'а (T-03/T-04):
один запрос, только валидные ненулевые дефолты, магазины без дефолта в словарь не попадают.
В `CommodityEntity` появилась колонка `category_source` (varchar 32) — будущая метка
`'merchant-default'`; доменный `Commodity` не изменён, существующие пути записи категорий
поведения не поменяли.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/Merchant.cs` — свойство
  `CommodityCategory? DefaultCommodityCategory` (optional-параметр конструктора) +
  `UpdateDefaultCommodityCategory(CommodityCategory?)`.
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/IMerchantRepository.cs` —
  контракт `GetDefaultCommodityCategoriesAsync` (по сигнатуре ADR-032 п. 2).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantEntity.cs` —
  `int? DefaultCommodityCategoryId` (сырой код, прецедент `PrecategorizedCommodityEntity`),
  маппинг domain→entity кодом, безопасное чтение в `MapToDomain`
  (`Enum.IsDefined && != 0`, иначе null).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantRepository.cs` —
  реализация batch-метода (`AsNoTracking`, пустой вход → пустой словарь); в update-ветке
  `AddAsync` — round-trip новой колонки (база для эндпоинта T-07).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/MerchantConfiguration.cs` —
  объявление колонки (`UseSnakeCaseNamingConvention` даёт `default_commodity_category_id`).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/ReceiptEntity.cs` —
  в `CommodityEntity`: `string? CategorySource` (+ XML-doc); `Create` оставляет NULL, в домен
  не проектируется (ADR-032 п. 2).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/CommodityConfiguration.cs` —
  `CategorySource` `HasMaxLength(32)` → колонка `category_source`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/MerchantEntityMappingTests.cs` —
  новый (9 тестов): маппинги, безопасное чтение (валидный/0/9999/NULL), round-trip, поведение
  domain-метода, `Create` → source NULL, parity имён колонок с миграцией 20261105130000.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/MerchantRepositoryDefaultsTests.cs` —
  новый (8 тестов): batch-метод (только валидные дефолты; NULL/0/устаревший код пропускаются),
  безопасное чтение через `GetByIdAsync`, round-trip записи через `AddAsync` (insert/изменение/
  сброс), регрессия «`UpdateCategoryAsync` не трогает дефолт», round-trip `category_source`.
  EF InMemory — прецедент `CommodityBatchRepositoryTests`.
- `docs/plans/merchant-default-commodity-category.md` — статус и чек-лист T-02.

## Как проверено

- `cd Analytics && dotnet build` — Build succeeded, 0 ошибок (30 warnings — предсуществующие
  NU1902/NU1903 по пакетам).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~Merchant"` — 43 passed, 0 failed
  (все существующие `MerchantEndpointsTests` без правок).
- `cd Analytics && dotnet test` — 371 passed, 0 failed, 0 skipped (полный набор зелёный без
  правки существующих тестов; прогон повторён после правки по замечанию ревьюера).

## Итоги ревью

- Исправлено по замечаниям ревьюера: в `MerchantEntityMappingTests.ColumnMapping_MatchesMigrationSchema`
  connection string заменён на очевидно фиктивный (`Password=test`; подключение не выполняется,
  проверяется только EF-модель) — чтобы не давать ложных срабатываний секрет-сканерам; после
  правки полный прогон — 371 passed.
- Оставлено без изменений (с обоснованием):
  - CHECK `IS NULL OR <> 0` не дублируется в EF-модели — в проекте нет ни одного
    `HasCheckConstraint`, источник целостности — SQL-миграция T-01 + безопасное чтение
    (прецедент ADR-027 п. 3); объявление CHECK в модели сломало бы сидинг невалидных кодов
    (0/9999) в тестах.
  - `CommodityEntity` правится в `ReceiptEntity.cs`, а не в отдельном `CommodityEntity.cs`,
    как предполагал план, — фактическое расположение типа в кодовой базе.
  - Round-trip новой колонки в update-ветке `MerchantRepository.AddAsync` — вне литеры
    чек-листа, но это «существующий repo-путь редактирования merchant» (ADR 011 /
    `UpdateMerchantName`), которым эндпоинт T-07 будет сохранять и сбрасывать дефолт;
    существующие вызовы передают либо новый магазин (null), либо загруженный из БД,
    перетирания нет; поведение по существующим колонкам не изменилось.

## Заметки для коммита

- Рекомендуемое сообщение коммита:
  `feat(analytics): дефолтная товарная категория магазина в домене и persistence (T-02, ADR-032)`
- **НЕ включать в коммит** `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
  `appsettings.Development.json` — в рабочем дереве есть посторонние локальные правки
  (личный AI-endpoint, TelegramId, креды Mongo). Коммитить пофайлово (только файлы T-02
  из списка выше).
- Побочный эффект к сведению: невалидный код дефолта в БД тихо нормализуется в NULL при
  любом редактировании магазина через `AddAsync` (имя, категория) — согласовано с ADR-032 A1
  как семантика «невалидный код = не задан», админ перезадаёт значение.
- Ручная проверка не требуется (API/UI — T-07…T-10); миграция T-01 уже применена на dev.
