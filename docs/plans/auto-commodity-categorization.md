# План: Автоматическая категоризация товаров в чеке

## Описание задачи

Реализовать сценарий автоматической категоризации позиций чека по [задаче auto-commodity-categorization](../tasks/auto-commodity-categorization.md) в соответствии с архитектурным решением [ADR 009](../adr/009-auto-commodity-categorization.md).

Схема: **webUI (`Analytics/frontend`) → nginx → Analytics HTTP API (.NET) → PostgreSQL**. Логика — в Analytics (.NET 8), категории позиций проставляются в PostgreSQL (колонки `commodities.category_id`/`category_name`). MongoDB используется **только для чтения при синхронизации** (как сейчас: `MongoReceiptBatchLoader` → `MongoReceiptMapper` → PostgreSQL). Go-backend и Telegram-бот не изменяются, nginx не изменяется (весь `/api` уже проксируется на Analytics). Справочник категорий — `CommodityCategory` (`Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs`).

Ключевые требования:
- Инициация и подтверждение — только в webUI; автоматическое сохранение запрещено (FR-3.2).
- Алгоритм приоритетов: существующая категория позиции → кэш ранее присвоенных → ИИ (FR-1.4).
- ИИ возвращает JSON `{"category": "<имя значения CommodityCategory>"}` (например, `{"category": "Food"}`) строго из `CommodityCategory` (**43 значения: 0–17, 18–41, `Other = 255`; в промт без `Undefined` попадают 42 категории** — состав расширен ADR 010); числовой формат `{"category_id": N}` отклонён решением заказчика; частичный успех (UC-4).
- **Каркас решения ADR 009 не меняется** при расширении справочника (ADR 010): JSON `{"category": "Name"}`, валидация `Enum.TryParse` по имени + `Enum.IsDefined`, генерация промта из `GetAll()` без `Undefined`, приоритеты «existing → cache → ai» остаются прежними; расширение влияет только на состав промта (требуются few-shot и эвристики — см. задачу 4).
- Адрес/таймаут ИИ — через конфигурацию (appsettings / env `AI__*`), без пересборки (NFR-1).
- Кэш ранее присвоенных категорий — **сквозной для всех пользователей** (таблица без `user_id`, уникальность по `normalized_name`); категории из MongoDB не переносятся (решения заказчика).
- Списочное представление **всех позиций всех чеков** с серверной фильтрацией (UC-6/FR-5): расширение `GET /api/commodities` параметром `categoryFilter`.
- Порт Analytics **не меняется** (остаётся `5039`); задача не затрагивает конфигурацию портов.

## Задачи

### 1. Нормализация имён и модель кэша (Domain)

**Описание:** Вспомогательный код для алгоритма категоризации в доменном слое.
- Файл `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityNameNormalizer.cs`: `NormalizeName(string) string` — lowercase + trim + схлопывание пробелов (NFR-3.1).
- Файл `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategoryAssignment.cs`: доменная сущность записи кэша (`NormalizedName`, `Name`, `CategoryId`, `CategoryName`, `UpdatedAt`) — **без `UserId`** (кэш сквозной для всех пользователей, решение заказчика).
- Файл `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/ICategoryAssignmentRepository.cs`: `GetByNormalizedNameAsync(string normalizedName, CancellationToken)` и `UpsertAsync(string name, string normalizedName, CommodityCategory category, CancellationToken)` — **без userId**.

**Затрагиваемые сервисы/файлы:** `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/` (новые файлы).

**Зависимости:** нет.

**Приоритет:** P0
**Оценка времени:** 0.5 дня

### 2. Миграция БД: таблица `commodity_category_assignments` (PostgreSQL)

**Описание:** Создание таблицы кэша «ранее присвоенных категорий» (ADR 009, решение C1).
- Новый SQL-скрипт в `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/` (именование по образцу `20241019160000_initial_create.sql`, порядковый номер — текущее время/дата):
```sql
CREATE TABLE commodity_category_assignments
(
    id uuid PRIMARY KEY,
    normalized_name varchar(256) NOT NULL,
    name varchar(256) NOT NULL,
    category_id integer NOT NULL,
    category_name varchar(128) NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE UNIQUE INDEX ux_commodity_category_assignments_normalized_name
    ON commodity_category_assignments (normalized_name);
```
- Таблица **без `user_id`** — кэш сквозной для всех пользователей (решение заказчика); одна запись на нормализованное имя.
- Проверить, что `MigrationRunner` подхватывает новый скрипт автоматически (см. `ReceiptCollector.Analytics.Migrations`).

**Затрагиваемые сервисы/файлы:** `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/` (новый скрипт).

**Зависимости:** нет (может выполняться параллельно с задачами 1, 3).

**Приоритет:** P0
**Оценка времени:** 0.5 дня

### 3. Репозиторий кэша `CategoryAssignmentRepository` (Infrastructure)

**Описание:** Реализация `ICategoryAssignmentRepository` поверх EF Core.
- Файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityCategoryAssignmentEntity.cs` (или в `ReceiptEntity.cs` по образцу `CommodityEntity`).
- Файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/CommodityCategoryAssignmentConfiguration.cs`: маппинг на таблицу `commodity_category_assignments`, `HasMaxLength` для `normalized_name`/`name`.
- Добавить `DbSet<CommodityCategoryAssignmentEntity>` в `ReceiptDbContext`.
- Файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CategoryAssignmentRepository.cs`: `GetByNormalizedNameAsync` (точечный `FirstOrDefault` по `normalized_name`) и `UpsertAsync` (поиск по уникальному индексу `normalized_name` + `SaveChangesAsync`) — без `userId`.
- `ProjectDependencyTests` не должны нарушаться (репозиторий — в Infrastructure, контракт — в Domain).

**Затрагиваемые сервисы/файлы:** `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/`, `Modules/Commodities/`.

**Зависимости:** задачи 1, 2.

**Приоритет:** P0
**Оценка времени:** 1 день

### 4. ИИ-клиент и конфигурация (Application + Infrastructure)

**Описание:** HTTP-клиент к внешней модели (ADR 009, решение F1).
- Файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/AiOptions.cs`: секция `AI` (в `appsettings*.json`), свойства `BaseUrl`, `Model`, `Timeout`, `Concurrency`, `ApiKey`.
- Файл `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/IAiClient.cs`: `SuggestAsync(string name, IReadOnlyCollection<CommodityCategory> allowed, CancellationToken)` → результат «категория | ошибка».
- Файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/AI/OpenAiCompatibleAiClient.cs`: `POST {BaseUrl}` OpenAI-совместимый формат чата, `Authorization: Bearer` при наличии ключа, таймаут из `AiOptions.Timeout`; извлечение `choices[0].message.content`, строгий JSON-parse, ожидается поле **`category`** — имя значения enum (`"Food"`); валидация через `Enum.TryParse<CommodityCategory>(category, ignoreCase: true, out ...)` + `Enum.IsDefined(...)` и `!= CommodityCategory.Undefined`; числовой формат `category_id` не принимается; ретрай 1 раз только на сетевые ошибки/таймаут; невалидный JSON не ретраится.
- Промт генерируется из `CommodityCategoryHelper.GetAll()` без `Undefined` (см. ADR 009, п. 4) — **42 категории** (состав после ADR 010). Требования к промту (ADR 010, решение E1):
  - **few-shot примеры**: несколько пар «название товара → категория» (напр., «Кофе в зёрнах 250г» → Groceries, «Капучино 0.3» → Beverages, «Филе куриное» → Poultry, «Стейк говяжий» → Meat, «Хлеб Бородинский» → Bakery, «Шоколад Alpen Gold» → Confectionery, «Шаурма с курицей» → FastFood, «Салат Оливье кулинария» → ReadyMeals);
  - **эвристики кофе/чай**: «Кофе в зёрнах / молотый / растворимый / капсулы», «Чай в пакетиках / листовой» → Бакалея; «Капучино», «Латте», «Американо», «Кофе с собой» → Напитки;
  - **разграничение схожих категорий**: `Meat` vs `Poultry`; `Vegetables` vs `Fruits`; `Bakery` vs `Confectionery`; `ReadyMeals` vs `FastFood`; `Groceries` vs `Food` (запасная).
  - Список категорий в промте **не хардкодится** — генерируется из `GetAll()` без `Undefined` (каркас решения ADR 009 не меняется).
- Биндинг опций — в `DependencyInjectionExtensions.ConfigureInfrastructureOptions`.

**Затрагиваемые сервисы/файлы:** `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/Options/AiOptions.cs`, `AI/OpenAiCompatibleAiClient.cs` (новые); `Configuration/DependencyInjectionExtensions.cs`.

**Зависимости:** задача 1 (справочник категорий — уже существует в Domain).

**Приоритет:** P0
**Оценка времени:** 1 день

### 5. Сервис категоризации (Application + Infrastructure)

**Описание:** Оркестрация алгоритма из ADR 009, п. 3, и частичного успеха (UC-4).
- Файл `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityCategorizationService.cs`:
  - `Task<CategorizationSuggestResult> SuggestForReceiptAsync(Guid userId, Guid receiptId, CancellationToken)`.
  - `Task<int> SaveCategoriesAsync(Guid userId, Guid receiptId, IReadOnlyCollection<SaveCategoryUpdate> updates, CancellationToken)`.
- Файл `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CategorizationModels.cs`: `CategorizedItemDto { CommodityId, Name, Category, CategoryName, Source, Error }` (категория — **имя** enum, `"Food"`, или `null`), `CategorizationSuggestResult { ReceiptId, Items }`, `SaveCategoryUpdate { CommodityId, Category }`.
- Файл `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityCategorizationService.cs`:
  - Загрузка чека через `IReceiptReadService.GetByIdAsync(userId, receiptId)` (owner-scoped, `404` на уровне эндпоинта).
   - Для каждой позиции: (1) своя категория (`CategoryId` задан) → `source=existing`; (2) кэш по `NormalizeName` → `GetByNormalizedNameAsync(normalized)` (без userId, сквозной кэш) → `source=cache`; (3) ИИ (семафор `AI:Concurrency`, `CancellationTokenSource` с таймаутом) → `source=ai`; при ошибке — `source=undefined` + `error` (без прерывания остальных).
   - Сохранение: для каждого `SaveCategoryUpdate` — `ICommodityRepository.UpdateCategoryAsync(commodityId, category)` (существующий метод; `null` = сброс категории в `category_id = NULL`) + `ICategoryAssignmentRepository.UpsertAsync(name, normalizedName, category)` для позиций с назначенной категорией.

**Затрагиваемые сервисы/файлы:** `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/` (новые), `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/` (новый).

**Зависимости:** задачи 3, 4.

**Приоритет:** P0
**Оценка времени:** 1.5 дня

### 6. HTTP-эндпоинты категоризации (Api)

**Описание:** Эндпоинты E1 из ADR 009, п. 5, в стиле `ReceiptEndpoints`/`CommodityEndpoints`.
- Файл `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs`: группа `app.MapGroup("/api/receipts")`, тег `Receipts`, регистрация `app.MapReceiptCategorizationEndpoints()` в `Program.cs`:
  - `POST "/{id:guid}/categories/suggest"` — `UserContext.UserId` (иначе `401`), `ICommodityCategorizationService.SuggestForReceiptAsync`; чек не найден/не владелец → `404`; `AI:BaseUrl` пуст → `503`; иначе `200` с `CategorizationSuggestResult`.
  - `PUT "/{id:guid}/categories"` — тело `SaveCategoriesRequest { Items: [{ commodityId, category }] }`, где `category` — **имя** значения `CommodityCategory` (`"Food"`) или `null` (сброс); валидация по `Enum.TryParse` (по имени) + `Enum.IsDefined`, не `Undefined`, `null` допустим → `400`; позиции не принадлежат чеку владельца → `400`; результат — `200 {"receiptId","updated"}`.
- Файл `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Commodities/CommodityEndpoints.cs`: расширение `GET /api/commodities` параметром `categoryFilter=any|uncategorized|undefined` (решение I1 ADR 009; невалидное значение → `400`); `ICommodityReadService.GetAsync`/`GetTotalCountAsync` получают параметр фильтра (`uncategorized` → `category_id IS NULL`, `undefined` → `category_id = (int)CommodityCategory.Undefined`).
- DTO-доработка: добавить `Id` (GUID `commodities.id`) в `ReceiptItemDto` (`Analytics/src/ReceiptCollector.Analytics.Application/Modules/Receipts/Models/ReceiptDetailsDto.cs`) и в маппинг `ReceiptEntity.MapToDomain()` — без этого невозможно сохранять категории по `commodityId`.
- DI-регистрации сервиса, ИИ-клиента и репозитория кэша — в `DependencyInjectionExtensions.AddInfrastructure`.

**Затрагиваемые сервисы/файлы:** `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs`, `Modules/Receipts/ReceiptCategorizationEndpoints.cs` (новый); `Modules/Commodities/CommodityEndpoints.cs` (расширение фильтром `categoryFilter`); `Application/Modules/Receipts/Models/ReceiptDetailsDto.cs`; `Application/Modules/Commodities/Contracts/ICommodityReadService.cs` и `Infrastructure/Modules/Commodities/CommodityReadService.cs` (параметр фильтра); `Infrastructure/Persistence/Postgres/ReceiptEntity.cs`; `Configuration/DependencyInjectionExtensions.cs`.

**Зависимости:** задачи 4, 5.

**Приоритет:** P0
**Оценка времени:** 1.5 дня

### 7. Юнит-тесты Analytics (.NET)

**Описание:** Покрытие алгоритма, ИИ-клиента, репозитория и эндпоинтов (критерий приёмки 8 задачи; паттерн — `MerchantEndpointsTests`, NSubstitute + `UserContext.SetUserId`).
- `CategorizationServiceTests.cs`: приоритеты «existing → cache → ai» (мок `IReceiptReadService`, `ICategoryAssignmentRepository`, `IAiClient`); частичный успех при падении ИИ по позиции (UC-4); ИИ не вызывается при попадании в кэш (FR-1.2); `Undefined` не предлагается.
- `OpenAiCompatibleAiClientTests.cs`: валидный JSON с именем категории (`{"category":"Food"}`), невалидный JSON, неизвестное имя категории, числовой `category_id` (отклоняется), таймаут (мок `HttpMessageHandler`).
- `CategoryAssignmentRepositoryTests.cs`: `GetByNormalizedNameAsync`/`UpsertAsync` без userId; сквозной кэш (категория от одного пользователя находится для другого при совпадении нормализованного имени); уникальность по `normalized_name` (InMemory/стенд PostgreSQL — по образцу `PostgresReceiptRepositoryTests`).
- `ReceiptCategorizationEndpointsTests.cs`: `401` без пользователя, `404` чужого/отсутствующего чека, `400` невалидных категорий/позиций, `503` без `AI:BaseUrl`, `200` успешного suggest/save.
- `CommodityEndpointsTests.cs` (расширение): `categoryFilter=uncategorized` возвращает только `category_id IS NULL`; `categoryFilter=undefined` — только `category_id = 0`; невалидное значение → `400`.
- Консистентность справочника (состав 43 значения, каждый член имеет отображаемое имя, `Food` = «Прочая еда», группировка UI) — покрыта отдельным `CommodityCategoryTests.cs` (создан в задаче «Расширение справочника категорий товаров», ADR 010); при правках справочника сверяться с ним.
- Запуск: `cd Analytics && dotnet test`.

**Затрагиваемые сервисы/файлы:** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/` (новые файлы).

**Зависимости:** задачи 1–6 (после стабилизации контрактов).

**Приоритет:** P1
**Оценка времени:** 2 дня

### 8. Интеграция с webUI (`Analytics/frontend`)

**Описание:** UX-флоу из ADR 009, п. 6: кнопка «Категоризировать» на странице чека, колонка «Категория» с select, индикация «предложено» (FR-3.6), ручное редактирование (FR-3.4), сохранение по явному действию (FR-3.5). Плюс списочное представление **всех позиций всех чеков** с серверной фильтрацией (UC-6/FR-5, решение I1 ADR 009).
- `Analytics/frontend/src/api/receipts.ts`: функции `suggestReceiptCategories(receiptId)` (`POST /api/receipts/{id}/categories/suggest`) и `saveReceiptCategories(receiptId, items)` (`PUT /api/receipts/{id}/categories`); `credentials: 'include'`.
- `Analytics/frontend/src/types/receipt.ts`: исправить `ReceiptItem.categoryId` на `number | null` (сейчас `string | null` — рассогласование с `int?` из DTO); добавить `id: string` (commodityId).
- `Analytics/frontend/src/components/ReceiptDetails.tsx`: колонка «Категория» в таблице позиций (select по категориям из `fetchCategories()` из `api/commodities.ts`), кнопка «Категоризировать», индикатор «предложено» для `source = ai|cache`, пометка «категория не определена» для `source = undefined` (UC-4), кнопка «Сохранить», обработка ошибок через существующий `CustomDialog`.
- `Analytics/frontend/src/api/commodities.ts`: функция `fetchCommodities({limit, offset, categoryFilter, signal})` (параметр `categoryFilter` → `GET /api/commodities?categoryFilter=...`).
- `Analytics/frontend/src/components/CommoditiesPage.tsx`/`CommodityTable.tsx`: фильтр «без категории» / «`CommodityCategory.Undefined`» / «все» (передаётся серверу через `categoryFilter`; FR-5.2), пагинация — как существующая; из строк — переход к чеку и ручная категоризация (`PUT /api/commodities/{id}/category`).
- Маршрутизация и меню не меняются (страница деталей чека и список позиций уже существуют).

**Затрагиваемые сервисы/файлы:** `Analytics/frontend/src/api/receipts.ts`, `types/receipt.ts`, `components/ReceiptDetails.tsx`, `api/commodities.ts`, `components/CommoditiesPage.tsx`, `components/CommodityTable.tsx` (и при необходимости `types/commodity.ts`).

**Зависимости:** задачи 6, 7 (контракты должны быть стабильны).

**Приоритет:** P1
**Оценка времени:** 3 дня

### 9. Ручное тестирование end-to-end (критерии приёмки)

**Описание:** Проверка всех критериев приёмки из задачи (раздел 6) в новой конфигурации.
- Подготовка: миграции (`cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run`), запуск Analytics API и frontend, настроенный `AI:BaseUrl` (напр., локальный Ollama/Qwen; пример — `QWEN.md`).
- Проверка через curl (до интеграции webUI):
   1. `POST /api/receipts/{id}/categories/suggest` → предложения; данные в PostgreSQL не меняются (критерий 4).
   2. Повторный вызов для позиции с ранее сохранённой категорией → `source=cache`, ИИ не вызывается (критерий 2).
   3. Новое название → `source=ai`, валидный JSON-ответ с **именем** категории (`{"category":"Food"}`) из `CommodityCategory` (критерий 3).
   4. `AI:BaseUrl` на несуществующий адрес → `source=undefined`, частичный результат (критерий 6).
   5. Изменение конфигурации (таймаут/адрес) без пересборки → поведение меняется (критерий 7).
   6. `PUT /api/receipts/{id}/categories` → сохранение выбора пользователя в `commodities.category_id`/`category_name` + запись в `commodity_category_assignments`, включая ручную правку (критерий 5).
- После интеграции webUI — ручная проверка сценария UC-1…UC-5 в браузере (критерии 1, 8).
- Проверка списка всех позиций: фильтры «без категории» и «`CommodityCategory.Undefined`» применяются на сервере и возвращают только соответствующие позиции (критерий 10).
- Проверка сквозного кэша: категория, сохранённая под одним пользователем, предлагается другому при совпадении нормализованного названия, ИИ не вызывается (критерий 11).
- Проверка, что порт Analytics остался `5039` и nginx-прокси работает без изменений (критерий 12).
- Дополнительно: убедиться, что MongoDB не изменяется (в `receipt_requests` после suggest/save нет записей категорий).

**Затрагиваемые сервисы/файлы:** окружение (MongoDB, PostgreSQL, Analytics), конфигурация `appsettings*.json`/env.

**Зависимости:** задачи 6, 7, 8.

**Приоритет:** P1
**Оценка времени:** 1 день

## План выполнения

1. Задачи 1–2 (нормализация/модель кэша + миграция) — параллельно.
2. Задачи 3–4 (репозиторий кэша + ИИ-клиент).
3. Задача 5 (сервис категоризации, алгоритм приоритетов).
4. Задача 6 (эндпоинты + DTO-доработка `ReceiptItemDto.Id` + DI).
5. Задача 7 (юнит-тесты).
6. Задача 8 (webUI в `Analytics/frontend` — категоризация чека + список всех позиций с фильтрацией).
7. Задача 9 (ручное тестирование, критерии приёмки, включая критерии 10–12).

## Критический путь

1. Задачи 1–5 (модель кэша + ИИ-клиент + сервис) — основа.
2. Задача 6 (эндпоинты) + задача 7 (тесты).
3. Задача 8 (webUI) — зависит от стабильных контрактов.
4. Задача 9 (приёмка).

## Критерии приёмки (адаптированы под направление «Analytics + PostgreSQL»)

Полный список — раздел 6 задачи `docs/tasks/auto-commodity-categorization.md` (**12 критериев**, включая UC-6/FR-5 и решения заказчика). Ключевые для новой конфигурации:
- UC-1 end-to-end: webUI (`Analytics/frontend`) → nginx → Analytics HTTP API → ответ в webUI (критерий 1).
- Совпадение названия → категория из кэша (`commodity_category_assignments`), ИИ не вызывается; кэш сквозной для всех пользователей (критерии 2, 11).
- Новые названия → категория от ИИ в формате JSON с **именем** категории (`{"category":"Food"}`) из `CommodityCategory` (критерий 3).
- Ничего не сохраняется без «Сохранить»: после `suggest` ни `commodities.category_id`, ни `commodity_category_assignments`, ни MongoDB не изменяются (критерий 4).
- Ручная правка сохраняется как выбор пользователя в `commodities` (критерий 5).
- Недоступность ИИ → частичный результат с пометкой «не определено» (критерий 6).
- Адрес и таймаут ИИ — через конфигурацию (`AI:BaseUrl`/`AI:Timeout` или `AI__*` env), работают без пересборки (критерий 7).
- UC-1…UC-6 покрыты автотестами Analytics (`dotnet test`) и проверены вручную через webUI (критерий 8).
- MongoDB остаётся read-only: ни один новый код не выполняет запись в `receipt_requests`/другие коллекции (критерий 9).
- Список всех позиций всех чеков с серверными фильтрами «без категории» (`category_id IS NULL`) и «`CommodityCategory.Undefined`» (`category_id = 0`), owner-scoped и с пагинацией (критерий 10).
- Порт Analytics не изменился — `5039`, nginx работает без пересборки/перенастройки (критерий 12).

## Открытые вопросы (требуют подтверждения)

1. **Документ задачи:** раздел 8 «Предположения» задачи `docs/tasks/auto-commodity-categorization.md` актуализирован под направление «Analytics + PostgreSQL» и решения заказчика (обновлён вместе с ADR 009 и планом).
2. **Контракт `ReceiptItemDto`:** для сохранения по `commodityId` в DTO и frontend-тип `ReceiptItem` добавляется `id`. Альтернатива — индекс-основанный контракт, но он хрупок при переупорядочивании позиций (выбран `commodityId`).
3. **Формат ответа ИИ:** **вопрос закрыт решением заказчика** — категория передаётся **именем** значения enum `{"category": "Food"}`; парсинг по имени через `Enum.TryParse`; числовой формат `{"category_id": N}` отклонён.
4. **Перенос исторических категорий** из MongoDB (7 значений backend) в PostgreSQL — **вопрос закрыт решением заказчика: не выполняется и не планируется**. Категоризация стартует с чистой базы PostgreSQL; учитываются только категории в `commodities.category_id`.
5. **Порт Analytics:** **вопрос закрыт решением заказчика** — порт не меняется, остаётся `5039` (nginx → `analytics:5039`); `launchSettings.json`/`Dockerfile EXPOSE` — вне области этой задачи.
