# Задача 04 - Репозиторий: две операции применения дефолта

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-04-репозиторий-две-операции-применения-дефолта
- **Дата**: 2026-11-05
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

`CommodityRepository` получил две атомарные операции присвоения дефолтной товарной категории
магазина позициям **без категории** (ADR-032 пп. 3–4, FR-M2/M3/M5/M6):
`ApplyMerchantDefaultForReceiptAsync(receiptId, category)` — для будущего автоприменения при
импорте чека (T-05), и `ApplyMerchantDefaultForMerchantAsync(merchantId, category)` — дожим
исторических позиций магазина всех пользователей (T-06; E1 — `IgnoreQueryFilters()` с grep-able
маркером намеренного выхода за owner-scoping). Каждая операция — одна транзакция: выборка имён
позиций с `category_id IS NULL OR = 0` и `ExecuteUpdateAsync` (`CategoryId`, `CategoryName`,
`CategorySource = 'merchant-default'`) по одному предикату; содержательные категории не
перезаписываются, повторный вызов идемпотентен (0 строк, источник/имя не меняются). Это
единственная точка записи `'merchant-default'` в системе (B1); существующие методы
(`UpdateCategoryAsync`, `UpdateCategoriesAsync`) колонку по-прежнему не пишут. После commit
транзакции кэш категорий пополняется `TryAdd` по нормализованным именам обновлённых строк (F1,
first-wins/no-op — семантика ADR 019); при нуле обновлённых — ни одного вызова.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/ICommodityRepository.cs` —
  два метода контракта с подробным XML-doc (транзакционность, E1-маркер, F1-порядок,
  идемпотентность, контракт возврата, `ArgumentException` на Undefined/вне-enum код).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityRepository.cs` —
  реализация обеих операций (транзакция + SELECT имён + `ExecuteUpdateAsync` + `TryAdd` после
  commit); `AddNamesToCache` (distinct по исходным именам, `CommodityNameNormalizer`);
  `EnsureAssignableDefaultCategory` (guard A1 по прецеденту precategorized-репозитория);
  конструктор — новый optional-параметр `ICommodityCategoryCache? cache = null` (совместимость
  существующих тестовых фабрик, прецедент T-03). Существующие методы не изменены. DI-регистрации
  правки не потребовалось: `ICommodityCategoryCache` — зарегистрированный singleton, MS DI
  внедряет его и в optional-параметр.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityRepositoryMerchantDefaultTests.cs` —
  новый, 7 тестов на Testcontainers Postgres (прецедент `PrecategorizedCommodityRepositoryTests`;
  EF InMemory не поддерживает `ExecuteUpdateAsync`) + NSubstitute-кэш: выборочное обновление
  NULL/0 против ручной/источниковой категории и соседнего чека; дожим позиций двух владельцев
  при активном owner-фильтре и неприкосновенность другого магазина; идемпотентность; TryAdd с
  проверкой порядка «commit → кэш» вторым подключением (до commit MVCC увидел бы 0 строк с
  маркером); 0 обновлений → 0 TryAdd; guard Undefined/9999; существующие пути не пишут
  `CategorySource`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityRepositoryCacheWiringTests.cs` —
  новый wiring-тест DI (reflect-проверка непустого `_cache` после реального resolve; закрыл риск
  «молча отвалившегося кэша» из-за optional-параметра; прецедент — замечание ревью T-03).
- `docs/plans/merchant-default-commodity-category.md` — чек-лист T-04, статус строки 04,
  сноска о Testcontainers вместо InMemory.

## Как проверено

- `cd Analytics && dotnet build` — 0 ошибок.
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityRepository"` — **21 passed**
  (13 существующих batch-тестов + 7 новых поведенческих + 1 wiring).
- `cd Analytics && dotnet test` — **404 passed / 404** (baseline 396 + 8 новых, без правок
  существующих тестов).
- `grep -in "доступ вне owner-scoping намеренный: дожим"` — маркер E1 найден одной строкой.

## Итоги ревью

- APPROVED в 1 раунд, blockers нет.
- Исправлено по ходу самопроверки: grep-able маркер E1 собран в одну строку; формулировка
  doc-комментария `AddNamesToCache`; добавлен wiring-тест DI (риск класса «молча выключившийся
  шаг» из замечания ревью T-03).
- Оставлено без изменений (принятые suggestions):
  - **Остаточная гонка SELECT(имена) → UPDATE.** В Postgres READ COMMITTED между SELECT имён и
    UPDATE позиция может быть категоризована другим писателем; FR-M5 при этом не нарушается —
    `ExecuteUpdateAsync` перепроверяет предикат на заблокированной строке (EPQ), счётчик
    `updated` честный. Риск ограничен кэшем: имя теоретически может попасть в кэш с дефолтной
    категорией, хотя позицию реально заполнил кто-то другой — безвредно: single instance
    (ADR-027 D12), first-wins без вытеснения, при рестарте кэш перезаполняется из БД
    (источник истины), а ADR-032 п. 4 прямо допускает пересечения дожима с фоновой сессией.
  - **Distinct по исходным (не нормализованным) именам** — нормализованные дубли дают повторные
    `TryAdd` с тем же ключом; no-op обеспечивает сам кэш (ADR 019 FR-1.4), как в прецеденте
    `CommodityBatchCategorizationService`.
  - **Optional-параметр кэша в конструкторе** — обязательный параметр сломал бы 7 существующих
    вызовов `new CommodityRepository(context)` в `CommodityBatchRepositoryTests`, а правка
    существующих тестов запрещена планом; постановка T-04 явно допускает null-default по
    прецеденту T-03. Регресс регистрации закрыт `CommodityRepositoryCacheWiringTests`.

## Заметки для коммита

- Рекомендуемое сообщение: `T-04: ApplyMerchantDefaultForReceipt/Merchant в CommodityRepository + пополнение кэша после commit (ADR-032 E1/F1)`.
- **НЕ включать** `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
  `appsettings.Development.json` — пользовательские dev-настройки, к задаче отношения не имеют.
- **ОБЯЗАТЕЛЬНО включить** untracked
  `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/CommodityRepositoryCacheWiringTests.cs`
  — иначе wiring-проверка внедрения кэша (F1) потеряется.
- Часть файлов уже находится в git-индексе (не по действиям агента) — перед коммитом сверить
  `git status`, чтобы в коммит не попали лишние пути.
- Ручная проверка не требуется: поведение — чистый persistence-слой, потребители (T-05/T-06)
  подключат операции следующими задачами; схема БД не менялась (миграция — T-01).
