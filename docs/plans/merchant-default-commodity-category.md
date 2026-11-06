# Декомпозиция: категория товаров по умолчанию для магазина (merchant default commodity category)

> Источники: задача [merchant-default-commodity-category](../tasks/merchant-default-commodity-category.md)
> (FR-M1…FR-M8, критерии приёмки, edge cases) и [ADR-032](../adr/032-merchant-default-commodity-category.md)
> (решения A1, B1, C1, D1, E1, F1 — единственный документ реализации).
>
> **Этот документ — только декомпозиция.** Новых архитектурных решений он не содержит и не
> заменяет ADR-032. Спорные места эскалируются к @software-architect и самостоятельным
> исполнителем не решаются.

## Ход выполнения

| NN | Задача | Статус |
| -- | ------ | ------ |
| 01 | Миграция схемы: `merchants.default_commodity_category_id` + `commodities.category_source` | committed |
| 02 | Домен и entity: дефолтная категория, безопасное чтение, batch-чтение дефолтов | committed |
| 03 | Конвейер: `CategorizationSource.MerchantDefault` + шаг `merchant-default` в `ICommodityCategoryResolver` | committed |
| 04 | Репозиторий: `ApplyMerchantDefaultForReceiptAsync` / `ApplyMerchantDefaultForMerchantAsync` | committed |
| 05 | Автоприменение дефолта при импорте чека (`ReceiptSynchronizationService`) | committed |
| 06 | Дожим: контракт и сервис `IMerchantDefaultCategoryService.ApplyExistingAsync` | committed |
| 07 | API: два эндпоинта /default-commodity-category + аддитивный `MerchantDto` | committed |
| 08 | Frontend: API-клиент merchants + подпись источника `merchant-default` | approved |
| 09 | Frontend: колонка и редактирование дефолта в `MerchantTable.tsx` | pending |
| 10 | Frontend: селект дефолта + кнопка «Категоризировать существующие» в `ReceiptDetails.tsx` | pending |
| 11 | Финальная проверка и ручная приёмка | pending |

## Обозначения

| Поле | Значение |
|---|---|
| Приоритет | **P0** — без этого нет сквозного сценария; **P1** — требуется критериями приёмки; **P2** — качество, не блокирует |
| Оценка | человеко-дни одного разработчика, без учёта ревью и ручной приёмки |
| Зависит от | подзадачи, которые должны быть **влиты** до старта |
| Проверка | конкретные команды и/или ручные сценарии |

Общие команды:

```bash
cd Analytics && dotnet build
cd Analytics && dotnet test              # все тесты, включая Architecture/ProjectDependencyTests
cd Analytics/frontend && npm run lint && npm run build
```

Общий критерий готовности любой бэкенд-подзадачи: `cd Analytics && dotnet test` зелёный **без правок
существующих тестов** (кроме случаев, явно оговорённых в подзадаче), новые тесты покрывают поведение,
описанное в ADR-032. Примечание: миграция схемы (T-01) применяется на dev-стенде вручную
(`dotnet run` в `ReceiptCollector.Analytics.Migrations`); схема БД в unit-тестах мокается
EF InMemory там, где это нужно.

## Сводка и порядок исполнения

| № | Подзадача | Слой | Приор. | Оценка | Зависит от | Параллельно с |
|---|---|---|---|---|---|---|
| T-01 | Миграция: две новые колонки + CHECK | Migrations | P0 | 0.5 | — | T-02 |
| T-02 | Домен/entity/mapping: `Merchant.DefaultCommodityCategory`, безопасное чтение, batch-чтение дефолтов, `category_source` в `CommodityEntity` | Domain/Infra | P0 | 1.0 | T-01 (схема) | T-03 после T-02 |
| T-03 | Конвейер: source `merchant-default` + шаг 1.5 в resolver | App/Infra | P0 | 1.0 | T-02 | T-04, T-06 |
| T-04 | Репозиторий: две операции применения дефолта | Infra | P0 | 1.0 | T-02 | T-03, T-06 |
| T-05 | Автоприменение при импорте чека | Infra | P0 | 0.5 | T-04 | T-06 |
| T-06 | Дожим: контракт + `MerchantDefaultCategoryService` | App/Infra | P0 | 0.75 | T-04 | T-03, T-05 |
| T-07 | API: `PUT` + `POST /apply` + `MerchantDto` расширение | Api | P0 | 1.0 | T-05, T-06 | T-08 (после контракта) |
| T-08 | FE: API-клиент + подпись `merchant-default` | FE | P1 | 0.25 | T-07 (контракт) | T-09, T-10 |
| T-09 | FE: колонка дефолта в `MerchantTable` | FE | P0 | 0.75 | T-08 | T-10 |
| T-10 | FE: селект + кнопка дожима в `ReceiptDetails` | FE | P0 | 0.75 | T-08 | T-09 |
| T-11 | Финальная проверка и ручная приёмка | все | P0 | 0.5–1.0 | T-01…T-10 | — |

**Критический путь:** T-01 → T-02 → T-03/T-04 → T-05 → T-06 → T-07 → T-08 → (T-09 | T-10) → T-11.

Суммарно последовательно ≈ 8 чел.-дн.; при распараллеливании (backend / frontend) ≈ 5–6 рабочих дней
до ручной приёмки.

Вне объёма (не выполнять ни в одной подзадаче): подсказка дефолта в AI-промт, глобальный дожим
«по всем магазинам», ретроактивное пересчёты сохранённых товаров при смене дефолта (задача, раздел
«Вне объёма»; ADR-032 «Вне области»).

---

## T-01. Миграция схемы: колонки дефолта и источника категории

**Приоритет:** P0 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** —

### Цель
Добавить в PostgreSQL колонки по решению A1/B1 (ADR-032, п. 1): хранение дефолта магазина и
persisted-источника категории товара.

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261105130000_add_merchant_default_commodity_category.sql` — новый.

### Чек-лист
- [x] `ALTER TABLE merchants ADD COLUMN default_commodity_category_id integer NULL` + констрейнт
      `ck_merchants_default_commodity_category CHECK (default_commodity_category_id IS NULL OR
      default_commodity_category_id <> 0)` (Undefined запрещён как дефолт — ADR-032 п. 1).
- [x] `ALTER TABLE commodities ADD COLUMN category_source varchar(32) NULL` (строка-имя, не smallint).
- [x] Backfill исторических строк **не выполняется** (`NULL` = manual/legacy — B1).
- [x] Индексы **не добавляются** (дожим опирается на `ix_receipts_merchant_id` +
      `ix_commodities_receipt_id`; отложенное решение — прецедент ADR-027 п. 3).
- [x] Скрипт соответствует конвенциям проекта (как остальные скрипты в `Scripts/`): единая
      транзакция, учёт в `migration_scripts_history` — естественно обеспечивается `MigrationRunner`.
- [x] Старый код работает с новыми колонками (rolling deploy: NULLable, аддитивно — риск-раздел ADR-032).

### Критерий готовности
Скрипт применён `MigrationRunner` на dev-стенде без ошибок; повторный прогон миграций идемпотентен;
`dotnet test` зелёный (домен/entity из T-02 в этой подзадаче не трогаются).

### Проверка
```bash
cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run   # на dev-стенде
# psql: \d merchants (default_commodity_category_id + CHECK), \d commodities (category_source)
cd Analytics && dotnet test
```

---

## T-02. Домен и entity: дефолтная категория, безопасное чтение, batch-чтение дефолтов

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-01 (схема; EF InMemory-тесты можно
начинать в параллели — скрипт применяется на dev к ревью)

### Цель
Дать домену/репозиторию возможность хранить и безопасно читать дефолтную товарную категорию
магазина (A1) и делать batch-чтение дефолтов для resolver'а (D1); добавить `category_source`
в `CommodityEntity` (B1).

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/Merchant.cs` — свойство
  `CommodityCategory? DefaultCommodityCategory { get; }` + метод `UpdateDefaultCommodityCategory(CommodityCategory?)`.
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/IMerchantRepository.cs` —
  `Task<IReadOnlyDictionary<Guid, CommodityCategory?>> GetDefaultCommodityCategoriesAsync(IReadOnlyCollection<Guid> merchantIds, CancellationToken ct)`.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantEntity.cs`,
  `Configurations/MerchantConfiguration.cs` — колонка `default_commodity_category_id`, маппинг.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityEntity.cs`,
  `Configurations/CommodityConfiguration.cs` — колонка `category_source string?` (+ маппинг в `Create`/`MapToDomain`).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantRepository.cs` — реализация batch-метода.
- Тесты (новые): `MerchantEntityMappingTests` / `MerchantRepositoryDefaultsTests` — по образцу существующих persistence-тестов проекта.

### Чек-лист
- [x] Домен: `UpdateDefaultCommodityCategory(null)` = сброс; **инварианта на enum в домене нет**
      (валидация `Enum.IsDefined`/`!= Undefined` — в эндпоинте, T-07; прецедент `PrecategorizedCommodity`, ADR-027 D11).
- [x] Entity-маппинг domain→entity: `DefaultCommodityCategory?.Id` (NULL, когда не задана).
- [x] **Безопасное чтение** (edge case «категория удалена из справочника»): в `MapToDomain` код
      читается как `CommodityCategory` только при `Enum.IsDefined(typeof(CommodityCategory), id) && id != 0`,
      иначе `null` (`DefaultCommodityCategory = null` в домен).
- [x] `GetDefaultCommodityCategoriesAsync`: только merchants с **валидным ненулевым кодом**; merchants
      без дефолта отсутствуют в словаре; `AsNoTracking()`.
- [x] `CommodityEntity`: `CategorySource` записывается только новыми операциями (T-04); маппинг
      в существующий доменный `Commodity` **не добавлять** (ADR-032 п. 2: домен `Commodity` без изменений).
- [x] Существующие методы (`UpdateCategoryAsync` магазина, `Create`/`Update` commodities) по поведению не изменены.
- [x] Тесты: безопасное чтение (валидный код / невалидный код / 0 / NULL → домен), round-trip записи,
      batch-метод возвращает только валидные дефолты.

### Критерий готовности
Домен/entity/mapping соответствуют ADR-032 п. 2 (таблица «Домен и слои»); невалидный код в БД
никогда не приводит к содержательному дефолту в домене.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~Merchant"
cd Analytics && dotnet test
```

---

## T-03. Конвейер: source `merchant-default` и шаг в `ICommodityCategoryResolver`

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-02 · **Параллельно:** T-04, T-06

### Цель
Вставить шаг `merchant-default` между `existing` и `cache` (D1, FR-M4): позиции чеков магазинов
с настроенным дефолтом получают его предложение раньше кэша/precategorized/AI; остальные идут
по прежнему потоку (FR-M8).

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CategorizationModels.cs` —
  enum-значение `CategorizationSource.MerchantDefault`, wire-имя `"merchant-default"` в
  `CategorizationSourceNames` (+ `ToWireName`).
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Merchants/Contracts/IMerchantDefaultCategoryProvider.cs` — новый узкий контракт (ADR-032 п. 5: узкий контракт предпочтительнее для тестируемости).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` — шаг 1.5.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` — регистрация провайдера.
- `Analytics/tests/.../CommodityCategoryResolverTests.cs` — новый тестовый класс (по образцу существующих тестов resolver'а).

### Чек-лист
- [x] Порядок шагов: `existing` → **`merchant-default`** → `cache` → `precategorized` → `ai`;
      дефолт стоит **ниже** `existing` (D3 отклонено — FR-M5).
- [x] Перед шагом 2 собираются `receiptIds` позиций, оставшихся без категории после шага 1; ровно
      **один batch-запрос** дефолтов по этой порции (N+1 запрещён — стиль FR-9.3 ADR-027);
      одна порция со страницы «Товары» может содержать несколько магазинов — обрабатываются все.
- [x] Провайдер: запрос `receipts JOIN merchants` (`AsNoTracking`) `WHERE receipts.id IN (...)` →
      словарь `receiptId → CommodityCategory?`; безопасное чтение дефолта уже сделано в `MapToDomain` (T-02).
- [x] Позиции с дефолтом: результат `source = "merchant-default"` (wire), ИИ/кэш/очередь для них не вызываются.
- [x] Позиции без дефолта: идут дальше по шагам 2–4 **без изменений** (FR-M8).
- [x] Ошибка чтения дефолтов → Warning + «пустой» словарь (все идут дальше) — стиль
      `GetQueueCategoriesAsync`; `OperationCanceledException` пробрасывается.
- [x] `ToWireName(MerchantDefault) == "merchant-default"`; существующие wire-имена не изменены.
- [x] Suggest остаётся read-only: resolver ничего не пишет (ни в БД, ни в кэш — ADR-032 п. 5, решение F).
- [x] Existing-тесты конвейера (категоризация чека, страница «Товары») зелёные **без правок** —
      у магазинов без дефолта поведение прежнее.

### Тесты (обязательные, dotnet test)
- [x] Порядок приоритетов: товар с категорией (`existing`) → дефолт не показывается; товар без
      категории от магазина с дефолтом → `merchant-default`, кэш/precategorized/AI не вызваны.
- [x] Магазин без дефолта → шаг промахивается, кэш вызывается (FR-M8).
- [x] Порция из двух магазинов (с дефолтом и без) — смешанный результат.
- [x] Ошибка batch-запроса → Warning, деградация к кэшу, ответ не 500.
- [x] Провайдер замокан (NSubstitute); для EF-части — отдельный тест провайдера (InMemory).

### Критерий готовности
Шаг D1 реализован, порядок FR-M4 выполняется, деградация отказоустойчива, suggest read-only.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryResolver"
cd Analytics && dotnet test
```

---

## T-04. Репозиторий: две операции применения дефолта

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-02 · **Параллельно:** T-03, T-06

### Цель
Атомарные операции присвоения дефолта позициям без категории (пп. 3–4 ADR-032, FR-M2/M3/M5/M6):
точка записи `category_source = 'merchant-default'` — **единственная** в этой задаче.

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityRepository.cs` — два метода.
- Тесты (новые): `CommodityRepositoryMerchantDefaultTests` (EF InMemory/NSubstitute по образцу persistence-тестов).

### Чек-лист
- [x] `ApplyMerchantDefaultForReceiptAsync(Guid receiptId, CommodityCategory category)` → `int`:
      SELECT имён позиций чека с `CategoryId == null || CategoryId == 0` →
      `ExecuteUpdateAsync` по `commodities WHERE receipt_id = :receiptId AND (category_id IS NULL OR category_id = 0)`:
      `CategoryId = (int)category`, `CategoryName = CommodityCategoryHelper.GetDisplayName(category)`,
      `CategorySource = "merchant-default"` — одна транзакция БД.
- [x] `ApplyMerchantDefaultForMerchantAsync(Guid merchantId, CommodityCategory category)` → `int`:
      тот же предикат по карточке `commodities JOIN receipts` с условием
      `receipts.merchant_id = :merchantId`; позиции **всех пользователей** —
      `IgnoreQueryFilters()` **явно с grep-able комментарием** «доступ вне owner-scoping намеренный:
      дожим по магазину обрабатывает позиции всех пользователей (ADR-032, E1)» (маркер в стиле ADR-027 D8/NFR-5).
- [x] Кэш пополняется (`TryAdd` по нормализованным именам обновлённых строк, F1) **после фиксации
      записи** — по прецеденту `PrecategorizedCommodityRepository.ConfirmProposedAsync` (TryAdd после
      commit, first-wins/no-op при «заморозке» ADR-019); при `updatedCount = 0` — ни одного `TryAdd`.
- [x] Позиции с содержательной категорией (`≠ NULL, ≠ 0`) не трогаются (FR-M5) — это и тест.
- [x] Кэш **не** пополняется в resolver'е/T-03 (запись — только через эти операции).
- [x] Второй вызов (idемпотентность дожима): второй UPDATE даёт `0` и не меняет `CategorySource/CategoryName`.
- [x] Возвращает число обновлённых строк.
- [x] Существующие методы репозитория (`UpdateCategoryAsync`, `UpdateCategoriesAsync` batch и др.)
      **`CategorySource` не пишут** (остаются `NULL`) — B1.

### Тесты (обязательные, dotnet test)
- [x] Чек с позициями «без категории / с ручной / с AI-категорией»: обновлена только без категории; source = `merchant-default`.
- [x] `merchantId`-вариант обновляет позиции разных владельцев (E1; fixture с двумя пользователями).
- [x] Идемпотентность: повторный вызов — 0 строк.
- [x] Кэш получил `TryAdd` по нормализованным именам (замоканный `ICommodityCategoryCache`), только после успешной фиксации.

> **Сноска (реализация T-04, согласовано с ревьюером):** тесты выполняются на **Testcontainers
> Postgres**, а не на EF InMemory, как предполагалось формулировкой: обе операции используют
> `ExecuteUpdateAsync` — релационный API, который EF InMemory не поддерживает. Прецедент —
> `PrecategorizedCommodityRepositoryTests` (тот же эталонный `ConfirmAsync` с транзакцией).

### Критерий готовности
Обе операции соответствуют пп. 3–4 ADR-032; единственная точка записи `'merchant-default'`.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityRepository"
cd Analytics && dotnet test
```

---

## T-05. Автоприменение дефолта при импорте чека

**Приоритет:** P0 · **Оценка:** 0.5 чел.-дн. · **Зависит от:** T-04 · **Параллельно:** T-06

### Цель
Новые чеки магазина с дефолтом категоризируются автоматически «при поступлении» (C1, FR-M2)
без фоновых сервисов.

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/ReceiptSynchronizationService.cs` — блок после `AddAsync`.
- Тесты: расширение существующего класса тестов синхронизации (`ReceiptSynchronizationServiceTests`).

### Чек-лист
- [x] После успешного `_receiptRepository.AddAsync(receipt)`: если `merchant.DefaultCommodityCategory == null` →
      ничего не происходит (нулевой overhead, FR-M8); существующий поток импорта никак не изменён.
- [x] Иначе — `ApplyMerchantDefaultForReceiptAsync(receipt.Id, category)`; число обновлённых строк логируется.
- [x] Исключение операции → `catch` + `LogError` и **продолжение импорта** (правило «сбой по чеку не валит цикл»
      сохраняется; partial failure — дожим кнопкой, edge case задачи).
- [x] `merchant` — уже резолвленный `ResolveMerchantAsync` (нового чтения merchant **нет**).
- [x] Чек без привязки к merchant: не меняется существующее правило импорта (пропуск чека) — не задача этой подзадачи.
- [x] Тесты: чек от магазина с дефолтом → операция применения вызвана с правильной парой (receiptId, категория);
      без дефолта → не вызвана; сбой операции → импорт завершён, Error залогирован, последующие чеки обработаны.

### Критерий готовности
FR-M2/M7: автоприменение ровно в точке импорта; изменение/снятие дефолта назад не влияет (правило привязано к импорту).

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~ReceiptSynchronizationService"
cd Analytics && dotnet test
```

---

## T-06. Дожим: контракт и сервис `IMerchantDefaultCategoryService`

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** T-04 · **Параллельно:** T-03, T-05

### Цель
Сервис дожима исторических позиций магазина (п. 4 ADR-032, FR-M3/M7): контракт в Application,
реализация в Infrastructure (прецедент `IPrecategorizedCommodityService`).

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Merchants/Contracts/IMerchantDefaultCategoryService.cs` — новый.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/MerchantDefaultCategoryService.cs` — новый.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` — DI (Scoped).
- Тесты (новые): `MerchantDefaultCategoryServiceTests`.

### Чек-лист
- [x] Контракт: `Task<MerchantDefaultApplyResult> ApplyExistingAsync(Guid adminUserId, Guid merchantId, CancellationToken ct)`
      (outcome по ADR п. 4: `NotFound` / `DefaultNotConfigured` / обновлено N — форма результата на усмотрение
      реализации, эндпоинтное маппирование в T-07).
- [x] Шаг 1: merchant через репозиторий (без `IgnoreQueryFilters` — merchants не фильтруются); `null` → `NotFound`.
- [x] Шаг 2: `DefaultCommodityCategory == null` (включая безопасно прочитанный из невалидного кода NULL) → `DefaultNotConfigured`.
- [x] Шаг 3: в одной транзакции — distinct-имена позиций магазина без категории →
      `ApplyMerchantDefaultForMerchantAsync(merchantId, category)` (кэш и `CategorySource` внутри, T-04).
- [x] Ответ — `updatedCount`; **0 считается успехом** (идемпотентность, edge case «повторное нажатие»).
- [x] Ни один путь записи результата дожима не перезаписывает существующие категории (FR-M5/M7) — гарантировано предикатом T-04.
- [x] Сервис не пишет в `precategorized_commodities` (пересечение с фоновой сессией безопасно — п. 4 ADR-032).
- [x] DI-регистрация Scoped; `Architecture/ProjectDependencyTests` зелёные (Application ↔ Infrastructure).

### Тесты (обязательные, dotnet test)
- [x] Магазин не найден → `NotFound`; дефолт не задан → `DefaultNotConfigured`; дефолт задан → вызов
      репозитория с верной категорией, счётчик пробрасывается наружу; 0 — успех.
- [x] Репозиторий замокан (NSubstitute); ошибки репозитория пробрасываются.

### Критерий готовности
Дожим по магазину работает идемпотентно и только по позициям без категории, всех пользователей.

### Проверка
```bash
cd Analytics && dotnet build
cd Analytics && dotnet test --filter "FullyQualifiedName~MerchantDefaultCategoryService"
cd Analytics && dotnet test
```

---

## T-07. API: эндпоинты дефолта + аддитивный `MerchantDto`

**Приоритет:** P0 · **Оценка:** 1.0 чел.-дн. · **Зависит от:** T-05, T-06

### Цель
HTTP-контракт по п. 6 ADR-032 + аддитивное расширение DTO (quoted: хранится отдельно от категории
магазина — FR-M1).

### Затрагиваемые файлы
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Merchants/Models/MerchantDto.cs` —
  `DefaultCommodityCategoryId int?`, `DefaultCommodityCategoryName string?` (`CommodityCategoryHelper.GetDisplayName`, `null` = не задана).
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs` — два маршрута + заполнение DTO в списке, XML-doc с ADR-032.
- `Analytics/tests/.../MerchantDefaultCategoryEndpointsTests.cs` — новый (по образцу существующих endpoint-тестов:
  `UserContext.SetUserId`, исполнение `IResult` на `DefaultHttpContext`).

### Чек-лист `PUT /api/merchants/{merchantId:guid}/default-commodity-category`
- [x] Тело `{ "categoryId": int | null }`; `null` = сброс (вызов `UpdateDefaultCommodityCategory(null)`,
      сохранение admin через существующий repo-путь редактирования merchant — ADR 011).
- [x] Порядок проверок: `401` (нет пользователся) → `403` не-админ (явный `Results.StatusCode(403)` — по уточнению
      ADR-027, дефект `Results.Forbid()` не воспроизводить в новых хендлах) → `404` merchant не найден → валидация → сохранение.
- [x] Валидация категории: `categoryId != null` и (`!Enum.IsDefined` **или** `== 0`(Undefined)) → `400` текстом.
- [x] `200 { categoryId, categoryName }`; сброс → `{ categoryId: null, categoryName: null }` (FR-M1).
- [x] Существующие маршруты `MerchantEndpoints` не изменены (DTO — только аддитивные поля).

### Чек-лист `POST /api/merchants/{merchantId:guid}/default-commodity-category/apply`
- [x] `200 { updated: int }` (в т.ч. `updated: 0`).
- [x] `DefaultNotConfigured` → `400` текстом («Default commodity category is not set for this merchant.»);
      `NotFound` → `404`; порядок прав как у PUT.
- [x] Оба хендлера — `public static async Task<IResult>` (тестируемость уровня хендлера); регистрация
      в группе merchants (`Program.cs` вызов уже есть, не дублировать).

### Список merchants
- [x] `GET /api/merchants` (список, используется `/merchants`) отдаёт новые поля `DefaultCommodityCategoryId/Name`
      (аддитивно — wire-контракт совместим).

### Тесты (обязательные, dotnet test)
- [x] PUT: 401 / 403 (исполнением IResult — это 403, не 500) / 404 / 400 (неизвестный код, 0) / 200 set / 200 reset.
- [x] Apply: 401 / 403 / 404 / 400 DefaultNotConfigured / 200 updated 0 и updated N.
- [x] `MerchantDto`-сериализация новых полей; `ProjectDependencyTests` зелёные.

### Критерий готовности
Коды/тела соответствуют п. 6 ADR-032 дословно; не-админ получает 403 и при прямом вызове.

### Проверка
```bash
cd Analytics && dotnet test --filter "FullyQualifiedName~MerchantDefaultCategoryEndpoints"
cd Analytics && dotnet test
# smoke на dev (после миграции T-01):
# curl -i -X PUT http://localhost/api/merchants/<id>/default-commodity-category -H 'Content-Type: application/json' -d '{"categoryId":5}'
```

---

## T-08. Frontend: API-клиент merchants + подпись источника

**Приоритет:** P1 · **Оценка:** 0.25 чел.-дн. · **Зависит от:** T-07 (контракт) · **Параллельно:** T-09, T-10

### Цель
Типизированный доступ к новым эндпоинтам и подпись нового источника (п. 7 ADR-032).

### Затрагиваемые файлы
- `Analytics/frontend/src/types/merchant.ts` (или место, где живёт `MerchantDto` в TS) — аддитивные поля.
- `Analytics/frontend/src/api/merchants.ts` — `updateMerchantDefaultCategory(merchantId, categoryId | null)`,
  `applyMerchantDefaultCategory(merchantId): Promise<{ updated: number }>`.
- `Analytics/frontend/src/utils/categorizationSource.ts` — подпись `'merchant-default'`.

### Чек-лист
- [x] Оба метода: `credentials: 'include'`, ожидание ошибок по `!response.ok` в стиле существующего API-клиента:
      401/403/400/404 различаемы сообщением пользователю на русском (формируется на клиенте).
- [x] `updateMerchantDefaultCategory`: тело `{ categoryId: number | null }`.
- [x] Подпись источника аддитивная: `'merchant-default'` → «по умолчанию магазина»; существующие
      source-имена не изменены; fallback для неизвестного source прежний.
- [x] Старые методы `fetchMerchants`/`updateMerchant` не изменены по поведению.

### Критерий готовности
Клиент и подписи готовы для T-09/T-10; сборка/линт чистые.

### Проверка
```bash
cd Analytics/frontend && npm run build && npm run lint
```

---

## T-09. Frontend: колонка и редактирование дефолта в `MerchantTable`

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** T-08 · **Параллельно:** T-10

### Цель
В `/merchants` админ задает/изменяет/снимает дефолтную товарную категорию (п. 7 ADR-032, FR-M1).

### Затрагиваемые файлы
- `Analytics/frontend/src/components/MerchantTable.tsx`.
- `Analytics/frontend/src/App.css` — только если не хватает существующих классов.

### Чек-лист
- [ ] Новая колонка «Категория товаров (по умолчанию)» — только при `isAdmin`; отображается
      `defaultCommodityCategoryName` из DTO или «Не задана».
- [ ] Кнопка «ред.»: `<select>` из `fetchCategories()` (`api/commodities.ts`, товарный справочник)
      **без** `Undefined`; первая опция «— не задана —» = сброс (`null`).
- [ ] Сохранение через `updateMerchantDefaultCategory`; тост об успехе/ошибке (стиль существующей
      строки редактирования).
- [ ] Отдельные состояния редактирования (`editingDefaultId`, `defaultSavingId`) — **не пересекаются**
      с редактированием имени/категории магазина (одновременно в строке редактируется одно поле).
- [ ] Категория **самого** магазина (`MerchantCategory`) не изменена — колонки и select'ы независимы (FR-M1).
- [ ] Не-админ: новая колонка скрыта, таблица работает как раньше.

### Критерий готовности
Админ может задать/изменить/снять дефолт прямо в `/merchants`; ручные/существующие поля не задеты.

### Проверка
```bash
cd Analytics/frontend && npm run build && npm run lint
```
Ручной сценарий (полный — в T-11): выбрать категорию → перезагрузка страницы → значение сохранено;
«— не задана —» → в DTO `null`.

---

## T-10. Frontend: селект дефолта и кнопка дожима в `ReceiptDetails`

**Приоритет:** P0 · **Оценка:** 0.75 чел.-дн. · **Зависит от:** T-08 · **Параллельно:** T-09

### Цель
В «Детали чека» (блок магазина, админ): настройка дефолта и кнопка «Категоризировать существующие»
(п. 7 ADR-032, FR-M1/M3).

### Затрагиваемые файлы
- `Analytics/frontend/src/components/ReceiptDetails.tsx`.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/.../ReceiptReadService.cs` (фактический путь найти grep) —
  **эскалация с ревью T-07:** `MerchantDto` строится 5 аргументами (строки ~38, 91, 123), поэтому в
  `GET /api/receipts/{id}` новые поля `defaultCommodityCategoryId/Name` будут `null`, а чек-лист T-10
  читает `receipt.merchant.defaultCommodityCategoryName`. Расширить заполнение (минимум `GetByIdAsync`);
  имя категории — из C#-словаря `CommodityCategoryHelper.GetDisplayName`, в SQL-проекцию не кладётся.
  Покрыть тестом: детали чека магазина с дефолтом → поля заполнены.

### Чек-лист
- [ ] В блоке магазина рядом с «ред.» имени — селект «категория товаров по умолчанию» (админ):
      текущее значение из `receipt.merchant.defaultCommodityCategoryName`; опции из `fetchCategories()`
      (без `Undefined`); «— не задана —» = сброс; сохранение через `updateMerchantDefaultCategory`.
- [ ] Кнопка «Категоризировать существующие» рядом с дефолтом — показывается **только** при админе
      **и** заданном дефолте; вызов `applyMerchantDefaultCategory(receipt.merchant.id)`.
- [ ] Успех: toast `Обновлено товаров: {updated}`; `updated == 0` → информационный тост
      «Все позиции уже категоризированы» (идемпотентность, edge case задачи).
- [ ] Ошибки (400/404/403/сеть) — `showDialog`/`toast.error` по прецеденту `saveSelectedCategories`.
- [ ] Существующие функции страницы («Категоризировать» чека, редактирование имени, сохранение
      категорий) не изменены; существующий `getSuggestionNote` работает с новым source после T-08.

### Критерий готовности
Сценарий настройки дефолта и дожима доступен из «Детали чека»; исторические страницы чеков ведут себя как раньше.

### Проверка
```bash
cd Analytics/frontend && npm run build && npm run lint
```
Ручной сценарий (полный — в T-11).

---

## T-11. Финальная проверка и ручная приёмка

**Приоритет:** P0 · **Оценка:** 0.5–1.0 чел.-дн. · **Зависит от:** T-01…T-10

### Цель
Сквозная проверка требований задачи и приёмка по критериям из раздела «Критерии приёмки» задачи.

### Чек-лист
- [ ] `cd Analytics && dotnet test` — все зелёные; `cd Analytics/frontend && npm run lint && npm run build` — чисто.
- [ ] Миграция применена на dev (`dotnet run` в Migrations), `migration_scripts_history` содержит скрипт.
- [ ] Ручной сквозной сценарий (соответствует «Сценарий использования» задачи):
  1. `/merchants` → задать дефолт (например, столовая → «Готовая еда») → значение сохранилось; категория магазина не изменилась.
  2. Тот же магазин в «Детали чека» → селект показывает дефолт, изменение работает.
  3. Новый чек от магазина (или импорт из MongoDB синхронизацией) → позиции без категории получили
     дефолт; в suggest подпись источника «по умолчанию магазина».
  4. Кнопка «Категоризировать существующие» → тост с числом; позиции **без** категории выбранного
     магазина заполнены, категории других магазинов и ручные не тронуты; повторное нажатие →
     «Все позиции уже категоризированы».
  5. Сменить/снять дефолт → уже сохранённые товары не изменились (в БД `category_source` только
     `'merchant-default'`/`NULL`).
- [ ] Edge cases со стенда: магазин без дефолта → обычный поток; ручная категория не перезаписывается;
     чек без merchant-привязки не ломает поток.
- [ ] Сводка приёмки — в `docs/reviews/merchant-default-commodity-category/` (протокол task-review-gate).

### Проверка
```bash
cd Analytics && dotnet test
cd Analytics/frontend && npm run lint && npm run build
```

---

## Открытые вопросы (эскалация к @software-architect) — РЕШЕНЫ

1. **Точка `TryAdd` в кэш (T-04): ✅ решено.** ADR-032 (amend): `TryAdd` выполняется **внутри**
   репозиторных операций `ApplyMerchantDefaultForReceiptAsync` / `ApplyMerchantDefaultForMerchantAsync`
   сразу после commit транзакции (прецедент `PrecategorizedCommodityRepository.ConfirmProposedAsync`,
   ADR-027 п. 4). Resolver (T-03) в кэш не пишет.
2. **Форма результата `ApplyExistingAsync` (T-06): ✅ решено.** ADR-032 (amend, п. 4): outcome-объект
   `MerchantDefaultApplyResult(MerchantDefaultApplyOutcome Outcome, int UpdatedCount)` с исходами
   `NotFound` / `DefaultNotConfigured` / `Applied`; исключения — только для сбоев инфраструктуры.
   Маппинг в T-07: `NotFound → 404`, `DefaultNotConfigured → 400` текстом, `Applied → 200 { updated }`
   (в т.ч. `updated = 0`).

## Ссылки

- [Задача: merchant-default-commodity-category](../tasks/merchant-default-commodity-category.md)
- [ADR-032](../adr/032-merchant-default-commodity-category.md)
- [ADR 009](../adr/009-auto-commodity-categorization.md), [ADR 019](../adr/019-in-memory-commodity-category-cache.md),
  [ADR 027](../adr/027-background-commodity-precategorization.md), [ADR 028](../adr/028-commodities-page-categorization.md),
  [ADR 011](../adr/011-merchant-name-editing.md)
