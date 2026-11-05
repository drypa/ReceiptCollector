# ADR-032: Категория товаров по умолчанию для магазина (merchant default commodity category)

## Статус

Принято (проектирование). Основание — задача
[merchant-default-commodity-category](../tasks/merchant-default-commodity-category.md)
(статус Draft, автор user, дата 2026-11-05; критерии приёмки и edge cases задачи обязательны
к исполнению). Реализация — по плану (декомпозиция задачи в `docs/plans/`).

**Дополняет, ничего не отменяет:** [ADR 009](009-auto-commodity-categorization.md)
(контракты suggest/apply чека), [ADR 019](019-in-memory-commodity-category-cache.md)
(in-memory кэш категорий), [ADR 027](027-background-commodity-precategorization.md)
(очередь предкатегорий и стиль админских эндпоинтов/глобального доступа),
[ADR 028](028-commodities-page-categorization.md) (общий конвейер
`ICommodityCategoryResolver`), [ADR 030](030-precategorization-work-window.md)
(окно фоновой предкатегоризации — не затрагивается).

**Вне области (зафиксировано задачей):** передача дефолтной категории магазина как
подсказки в AI-запрос; глобальная кнопка «прогнать все магазины с настроенной
категорией»; ретроактивное изменение уже сохранённых товаров при смене дефолта.

> **Дополнено (2026-11-05, решение открытых вопросов декомпозиции
> `docs/plans/merchant-default-commodity-category.md`):** фиксируются две детали реализации,
> контрактных изменений не вносящие.
> (1) **Точка `TryAdd` в кэш** — внутри репозиторных операций
> `ApplyMerchantDefaultForReceiptAsync` / `ApplyMerchantDefaultForMerchantAsync`,
> непосредственно после commit: по прецеденту `PrecategorizedCommodityRepository.ConfirmProposedAsync`
> (ADR-027 п. 4). Выбор имён и UPDATE живут в одной транзакции — вынесение `TryAdd`
> на уровень сервиса ввело бы гонку «имена прочитаны до, UPDATE после чужой записи».
> Resolver кэш не пополняет (п. 5).
> (2) **Форма результата `ApplyExistingAsync`** — outcome-объект
> `MerchantDefaultApplyResult(Outcome, UpdatedCount)` с исходами
> `NotFound` / `DefaultNotConfigured` / `Applied` по образцу `ConfirmPrecategorizedResult`
> (ADR-027): ожидаемые бизнес-состояния возвращаются значением; исключения — только
> для сбоев инфраструктуры. Подробности — п. 4.

## Контекст

### Проблема

Администратор вручную категоризирует товары по всем историческим чекам, при этом
многие магазины логично «мапятся» на одну товарную категорию (столовая — «Готовая еда»,
автосервис — «Автотовары», фитнес-клуб — «Фитнес и спорт»). Требуется: админ задаёт
магазину категорию товаров по умолчанию; новые чеки магазина категоризируются ею
автоматически при поступлении; исторические позиции дожимаются кнопкой в «Детали чека»
(только позиции этого магазина **без категории**).

### Текущее состояние кода (факты, проверены по коду 2026-11-05)

| Место | Факт | Значение для решения |
|---|---|---|
| `Domain/Modules/Merchants/Merchant.cs`, `MerchantEntity.cs`, `MerchantConfiguration.cs` | `Merchant(id, name, category: MerchantCategory, address, inn)`; таблица `merchants`: `category integer NOT NULL` (код `MerchantCategory`), `inn` unique | Категория **самого магазина** — отдельный справочник `MerchantCategory` (ADR 008), не пересекается с товарным |
| `Domain/Modules/Commodities/Commodity.cs` | `Commodity(Id, ReceiptId, Name, …, Category)` — `ReceiptId` известен на доменном уровне | Позволяет в общем конвейере определять магазин позиции без изменения домена |
| `Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` | Общий конвейер (ADR-028 п. 9): шаг 1 `existing` (категория самого товара) → шаг 2 кэш (`ICommodityCategoryCache.TryGet`) → шаг 3 очередь `precategorized` (batch по точным именам) → шаг 4 AI с дедупликацией. Source — `CategorizationSource`/wire-имена | Дефолт магазина вставляется как **новый шаг между шагами 1 и 2** |
| `Application/Modules/Commodities/Models/CategorizationModels.cs` | `CategorizationSource { Existing, Cache, Precategorized, Ai, Undefined }`, wire-имена lowercase | Аддитивное значение `MerchantDefault` / `"merchant-default"` |
| `Infrastructure/Synchronization/ReceiptSynchronizationService.cs` (метод `SynchronizeReceipts`) | Единственная точка импорта чека: `ResolveMerchantAsync` → `MongoReceiptMapper.Map` → `ReceiptRepository.AddAsync` (dedup по external id и естественному ключу); ошибки по чеку логируются и не валят цикл | Точка автоприменения дефолта «при поступлении чека» |
| `Persistence/Postgres/CommodityRepository.cs` | `UpdateCategoryAsync`, `GetByOwnerAsync`, `UpdateCategoriesAsync`; у `DbSet<CommodityEntity>` query-filter **нет**, но join-навигация на `Receipt` наследует глобальный фильтр `Receipts` | Для админских/глобальных операций — паттерн `IgnoreQueryFilters()` (ADR-027 D8) |
| `Persistence/Postgres/PrecategorizedCommodityRepository.cs` (`ConfirmProposedAsync`) | Транзакция + `ExecuteUpdateAsync` по всем пользователям (`IgnoreQueryFilters()`), условие `CategoryId == null \|\| CategoryId == 0` («без категории»), заполнение `category_id` **и** `category_name`, `cache.TryAdd` после фиксации | Эталон семантики дожима по магазину; следуем D11 («0/Undefined = категории нет») |
| `Infrastructure/Modules/Commodities/InMemoryCommodityCategoryCache.cs` (ADR 019) | Сквозной кэш (без `userId`), first-wins, «замерзание» при `MaxSize`; наполняется при старте из `commodities` (`CategoryId != null && != 0`) | Важно для решения «пополнять ли кэш категориями, проставленными дефолтом» (см. решение F) |
| `Api/Modules/Merchants/MerchantEndpoints.cs` | Эталон админских эндпоинтов: `401` → `Results.Forbid()` (известный дефект 403, вынесен в бэклог) → текстовые ошибки `Results.BadRequest/NotFound`; `X-Total-Count`; DTO `MerchantDto(Id, Name, Category, Address, Inn)` переиспользуется в `ReceiptSummaryDto`/`ReceiptDetailsDto` | Новые маршруты: `PUT .../default-commodity-category`, `POST .../default-commodity-category/apply` в этой же группе |
| Схема PostgreSQL (`20241019160000_initial_create.sql`) | `receipts.merchant_id` FK → `merchants` (RESTRICT); `commodities.receipt_id` FK → `receipts` (CASCADE); индексы `ix_receipts_merchant_id`, `ix_commodities_receipt_id`; `commodities.category_id integer NULL`, `category_name varchar(128) NULL` | Ссылка commodity → merchant только через `receipts`; новые колонки `merchants.default_commodity_category_id`, `commodities.category_source` |
| `MigrationRunner` (ADR-027 п. 3) | Транзакция на скрипт, учёт в `migration_scripts_history` | Обычные SQL-скрипты в `Migrations/Scripts` |
| Frontend | `MerchantTable.tsx` (inline-select категорий магазина, паттерн из ADR 008), `ReceiptDetails.tsx` (блок магазина с редактированием имени `isAdmin`, кнопка «Категоризировать» → `suggestReceiptCategories`, проверка `isAdmin`, `toast`, `showDialog`); источники предложений подписываются через `getSuggestionNote` (`utils/categorizationSource.ts`); `api/commodities.ts` → `fetchCategories()` (товарный справочник `GET /api/commodities/categories`); `npm test` на фронте нет (ADR 011 E1) | Ручная проверка UI + `npm run build`/`lint`; backend-тесты — NSubstitute + `UserContext.SetUserId` |

### Требования (из задачи, сводка)

- **FR-M1** — в `/merchants` можно задать/изменить/снять дефолтную **товарную** категорию
  магазина; то же доступно в «Детали чека». Параметр хранится отдельно и не пересекается
  с категорией самого магазина (`MerchantCategory`).
- **FR-M2** — новые чеки магазина с дефолтом автоматически категоризируются ею при
  поступлении (без участия пользователя).
- **FR-M3** — кнопка «категоризировать существующие» в «Детали чека» дожимает только
  позиции **конкретного магазина** без категории (NULL или 0).
- **FR-M4** — приоритет: ручная категория → дефолт магазина → кэш → precategorized → AI.
- **FR-M5** — ручные категории никогда не перезаписываются; позиция с содержательной
  категорией (≠ NULL, ≠ 0) не трогается ни при импорте, ни при дожиме, ни suggest'ом.
- **FR-M6** — категория, проставленная дефолтом, помечается источником `merchant-default`
  и отличима от `ai`, `precategorized`, manual — **в том числе после перезапуска**.
- **FR-M7** — изменение/снятие дефолта не влияет на уже сохранённые товары.
- **FR-M8** — магазин без дефолта работает по текущему потоку без изменений.

## Рассмотренные варианты

### Ключевое решение A: Хранение дефолтной товарной категории магазина

#### Вариант A1: Колонка `merchants.default_commodity_category_id integer NULL`, валидация по enum — **выбран**

**Описание:** nullable-колонка с кодом `CommodityCategory`; `CHECK (col IS NULL OR col <> 0)`
(`Undefined` запрещён как значение дефолта; сброс = `NULL`). Валидация при записи —
`Enum.IsDefined` + `!= Undefined` в эндпоинте; при чтении — «безопасное» отображение в домен:
код не входит в enum (или 0) → домен получает `null` (трактуется как «не задан»).

**Плюсы:**
- Точный прецедент: `precategorized_commodities.proposed_category_id integer NOT NULL` —
  тот же способ хранения товарной категории без FK (ADR-027 п. 3, с проверкой
  `Enum.IsDefined` при чтении — FR-9.2).
- Справочник категорий — enum в коде (ADR 005, решение B1): таблицы-справочника нет,
  FK на несуществующую таблицу невозможен. Целостность кода-справочника
  обеспечивается валидацией на записи и безопасным чтением.
- Смена/удаление значения enum между релизами (ADR 010, миграция
  `fix_commodity_category_food_name`) не ломает БД: невалидный код читается как
  «не задан», админ перезаписывает (edge case задачи: «поведение при удалении категории
  определяет архитектор — например, дефолт сбрасывается»). Это сброс «по факту чтения»,
  без миграции данных — принятая семантика.

**Минусы:**
- Код-справочник не контролируется БД-констрейнтом (кроме запрета 0) — компенсируется
  валидацией приложения (тот же компромисс принят в ADR-027).

#### Вариант A2: Перевод `CommodityCategory` в таблицу-справочник + FK

**Минусы:** ломает явное решение ADR 005 (справочник задан enum'ом), тянет перекодировку
данных, EF-конфигурации и весь UI — непропорциональный scope creep. Отклонено.

#### Вариант A3: Автоматический маппинг `MerchantCategory` → `CommodityCategory`

**Описание:** не хранить дефолт вообще, вычислять товарную категорию из категории магазина.

**Минусы:** справочники расходятся (21 vs 19 значений, разные коды и русские названия —
ADR 008 «Риски»); соответствия «столовая → Готовая еда», «фитнес-клуб → Фитнес и спорт»
такой маппинг гарантированно не даёт; задача прямо требует **явную настройку админом**.
Отклонено (унификация/маппинг справочников — отдельное решение, см. ADR 008 «Риски»).

### Ключевое решение B: Маркировка источника `merchant-default`

#### Вариант B1: Persisted-колонка `commodities.category_source varchar(32) NULL` — **выбран**

**Описание:** новая nullable-колонка. Значение `'merchant-default'` записывается только
при присвоении категории дефолтом магазина (автоимпорт и дожим). Все прочие пути записи
категории (ручной `PUT /api/commodities/{id}/category`, batch `PUT /api/commodities/categories`,
suggest-подтверждение чека, confirm precategorized) записывают/оставляют `NULL`.
`NULL` = manual/legacy/неизвестно. Читатель различает: `'merchant-default'` ≠ все остальные.

**Плюсы:**
- FR-M6 выполняется и после перезапуска (единиственный способ: suggest-source
  транзиентен, кэш in-memory — оба теряются/перезаполняются).
- Согласовано с тем, что источник категории в системе нигде ещё не persisted —
  колонка вводится минимально (одно значение), не перестраивая существующие пути.
- Дешёвая миграция (nullable varchar), без backfill — исторические строки остаются `NULL`.

**Минусы:**
- Отдельная асимметрия: для категорий, проставленных вручную/AI/precategorized
  исторически, источник неизвестен (`NULL`). Полная persistence источников всех путей —
  отдельное решение (зафиксировано как путь эволюции), в объём задачи не входит.

#### Вариант B2: Не persist, вести источник только в suggest-ответе — **отклонено**

**Минусы:** не выполняет FR-M6 («отличима от ai, precategorized, manual») — после
перезапуска по строке `commodities` невозможно сказать, кто проставил категорию;
админ не отличит дожатые дефолтом позиции от вручную подтверждённых.

#### Вариант B3: Отдельная таблица-журнал присвоений — **отклонено**

**Минусы:** аудит присвоений четырьмя путями — сквозная инфраструктура (YAGNI);
для задачи достаточно одной колонки.

### Ключевое решение C: Точка автоприменения при поступлении чека

#### Вариант C1: В `ReceiptSynchronizationService` после успешного `AddAsync` — **выбран**

**Описание:** единственная точка импорта уже резолвит `Merchant` (`ResolveMerchantAsync`)
до сохранения. После успешного `AddAsync(receipt)`: если у магазина есть валидный дефолт —
вызывается новая репозиторная операция «проставить категорию всем позициям чека без
категории» + наполнение кэша; сбой автоприменения логируется и **не валят импорт чека**.

**Плюсы:**
- Требование FR-M2 выполняется ровно «при поступлении», без фонового пересканирования.
- Изменение локально: синхронизация и так является точкой принятия решений импорта
  (dedup, пропуски, логирование) — добавляется ещё одно правило.
- Контекст запроса не нужен: commodities не покрыты query-filter — операция целится
  в `receipt_id` конкретного чека.

**Минусы:**
- Применение идёт отдельной транзакцией после фиксации чека: сбой между ними оставит
  позиции без категории. Принятая модель отказа: такая же, как уже есть (импорт чека
  коммитится до любых последующих действий); досыпание — кнопкой дожима (FR-M3).

#### Вариант C2: Внутри `ReceiptRepository.AddAsync` — **отклонено**

**Минусы:** репозиторию пришлось бы зависеть от категоризационных зависимостей
(кэш, merchant-загрузку), смешивая persistence с бизнес-правилом; `AddAsync` используется
и вне импорта — риск неявного побочного эффекта. Нарушение единственной ответственности.

#### Вариант C3: Отдельный hosted-service «досыпающий» дефолты по расписанию — **отклонено**

**Минусы:** новая фоновая машина состояний, гонки с фоновым `PrecategorizationBackgroundService`
и AI — только ради ближайшего приложения, которое доступно синхронно в точке импорта
(KISS/YAGNI).

### Ключевое решение D: Место дефолта в цепочке категоризации

#### Вариант D1: В `ICommodityCategoryResolver` — новый шаг между `existing` и `cache` — **выбран**

**Описание:** порядок становится `existing → merchant-default → cache → precategorized → ai`.
Resolver перед шагом 2 делает **один batch-запрос** дефолтов магазинов
по множеству `ReceiptId` позиций (позиции одной порции могут относиться к разным
магазинам на странице «Товары»): `receipts JOIN merchants` по `merchant_id`,
`WHERE receipts.id IN (...)` → словарь `receiptId → CommodityCategory?`.
Позиции с категорией (шаг 1, `existing`) не затрагиваются; с `receiptId` без записи дефолта —
идут дальше по текущему потоку (FR-M8).

**Плюсы:**
- Выполняет заявленный порядок приоритетов дословно (задача, критерий приёмки 5).
- Один batch-запрос на порцию (стиль FR-9.3 ADR-027: N+1 запрещён); попадание или
  промах не меняют обработку остальных позиций — частичная деградация разрешена
  (ошибка чтения — Warning + деградация к кэшу/AI, как `GetQueueCategoriesAsync`).
- Подключение правила через общий решатель даёт функцию сразу обоим потребителям
  конвейера (suggest чека и порционная страница «Товары») без правки контрактов
  эндпоинтов — аддитивное wire-значение `'merchant-default'`.

**Минусы:**
- Новый параметр-зависимость resolver'а (batch-провайдер дефолтов) — небольшой diff,
  покрытый тестами.

#### Вариант D2: Только в точке импорта, без участия resolver'а — **отклонено**

**Минусы:** suggest чека после исторических (до-функциональных) чеков не показывал бы
дефолтную категорию админу — цепочка бы «проскочила» к кэшу/AI мимо более приоритетного
дефолта (нарушение FR-M4); страница «Товары» тоже не увидела бы приоритет.

#### Вариант D3: Дефолт **выше** `existing` — **отклонено**

**Минусы:** `existing` — это категория, уже сохранённая на строке товара
(включая ранее проставленную тем же дефолтом); поднимание дефолта выше создало бы
эффект «меняю дефолт → suggest показывает новую категорию для строк,
где дефолт уже применён» при том, что сохранённые данные менять нельзя (FR-M7).
Приоритет задачи прямо ставит ручную/существующую категорию первым шагом.

### Ключевое решение E: Охват дожима кнопкой «категоризировать существующие»

#### Вариант E1: Позиции магазина **всех пользователей** — **выбран**

**Описание:** дожим — один `ExecuteUpdateAsync` с `IgnoreQueryFilters()` на
`commodities JOIN receipts` (фильтр объявлен на `Receipts`, поэтому маркер обязателен,
прецедент и комментарий-маркер — ADR-027 D8/NFR-5); условие
`receipts.merchant_id = :merchantId AND (commodities.category_id IS NULL OR = 0)`.

**Плюсы:**
- Точная семантика задачи: «позиции этого магазина без категории» — магазин не
  принадлежит ни одному пользователю, а исторические позиции одного магазина рассыпаны
  по всем пользователям; дожим per-user дал бы неполный эффект и невыполнимую ручную
  обёртку «войти в каждый аккаунт».
- Точный прецедент: confirm precategorized обновляет `commodities` всех пользователей
  (ADR-027, D10): «товары всех пользователей … содержательные категории не
  перезаписываются».
- Идемпотентность природная: повторный запуск дожимает только оставшиеся без категории
  (edge case задачи).

**Минусы:**
- Расширение границ owner-scoping ещё на одну операцию — ограничено админ-проверкой
  эндпоинта и явным `IgnoreQueryFilters()` маркером (прецедент уже есть).

#### Вариант E2: Только позиции текущего пользователя — **отклонено**

**Минусы:** см. E1; несовместимо с целью задачи (ускорить категоризацию по магазину в целом).

### Ключевое решение F: Пополнять ли in-memory кэш категориями, проставленными дефолтом

#### Вариант F1: Пополнять (`cache.TryAdd` после фиксации записи) — **выбран**

**Описание:** и при автоимпорте, и при дожиме — после фиксации записи выполняется
`TryAdd(нормализованное имя, категория)` для чтённых до записи названий обновлённых строк
(first-wins/no-op при «заморозке» — существующая семантика ADR-019).

**Плюсы:**
- Кэш остаётся согласованным с источником истины (`commodities`) **немедленно**:
  наполнение при старте (ADR-019 FR-1.2) читает **все** строки с категорией, включая
  merchant-default, — после рестарта кэш и так получит эти значения. F1 просто
  гарантирует то же состояние до рестарта; иначе кэш и БД расходились бы в двух
  точках записи при том, что обе они контролируются кодом.
- Предсказуемая семантика suggest'а: одно и то же название не даёт разных ответов
  «cache»/«merchant-default» в зависимости от того, был ли рестарт.

**Минусы:**
- Дефолтная категория магазина X распространяется через кэш на одноимённые товары
  магазинов без дефолта (`source = cache`). Компромисс принят и зафиксирован ниже
  (осознанное следствие сквозного кэша, решение заказчика ADR 009 C4/ADR-019 FR-2.4).

#### Вариант F2: Не пополнять кэш (или исключить merchant-default-строки из наполнения при старте) — **отклонено**

**Минусы:** перемена поведения после рестарта: до рестарта suggesting отдаёт
`merchant-default`, после — `cache`; наполнение при старте пришлось бы фильтровать
по `category_source` (лишняя пара колонок в горячем пути init-сервиса), а манипуляция
«кэш ≠ БД» противоречит модели ADR-019 (кэш — ускоритель над источником истины).

## Решение

Выбраны: **A1** (колонка `merchants.default_commodity_category_id integer NULL` +
валидация enum'ом, безопасное чтение), **B1** (persisted-колонка
`commodities.category_source varchar(32) NULL`, значение `'merchant-default'`),
**C1** (автоприменение в `ReceiptSynchronizationService` после фиксации чека),
**D1** (новый шаг конвейера `merchant-default` между `existing` и `cache`, batch-запрос
по `ReceiptId`), **E1** (дожим — позиции магазина всех пользователей,
`IgnoreQueryFilters()` + транзакция + `ExecuteUpdateAsync`), **F1** (кэш пополняется
после фиксации записи).

### Обоснование

1. **Дословное следование порядку приоритетов задачи** (FR-M4) и прецедентам проекта:
   этап A1 повторяет вариант хранения `proposed_category_id` (ADR-027), E1 повторяет
   confirm precategorized (глобальный дожим, D11 «NULL/0 = без категории», транзакция,
   `cache.TryAdd` после фиксации), C1 развивает единственную точку импорта.
2. **Ручные категории не перекрываются** (FR-M5): все пути записи дефолта затрагивают
   только строки `category_id IS NULL OR = 0`; suggest (read-only) вообще ничего
   не пишет; `existing`-шаг в resolver'е стоит раньше дефолта.
3. **KISS/YAGNI:** нет новых фоновых сервисов, распределённых блокировок, очередей;
   автоприменение — одна репозиторная операция, дожим — один UPDATE, конфигурация
   не расширяется (дефолт — данные, а не настройка).
4. **Отказоустойчивость:** сбой автоприменения при импорте логируется и не валит цикл
   синхронизации; сбой batch-чтения дефолтов в resolver'е деградирует без ошибки запроса
   (тот же стиль, что `GetQueueCategoriesAsync`).
5. **Single instance:** все операции локальны в транзакцию PostgreSQL; распределённая
   координация не требуется (ограничение общего плана, ADR-027 D12).

### Детали решения

#### 1. Миграция схемы PostgreSQL

Скрипт `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20261105130000_add_merchant_default_commodity_category.sql`
(транзакция на скрипт, учёт в `migration_scripts_history` — идемпотентно):

```sql
ALTER TABLE merchants
    ADD COLUMN default_commodity_category_id integer NULL,
    ADD CONSTRAINT ck_merchants_default_commodity_category
        CHECK (default_commodity_category_id IS NULL
               OR default_commodity_category_id <> 0);  -- Undefined нельзя задать дефолтом

ALTER TABLE commodities
    ADD COLUMN category_source varchar(32) NULL;
```

- Кодировка `category_source` именем строки (и не smallint) — единственное значение
  на сегодня `'merchant-default'`; расширяемость будущих источников без миграции.
- Backfill исторических строк не выполняется (`NULL` = manual/legacy — осознанно).
- Индексы **не добавляются**: дожим опирается на существующий
  `ix_receipts_merchant_id` + `ix_commodities_receipt_id` (join в сторону
  commodity→receipt отфильтрован по merchant); замер плана и индексы — по прецеденту
  «отложенное решение» ADR-027 (п. 3), при заметной стоимости — отдельным решением.

#### 2. Домен и слои (карта для плана)

| Слой / файл | Действие |
|---|---|
| `Domain/Modules/Merchants/Merchant.cs` | свойство `CommodityCategory? DefaultCommodityCategory { get; }` + метод `UpdateDefaultCommodityCategory(CommodityCategory?)` (сброс = `null`); **без инварианта на enum в домене** (валидация `Enum.IsDefined`/`!= Undefined` — в эндпоинте, домен не знает о wire-формате; прецедент `PrecategorizedCommodity` ADR-027) |
| `Domain/Modules/Merchants/IMerchantRepository.cs` | `Task<IReadOnlyDictionary<Guid, CommodityCategory?>> GetDefaultCommodityCategoriesAsync(IReadOnlyCollection<Guid> merchantIds, CancellationToken)` — batch для resolver'а; `UpdateCategoryAsync` не трогается |
| `Infrastructure/Persistence/Postgres/MerchantEntity.cs`, `Configurations/MerchantConfiguration.cs` | колонка `default_commodity_category_id`; `MapToDomain`: `Enum.IsDefined(typeof(CommodityCategory), id) && id != 0` → значение, иначе `null` (**безопасное чтение**, edge case «категория удалена из справочника»); domain→entity: `DefaultCommodityCategory?.Id` |
| `Domain/Modules/Commodities/Commodity.cs` | **без изменений** (источника у категории нет на домене; маркировка — persistence-деталь) |
| `Infrastructure/Persistence/Postgres/CommodityEntity.cs` / `Configurations/CommodityConfiguration.cs` | колонка `category_source string?` (+ маппинг в `Create`/`MapToDomain`) |
| `Application/Modules/Merchants/Models/MerchantDto.cs` (переиспользуемый) | аддитивное поле `DefaultCommodityCategoryId int?` (и `DefaultCommodityCategoryName string?` — из `CommodityCategoryHelper.GetDisplayName`, либо `null` = не задана) |
| `Application/Modules/Commodities/Models/CategorizationModels.cs` | enum `CategorizationSource.MerchantDefault`, wire-имя `MerchantDefault = "merchant-default"` в `CategorizationSourceNames` (+ `ToWireName`) |
| `Application/Modules/Merchants/Contracts/IMerchantDefaultCategoryService.cs` (новый) | `Task<MerchantDefaultApplyResult> ApplyExistingAsync(Guid adminUserId, Guid merchantId, CancellationToken)` — дожим (outcome `NotFound`/`DefaultNotConfigured`/`Applied`, см. п. 4); контракт в Application, реализация в Infrastructure (прецедент `IPrecategorizedCommodityService`) |
| `Infrastructure/Modules/Commodities/MerchantDefaultCategoryService.cs` (новый) | см. п. 4 |
| `Infrastructure/Persistence/Postgres/CommodityRepository.cs` | 2 новых метода: `ApplyMerchantDefaultForReceiptAsync(Guid receiptId, CommodityCategory category)` (для импорта) и `ApplyMerchantDefaultForMerchantAsync(Guid merchantId, CommodityCategory category)` (дожим; см. пп. 3–4) |
| `Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` | новый шаг 1.5 (см. п. 5); зависимость — batch-провайдер дефолтов (внедрение через `IMerchantRepository` или узкий контракт `IMerchantDefaultCategoryProvider` в Application — решает реализация; узкий контракт предпочтительнее для тестируемости) |
| `Infrastructure/Synchronization/ReceiptSynchronizationService.cs` | блок автоприменения после `AddAsync` (см. п. 3) |
| `Api/Modules/Merchants/MerchantEndpoints.cs` | два новых маршрута (см. п. 6) |
| `Infrastructure/Configuration/DependencyInjectionExtensions.cs` | DI: `IMerchantDefaultCategoryService` + узкий провайдер дефолтов |
| `Migrations/Scripts/20261105130000_add_merchant_default_commodity_category.sql` | миграция (п. 1) |
| Frontend | `api/merchants.ts` (`updateMerchantDefaultCategory`, `applyMerchantDefaultCategory`), `MerchantTable.tsx` (колонка), `ReceiptDetails.tsx` (селект дефолта в блоке магазина + кнопка дожима), `utils/categorizationSource.ts` (подпись `'merchant-default'`) |
| Документация | `AGENTS.md` — не расширяется (новых настроек env нет; дефолт — данные БД) |
| Backend (Go), бот, nginx, MongoDB | **без изменений** (весь `/api` уже проксируется на Analytics) |

#### 3. Автоприменение при поступлении чека (C1, FR-M2)

В `ReceiptSynchronizationService`, после успешного `_receiptRepository.AddAsync(receipt)`:

1. `var defaultCategory = merchant.DefaultCommodityCategory;` (уже резолвлен и
   безопасно прочитан из БД).
2. Если `null` → ничего не происходит (FR-M8, нулевой overhead).
3. Иначе вызывается `CommodityRepository.ApplyMerchantDefaultForReceiptAsync`
   (один метод, одна транзакция БД):
   - `SELECT name` позиций чека с `CategoryId == null || 0` (для кэша и счётчика);
   - `ExecuteUpdateAsync` по `commodities WHERE receipt_id = :receiptId AND (category_id IS NULL OR category_id = 0)`:
     `CategoryId = (int)category`, `CategoryName = CommodityCategoryHelper.GetDisplayName(category)`,
     `CategorySource = "merchant-default"`;
   - после фиксации — `_cache.TryAdd(CommodityNameNormalizer.NormalizeName(name), category)`
     по каждому обновлённому названию (F1, семантика first-wins ADR-019); **TryAdd выполняется
     внутри самой репозиторной операции, сразу после commit** (прецедент
     `PrecategorizedCommodityRepository.ConfirmProposedAsync`, ADR-027 п. 4);
   - возвращает число обновлённых строк (логируется).
4. Исключение из операции — `catch (Exception ex) → LogError` и продолжение импорта
   (существующие правила обработки ошибок синхронизации «по чеку, не валя цикл»
   не меняются; досыпание — кнопкой).
   Позиции без категории пока останутся без неё; новая категория применяется к будущим
   чекам (FR-M7 сохраняется по построению: правило привязано к моменту импорта).

Гонки внутри сессии импорта нет: в один чек пишем только мы (dedup), кэш потокобезопасен.

#### 4. Дожим кнопкой «категоризировать существующие» (E1, FR-M3)

`MerchantDefaultCategoryService.ApplyExistingAsync(adminUserId, merchantId)`:

1. Загрузить merchant (репозиторий, **без** IgnoreQueryFilters — merchants не фильтруются);
   `null` → outcome `NotFound` (эндпоинт → 404).
2. Дефолт не задан (включая «некорректный код из БД», приведённый к `null` при
   безопасном чтении: `DefaultCommodityCategory == null`) → outcome
   `DefaultNotConfigured` (эндпоинт → 400 текстом; крайний случай «категория удалена
   из справочника»).
3. Иначе — вызов `CommodityRepository.ApplyMerchantDefaultForMerchantAsync(merchantId, category)`
   (транзакция, выборка имён, UPDATE, счётчик и `TryAdd` — **внутри репозиторной операции**,
   T-04; сервис дожима транзакцию не дублирует):
   - distinct-имена позиций магазина без категории
     (`commodities JOIN receipts … receipts.merchant_id = :merchantId`,
     `IgnoreQueryFilters()` **явно с комментарием** «доступ вне owner-scoping намеренный:
     дожим по магазину обрабатывает позиции всех пользователей (ADR-032, E1)» —
     маркер grep-able как в ADR-027);
   - `ExecuteUpdateAsync` по тому же предикату: `CategoryId`, `CategoryName`,
     `CategorySource = "merchant-default"`; возвращает `updatedCount`;
   - `Cache.TryAdd` по нормализованным именам **после фиксации транзакции**, внутри
     операции (решение F1, прецедент ADR-027 п. 4); при `updatedCount = 0` — ни одного `TryAdd`;
   - ответ — `updatedCount` (может быть 0: все уже категоризированы —
     повторное нажатие кнопки идемпотентно, edge case задачи).
4. **Форма результата сервиса — outcome-объект, а не исключения** (решение по открытому
   вопросу декомпозиции № 2):
   `MerchantDefaultApplyResult(MerchantDefaultApplyOutcome Outcome, int UpdatedCount)`,
   исходы `NotFound` / `DefaultNotConfigured` / `Applied` — по образцу
   `ConfirmPrecategorizedResult(PrecategorizedOperationOutcome, …)` (ADR-027).
   **Обоснование:** оба не-успеха — ожидаемые, рядовые бизнес-состояния админского
   сценария (кнопка нажата при снятом дефолте / при устаревшем списке магазинов),
   а не исключительные ситуации; контролирующий поток через `try/catch` сложнее
   тестировать исчерпывающе и противоречит прецеденту precategorized-пути
   (outcome-enum + результат-record, тесты матчят исход, а не тип исключения).
   `Applied` с `UpdatedCount = 0` — успех (идемпотентность). Исключения инфраструктуры
   (сбой БД и т.п.) пробрасываются как есть — их перехватывает глобальное правило
   запроса (500), а не сервис.

Порядок с фоновой сессией precategorization: обе операции пишут только позиции
без категории, один UPDATE атомарен — пересечение безопасно (никакой distributed lock
не требуется); worst case — precategorized-предложение для имени, которое дожим
уже заполнил, попадёт в очередь лишний раз и, при подтверждении, обновит 0 строк
(заложено в ADR-027 D10).

#### 5. Общий конвейер: шаг `merchant-default` (D1, FR-M4)

В `CommodityCategoryResolver.ResolveAsync` между шагами 1 и 2:

1. Собрать `receiptIds` позиций без категории (шаги `existing` пропустили их).
2. Один batch-запрос дефолтов: `receipts JOIN merchants` (`AsNoTracking`)
   `WHERE receipts.id IN (receiptIds)` → `receiptId → merchant.default_commodity_category_id`
   (безопасное чтение уже выполнено в `MapToDomain`: невалидные коды → `null`).
3. Позиции с дефолтом: результат `source = "merchant-default"`, ИИ/кэш/очередь не
   вызываются; остальные — шаги 2–4 без изменений.
4. Ошибка чтения → Warning + словарь «пуст» (все идут дальше по потоку), стиль
   `GetQueueCategoriesAsync` (FR-9.6). `OperationCanceledException` — пробрасывается.
5. Suggest остаётся read-only: предложения дефолта никем в нём не сохраняются;
   автоприменение при импорте — единственная точка сохранения без подтверждения
   (по требованию задачи).

Кэш **не** пополняется resolver'ом: категория по дефолту пишется в БД только
в операциях п. 3/4, где и выполняется `TryAdd`.

#### 6. HTTP API (стиль `MerchantEndpoints`: `401`/`Results.StatusCode(403)`/текстовые ошибки)

| Метод и путь | Назначение | Коды |
|---|---|---|
| `PUT /api/merchants/{merchantId:guid}/default-commodity-category` | установить/сбросить (`{ "categoryId": int \| null }`; `null` = сброс). Валидация: merchant существует → `404`; `categoryId != null` и (`!Enum.IsDefined` **или** `== 0`(Undefined)) → `400` | `200 { categoryId, categoryName }` (сброс → `{ categoryId: null, categoryName: null }`), `400`, `404`, `401`, `403` |
| `POST /api/merchants/{merchantId:guid}/default-commodity-category/apply` | дожим исторических позиций магазина (FR-M3) | `200 { updated: int }`, `400` («Default commodity category is not set for this merchant.»), `404`, `401`, `403` |

Проверки прав — как у всех методов `MerchantEndpoints` (по `IUserRepository.GetByIdAsync → IsAdmin`).
`403` — `Results.StatusCode(403)` по уточнению ADR-027 (дефект `Results.Forbid()`).
Nginx не меняется (см. ADR-009 H2). Дефолт — данные, а не настройка приложения:
конфиг-секций и env-переменных новых нет (AGENTS.md не расширяется).

#### 7. Frontend

- `api/merchants.ts`: `updateMerchantDefaultCategory(merchantId, categoryId | null)`,
  `applyMerchantDefaultCategory(merchantId): Promise<{ updated: number }>`
  (`credentials: 'include'` — опции fetch, прецедент ADR-011 C1).
- `MerchantTable.tsx` (только при `isAdmin`): новая колонка «Категория товаров (по умолчанию)» —
  текущее значение (имя через `CommodityCategoryHelper` — приходит в DTO) или «Не задана»;
  кнопка «ред.» → `<select>` из `fetchCategories()` (`api/commodities.ts`, товарный
  справочник, **без** `Undefined`); сохранение через updateMerchantDefaultCategory;
  сброс выбором «— не задана —» (`null`). Отдельные состояния редактирования
  (`editingDefaultId`, `defaultSavingId`) — не пересекаются с именем/категорией магазина
  (одновременно редактируется одно поле в строке).
- `ReceiptDetails.tsx` (админ):
  - в блоке магазина рядом с «ред.» имени — селект «категория товаров по умолчанию»
    (тот же API; отображение текущего значения с `receipt.merchant.defaultCommodityCategoryName`);
  - кнопка «Категоризировать существующие» рядом с дефолтом: вызывает
    `applyMerchantDefaultCategory(receipt.merchant.id)`, toast
    `Обновлено товаров: {updated}`; ошибки — `showDialog`/`toast.error` (прецедент
    `saveSelectedCategories`). Кнопка показывается только при админе и заданном дефолте;
    пустой ответ (0) — информационный toast («Все позиции уже категоризированы»).
- `utils/categorizationSource.ts`: подпись источника `'merchant-default'`
  (например, «по умолчанию магазина») — аддитивно; существующеие source-имена не меняются.
- Тестов фронт нет (`npm test` не существует, ADR-011 E1): сборка + lint + ручной чек-лист.

#### 8. Edge cases задачи → поведение решения

| Edge case | Поведение |
|---|---|
| Нет дефолта | Поставщик дефолтов в resolver'е возвращает `null` → обычный поток (FR-M8); автоимпорт пропускает блок; дожим отвечает `400`; UI скрывает кнопку дожима |
| Ручная категория у товара | Не трогается нигде: блок импорта затрагивает только `NULL/0`, дожим — только `NULL/0`, suggest read-only, `existing`-шаг выше дефолта (FR-M5) |
| У товара категория от AI/precategorized/кэша | Содержательная категория (`≠ NULL/0`) — тот же не-трогательный предикат; при новом импорте такая ситуация невозможна (позиции новые, `NULL`); в suggest дефолт бьёт **машинные предложения** (кэш/precategorized/AI), но не сохранённую категорию строки (FR-M4) |
| Смена/снятие дефолта | Не трогает сохранённые строки: ни один путь записи не читает историю дефолта (FR-M7); новые чеки получают новую категорию; повторный дожим — только позиции без категории |
| Дефолт → категория удалена/переименована из справочника | Удалить из enum нельзя без релиза; при смене состава код «выходит» из enum → безопасное чтение (`MapToDomain`) отдаёт `null` → тракт как «не задана»; админ видит пусто и перезаписывает. CHECK `<> 0` предотвращает Undefined-дефолт |
| Чек без привязки к merchant | Импорт невозможен без merchant (`ResolveMerchantAsync` обязателен, иначе чек пропускается — существующее правило); в resolver'е позиция с `receiptId` без записи дефолта просто промахивается мимо шага |
| Partial failure при импорте | Автоприменение после чека в отдельной транзакции: сбой → позиции без категории, лог Error, дожим досыпает |
| Повторное нажатие кнопки | Идемпотентно: `updated = 0` для уже заполненных позиций |

## Последствия

### Положительные

- Массовая категоризация по магазину одним действием; новые чеки «попадают» в
  аналитику категорий без ручного участия.
- Ручные категории и семантика suggest/apply чека, страницы «Товары» и precategorization
  сохранены без изменений; расширение конвейера — аддитивный шаг и wire-значение.
- Источник `merchant-default` persisted — отличимость устойчива к перезапускам (FR-M6).
- Естественный путь эволюции: следующий шаг (вне объёма) — дефолт как подсказка AI
  (роль в промте) и глобальный дожим; архитектура позволяет без изменения контрактов.

### Отрицательные

- Категории, проставленные дефолтом магазина, попадают в сквозной in-memory кэш и
  влияют на предложения по одноимённым товарам **других** магазинов (F1 — осознанный
  компромисс, согласуемый с принятой моделью «кэш глобален для всех пользователей»).
- Источник категорий для остальных путей записи остаётся неизвестным (`NULL`);
  полная персистентность источников всех путей — отдельное решение.
- Новая колонка в горячей таблице `commodities` (незначительный рост объёма строки).
- Дожим — один UPDATE по join: на больших объёмах исторических позиций транзакция
  может быть заметной; UI ждёт синхронного ответа со счётчиком `updated`.
  **Митигация:** изменение/снятие дефолта + повторные (идемпотентные) нажатия кнопки;
  при необходимости — порционирование по `receipt_id` отдельным решением.
- **Кэш распространяет merchant-default-категории за пределы магазина** (F1) —
  осознанно, согласуется с «кэш сквозной по всем пользователям» (ADR 009 C4).
  При переосмыслении — исключение `category_source = 'merchant-default'` из наполнения
  кэша при старте; для этого колонка и остаётся в схеме.

### Компромиссы

- **Кэш получает merchant-default-значения немедленно (F1)** vs «кэш не знает о
  дефолтах»: выбрана согласованность кэш↔БД (после рестарта — те же значения),
  отклонена фильтрация наполнения по `category_source`.
- **Автоприменение без подтверждения** — единственное место, где категория пишется
  в `commodities` без явного действия пользователя/AI-подтверждения. Это прямо
  требование задачи («автоматически при поступлении»); источник помечается
  `'merchant-default'`, отмена — дожим применим только к `NULL/0`, а ручная правка
  строки администратором перекрывает значение по всем существующим контрактам.
- **Валидация enum на записи в эндпоинте, а не в домене**: домен получает «сырое»
  значение; безопасное чтение в `MapToDomain` — единственная точка приведения к
  «нет/default». Согласовано с `PrecategorizedCommodity` (ADR-027 D11).
- **No distributed lock**: single instance, все записи — атомарные SQL-операции;
  пересечение с фоном precategorization безопасно (см. п. 4).

### Риски

- **Rolling deploy**: новые колонки добавляются аддитивно (NULLable), старый код
  работает; после деплоя Analytics остаётся применить миграцию `MigrationRunner`
  до старта API (существующая процедура). Откат релиза — колонки остаются
  (старый код их не трогает), расчистка не требуется.
- **Дожим на очень большом историческом объёме**: замерить длительность первого
  запуска; при необходимости (не сейчас) — порционирование по `receipt_id` с повторами
  кнопки (идемпотентность уже обеспечена) — отдельное решение.
- **Рост числа категорий-справочника**: batch-запрос дефолтов — один JOIN, отказоустойчив
  (деградация к кэшу/AI).
- **403-дефект `Results.Forbid()`** — известный, вынесен в бэклог ADR-027
  (в новых эндпоинтах пишем `Results.StatusCode(403)` сразу).

## Реестр приёмки (критерии задачи → решение)

| Критерий | Где закрыт |
|---|---|
| Задать/изменить дефолт в `/merchants` и «Детали чека» | Endpoints п. 6, UI п. 7 (MerchantTable + ReceiptDetails) |
| Хранится отдельно от категории магазина | `merchants.default_commodity_category_id` ≠ `merchants.category` (A1) |
| Новые чеки категоризируются автоматически | П. 3 (`ReceiptSynchronizationService`) |
| Дожим только позиции конкретного магазина без категории | П. 4 (E1, предикат `merchant_id + NULL/0`) |
| Порядок приоритетов | П. 5 (D1: existing → merchant-default → cache → precategorized → ai) |
| Ручные не перезаписываются | П. 8; `existing`-шаг выше; предикат `NULL/0` везде |
| Источник `merchant-default`, отличимый | B1 (persisted-колонка + wire-значение `merchant-default`) |
| Магазины без дефолта — прежний поток | D1/п. 3 (null → прыжок мимо шага) |
| Смена дефолта не меняет сохранённые товары | FR-M7, «no retroactive» по построению всех путей |
| Unit-тесты; ручная проверка UI | Тесты resolver/синхронизации/эндпоинтов; фронт — build+lint+ручной чек-лист (прецедент ADR-011 E1) |

## Связь с другими ADR

- **ADR 009** — контракты suggest/apply чека и поведение `503` при пустом `AI:BaseUrl`
  не изменяются; конвейер (решение ADR-009 E1/I1 и др.) расширяется аддитивным шагом.
- **ADR 019** — кэш: семантика `TryAdd`/first-wins/«замерзание» не меняется; только
  два **новых** места вызова `TryAdd` (автоимпорт, дожим) — в стиле существующих
  точек записи (ADR-028: подтверждение чека, batch-страница «Товары»; ADR-027:
  confirm precategorized).
- **ADR 027** — переиспользуется эталон админских эндпоинтов (401/403/текстовые ошибки),
  `IgnoreQueryFilters()`-маркер и семантика D11 («NULL/0 = без содержательной категории»).
- **ADR 030** — рабочее окно precategorization не затрагивается (дефолт не ходит через
  фоновую сессию; пересечение — безопасно, п. 4).

## Ссылки

- [Задача: Категория товаров по умолчанию для магазина](../tasks/merchant-default-commodity-category.md)
- [ADR 009: Автоматическая категоризация товаров в чеке](009-auto-commodity-categorization.md)
- [ADR 008: Управление категориями магазинов](008-merchant-category-management.md)
- [ADR 019: In-memory кэш категорий товаров](019-in-memory-commodity-category-cache.md)
- [ADR 027: Фоновая предкатегоризация товаров](027-background-commodity-precategorization.md)
- [ADR 028: Порционная категоризация страницы «Товары»](028-commodities-page-categorization.md)
- [ADR 011: Редактирование имени магазина в /merchants](011-merchant-name-editing.md)
- Фактический код (проверен 2026-11-05): `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/Merchant.cs`,
  `.../Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs`,
  `.../Infrastructure/Modules/Commodities/CommodityCategorizationService.cs`,
  `.../Infrastructure/Modules/Commodities/CommodityBatchCategorizationService.cs`,
  `.../Infrastructure/Synchronization/ReceiptSynchronizationService.cs`,
  `.../Infrastructure/Persistence/Postgres/{MerchantEntity,CommodityEntity,CommodityRepository,PrecategorizedCommodityRepository}.cs`,
  `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs`,
  `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CategorizationModels.cs`,
  `Analytics/frontend/src/components/{MerchantTable,ReceiptDetails}.tsx`,
  `Analytics/frontend/src/api/{merchants,commodities,receipts}.ts`
