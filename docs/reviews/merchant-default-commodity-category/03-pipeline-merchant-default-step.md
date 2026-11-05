# Задача 03 - Конвейер: source `merchant-default` и шаг в `ICommodityCategoryResolver`

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-03-конвейер-source-merchant-default-и-шаг-в-icommoditycategoryresolver
- **Дата**: 2026-11-05
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Общий конвейер категоризации получил новый шаг `merchant-default` между `existing` и `cache`
(ADR-032, D1; FR-M4): позиции чеков магазинов с настроенной товарной категорией по умолчанию
получают её как предложение раньше кэша, очереди предкатегорий и ИИ; позиции магазинов без
дефолта идут по прежнему потоку без изменений (FR-M8). Дефолт стоит **ниже** `existing` —
сохранённая (в т.ч. ручная) категория приоритетнее (D3 отклонено). Перед шагом собираются
distinct `receiptId` позиций без категории и выполняется ровно **один batch-запрос**
`receipts JOIN merchants` (N+1 запрещён); смешанная порция страницы «Товары» из нескольких
магазинов обрабатывается полностью. Ошибка чтения дефолтов — Warning + деградация к
кэшу/очереди/ИИ (стиль `GetQueueCategoriesAsync`), `OperationCanceledException` пробрасывается.
Suggest остался read-only: resolver не пишет ни в БД, ни в кэш (решение F). Wire-контракт
расширен аддитивно: новый источник `"merchant-default"`, существующие имена не менялись.

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CategorizationModels.cs` —
  enum-значение `CategorizationSource.MerchantDefault` (порядок значений = порядок шагов) +
  const `MerchantDefault = "merchant-default"` + кейс `ToWireName`; существующие wire-имена не
  изменены (числовые коды enum нигде не persisted — wire только строковый).
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Merchants/Contracts/IMerchantDefaultCategoryProvider.cs` —
  новый узкий batch-контракт (ADR-032 п. 2 — предпочтён `IMerchantRepository` для тестируемости):
  `GetDefaultsByReceiptIdsAsync(receiptIds) → receiptId → CommodityCategory?`; семантика: валидный
  дефолт → категория, NULL/0/устаревший код → `null` («дефолта нет»), неизвестный чек → без ключа.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantDefaultCategoryProvider.cs` —
  новый: `Receipts.AsNoTracking().Where(r => idList.Contains(r.Id))` c проекцией по навигации
  `r.Merchant.DefaultCommodityCategoryId` (JOIN receipts→merchants), безопасное чтение кода
  (`!= 0 && Enum.IsDefined`, тот же предикат, что `MerchantEntity.MapToDomain` T-02). Ошибки не
  глотает — обработку несёт resolver. Owner query-filter `Receipts` намеренно **не** обходится:
  позиции suggest всегда текущего пользователя (в отличие от дожима E1 — это T-04/T-06).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` —
  шаг 2 между existing и кэшем + хелпер `GetMerchantDefaultCategoriesAsync` (стиль
  `GetQueueCategoriesAsync`: OCE → rethrow, Exception → Warning + пустой словарь). Конструктор:
  новый **последний** параметр `IMerchantDefaultCategoryProvider? = null` (см. допущение ниже).
  Ничего не пишет (read-only). Перенумерованы комментарии шагов.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityBatchCategorizationService.cs` —
  только XML-doc: цепочка конвейера приведена к пятишаговой (замечание ревьюера — дрейф документации).
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategoryResolver.cs` —
  только XML-doc (цепочка с `merchant-default`), сигнатура не менялась.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs` —
  `AddScoped<IMerchantDefaultCategoryProvider, MerchantDefaultCategoryProvider>()`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryResolverTests.cs` —
  новый (13 тестов + theory wire-имён): existing выше дефолта (включая «провайдер не вызван,
  когда всё existing»); без категории + дефолт → `merchant-default`, кэш/очередь/ИИ не вызваны
  (даже при попаданиях в них); магазин без дефолта → кэш (FR-M8); `null`-значение словаря →
  промах; смешанная порция двух магазинов (кэш опрошен только для позиции без дефолта); ровно
  1 batch-запрос с distinct receiptIds; ошибка batch → Warning (CapturingLogger) + деградация без
  исключения (не 500); OCE пробрасывается; read-only (`TryAdd` не вызван); совместимость
  конструктора без провайдера; `ToWireName` всех шести источников.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/MerchantDefaultCategoryProviderTests.cs` —
  новый (6 EF InMemory-тестов): JOIN-маппинг чек→магазин, безопасное чтение 0/9999 → null,
  неизвестные чеки отсутствуют в словаре, дубликаты id входа, пустой вход, owner query-filter
  соблюдается.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Infrastructure/MerchantDefaultCategoryProviderRegistrationTests.cs` —
  новый (2 теста, по замечанию ревьюера): дескриптор `IMerchantDefaultCategoryProvider`
  зарегистрирован Scoped на конкретную реализацию; контейнер с полным `AddInfrastructure`
  резолвит `ICommodityCategoryResolver` и провайдер в scope.
- `docs/plans/merchant-default-commodity-category.md` — статус и чек-лист T-03.

## Как проверено

- `cd Analytics && dotnet build` — 0 ошибок (NU1902/NU1903 — предсуществующие warning'и по пакетам).
- `cd Analytics && dotnet test --filter "FullyQualifiedName~CommodityCategoryResolver|FullyQualifiedName~MerchantDefaultCategoryProvider"` —
  23 passed, 0 failed.
- `cd Analytics && dotnet test --filter "FullyQualifiedName~MerchantDefaultCategoryProviderRegistration"` —
  2 passed, 0 failed.
- `cd Analytics && dotnet test` — **396 passed, 0 failed, 0 skipped** (базлайн до задачи — 371;
  +23 шаговых/EF + 2 wiring). Существующие тесты конвейера (категоризация чека, страница
  «Товары») и `Architecture/ProjectDependencyTests` зелёные **без правок**.

## Итоги ревью

- Исправлено по неблокирующим замечаниям ревьюера (раунд 1):
  - XML-doc `CommodityBatchCategorizationService` описывал четырёхшаговую цепочку — обновлён до
    пятишаговой с `merchant-default`. Дрейф по остальным XML-doc'ям проверен grep'ом:
    единственный другой адресат с прежней цепочкой — `ICommodityCategoryResolver` (обновлён при
    реализации); упоминания в `PrecategorizationBackgroundService` — про собственный поток
    фоновой сессии (existing → кэш → ИИ, ADR-027), он общего решателя не использует и шага
    дефолта не получает, менять не нужно.
  - Добавлен wiring-тест DI (`MerchantDefaultCategoryProviderRegistrationTests`) — собран
    полный контейнер `AddInfrastructure` на in-memory конфигурации с фиктивной Postgres-строкой
    (подключение не открывается): потеря регистрации провайдера, которая иначе «проглатывалась»
    из-за опционального параметра конструктора, уронит этот тест.
- Оставлено без изменений (с обоснованием):
  - Дубль предиката безопасного чтения в трёх местах (`MerchantEntity.MapToDomain`,
    `MerchantRepository.GetDefaultCommodityCategoriesAsync`, `MerchantDefaultCategoryProvider`)
    плюс оборонительная проверка в resolver'е — осознанная избыточность: каждый путь чтения
    автономен и не зависит от дисциплины вызывающего (3 строки, все четыре места покрыты
    тестами); вынос в общий хелпер связал бы несвязанные persistence-слои ради трёх строк —
    кандидат на рефакторинг при появлении четвёртого потребителя.
  - «Избыточная» проверка `_merchantDefaultProvider is null` внутри
    `GetMerchantDefaultCategoriesAsync` (внешний шаг уже проверяет) — хелпер самодостаточен и
    не зависит от контекста вызова; стоимость — одна ленивая проверка, семантика при прямом
    вызове корректна (пустой словарь → промах шага).

## Заметки для коммита

- Рекомендуемое сообщение коммита:
  `feat(analytics): шаг merchant-default в конвейере категоризации (T-03, ADR-032 D1/F)`
- **НЕ включать в коммит** `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
  `appsettings.Development.json` — в рабочем дереве посторонние локальные правки человека
  (личный AI-endpoint, TelegramId, креды Mongo). Коммитить пофайлово (только файлы T-03).
- К сведению: `IMerchantRepository.GetDefaultCommodityCategoriesAsync` (T-02) пока без
  production-потребителей — resolver подключён через узкий контракт
  `IMerchantDefaultCategoryProvider` (предпочтение ADR-032 п. 2 для тестируемости). Метод —
  артефакт плана T-02 (реализован строго по чек-листу); решение об его удалении/переиспользовании
  — за человеком/архитектором, в рамки T-03 удаление не входило.
- Риск «молча выключенного шага» (потеря DI-регистрации при опциональном параметре конструктора
  resolver'а) закрыт wiring-тестом; при ручной проверке на dev — у магазина с дефолтом suggest
  чека/страницы «Товары» отдаёт `"source": "merchant-default"` для позиций без категории
  (подпись на фронте — T-08).
- Ручная проверка функциональности — в T-11 (API/UI: T-07…T-10).
