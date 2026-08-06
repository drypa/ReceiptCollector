# 8. Управление категориями магазинов (Merchant Category Management)

## Статус

Принято (реализовано; документ приведён в соответствие с фактическим кодом)

## Контекст

Администратор может назначать категории отдельным **товарам (Commodity)** внутри чека (эндпоинт `PUT /api/commodities/{id}/category`, реализованный в рамках ADR 005), но не мог категоризировать **магазины (Merchant)** целиком. При этом категория магазина уже существовала в модели данных и используется фактически:

- Доменная сущность `Merchant` (`Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/Merchant.cs`) имеет свойство `Category` типа **`MerchantCategory`** и метод `UpdateCategory(MerchantCategory)`.
- Перечисление **`MerchantCategory`** объявлено в `Domain/Modules/Merchants/MerchantCategory.cs` — **21 значение, коды 0–20** (полный список значений и русских названий — в разделе «Детали решения»).
- Колонка `category integer NOT NULL` в таблице `merchants` создана миграцией `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20241019160000_initial_create.sql`; маппинг выполняется через `MerchantConfiguration.HasConversion<int>()` (EF Core, `Infrastructure/Persistence/Postgres/Configurations/MerchantConfiguration.cs`).

> **Важное замечание о расхождении с исходной формулировкой задачи.** При пересмотре данного ADR предполагалось, что категории магазинов должны переиспользовать перечисление `CommodityCategory` (`Domain/Modules/Commodities/CommodityCategory.cs`, используется для товаров), а отдельного типа `MerchantCategory` в коде нет. **Проверка кодовой базы это предположение опровергла**: `MerchantCategory` существует (это отдельный enum со своими 21 значениями и кодами 0–20), `Merchant.Category` имеет именно тип `MerchantCategory`, а вся реализация задачи (репозиторий, эндпоинты, DTO, хелпер, frontend, тесты) построена на нём. Замена на `CommodityCategory` потребовала бы перекодировки данных в БД (коды товарного enum: 0–17 + `Other = 255`), пересмотра состава справочника и изменения уже работающего кода, поэтому **не выполнялась**; унификация справочников `MerchantCategory` и `CommodityCategory` зафиксирована как открытый вопрос (см. «Риски»).

Отсутствовали и были реализованы в рамках этого решения: эндпоинт получения списка **всех** магазинов, эндпоинт обновления категории магазина, хелпер с отображаемыми русскими названиями категорий магазинов, UI-страница для управления категориями магазинов.

### Бизнес-требования

1. **Просмотр всех магазинов** — администратор видит полный список магазинов с их категориями.
2. **Назначение категории магазину** — администратор может изменить категорию магазина или сбросить её (`Undefined`).
3. **Ограничение прав** — просмотр списка и изменение категорий доступны только администраторам (`IsAdmin == true`).
4. **Подготовка к аналитике** — категоризация магазинов в будущем позволит строить аналитику расходов по категориям и автоподставлять категорию магазина для новых товаров из этого магазина.
5. **Совместимость с существующим UI** — паттерн редактирования категории повторяет `CommodityTable.tsx`.
6. **Справочник категорий** — категории магазинов берутся из существующего перечисления `MerchantCategory` (21 значение), отдельный справочник для магазинов не создаётся.

## Рассмотренные варианты

### Ключевое решение A: Размещение логики категорий (хелпер `MerchantCategoryHelper`)

#### Вариант A1: Domain-слой, рядом с enum (`Domain/Modules/Merchants/MerchantCategoryHelper.cs`) — **выбран и реализован**

**Описание:** Статический класс с `DisplayNames`, `GetDisplayName()` и `GetAll()`, размещённый в том же модуле, что и `MerchantCategory`.

**Плюсы:**
- Полное соответствие уже принятому паттерну `CommodityCategoryHelper` (живёт в `Domain/Modules/Commodities/` рядом с `CommodityCategory`)
- Enum и его отображение физически не могут разойтись по слоям — единый модуль, понятная навигация
- Domain не зависит ни от кого (проверяется `ProjectDependencyTests`), хелпер доступен всем слоям без доп. зависимостей
- Лёгкая тестируемость — чистый статический код

**Минусы:**
- Отображаемые русские имена — это «UI-презентация», семантически слегка чужая для Domain (компромисс, уже принятый проектом для товаров)
- Любое изменение названий требует перекомпиляции доменной сборки

#### Вариант A2: Application-слой (`Application/Modules/Merchants/`)

**Описание:** Хелпер в Application-слое, Domain содержит только enum.

**Плюсы:**
- Русские имена ближе к представлению (DTO), Application — «правильный» слой для презентационных маппингов
- Application может переиспользовать его в read-сервисах

**Минусы:**
- Расходится с принятым в проекте паттерном `CommodityCategoryHelper` (создаёт две разные «школы» для одинаковой задачи)
- Application зависит от Domain, так что enum и хелпер разнесены по двум проектам при одинаковой сути

#### Вариант A3: API-слой (рядом с `MerchantEndpoints`)

**Описание:** Хелпер в `Api/Modules/Merchants/`.

**Плюсы:**
- Максимально близко к месту использования (эндпоинт `ListCategories`)

**Минусы:**
- Api — самый «верхний» слой; если хелпер понадобится read-сервисам или миграциям, потребуется дублирование или подъём вниз
- Нарушает принятую в проекте структуру: все domain-справочники живут в Domain

#### Вариант A4 (отклонён по факту кода): переиспользование `CommodityCategoryHelper` для категорий магазинов

**Описание:** Не создавать `MerchantCategoryHelper`, а для отображения категорий магазинов использовать существующий `CommodityCategoryHelper` из `Domain/Modules/Commodities/` (единый хелпер категорий на проект).

**Причина отклонения:** у магазинов и товаров **разные справочники** — `MerchantCategory` (21 значение, коды 0–20: `GroceryStores`, `ClothingAndFootwear`, `Pharmacies`, `PetStores`, `Jewelry`, `Hobbies`, `HouseholdService` и т.д.) не имеет аналогов в `CommodityCategory` (19 значений, коды 0–17 и `Other = 255`). Русские названия тоже расходятся («Продуктовые магазины» vs «Продукты питания», «Аптеки» vs «Аптека», «Косметика» vs «Косметика и гигиена»). Переиспользование привело бы к потере магазинных категорий или к неверной семантике. Отдельный `MerchantCategoryHelper` со своим словарём `DisplayNames` — единственное непротиворечивое решение. Унификация справочников возможна только при принятии отдельного ADR (перекодировка значений в БД).

### Ключевое решение B: Размещение методов репозитория (`GetAllAsync`, `UpdateCategoryAsync`)

#### Вариант B1: Расширение `IMerchantRepository` / `MerchantRepository` (Domain + Infrastructure) — **выбран и реализован**

**Описание:** Методы добавлены в существующий интерфейс и реализацию (`Persistence/Postgres/MerchantRepository.cs`), регистрация в DI не меняется (`AddScoped<IMerchantRepository, MerchantRepository>()` уже есть).

**Плюсы:**
- Естественное место: репозиторий и так оперирует сущностью `Merchant`
- Минимальные изменения — DI не трогаем, `ProjectDependencyTests` не затрагиваются
- `GetAllAsync` следует стилю существующих методов (`AsNoTracking()` + `MapToDomain()`)

**Минусы:**
- `GetAllAsync` без фильтрации/пагинации — при большом числе магазинов потенциально тяжёлый запрос (пагинация выполняется в эндпоинте в памяти)

#### Вариант B2: Отдельный read-сервис в Application (по паттерну `ICommodityReadService`)

**Описание:** Создать `IMerchantReadService` с методом `GetPageAsync(limit, offset, search)` и перенести пагинацию/поиск на уровень БД.

**Плюсы:**
- Чистое разделение read/write-операций (CQRS-подобный подход, уже есть прецедент для commodities)
- Пагинация и поиск выполняются в PostgreSQL (эффективнее на больших объёмах)

**Минусы:**
- Избыточный слой для текущей задачи: паттерн read-сервиса в проекте применяется там, где нужна агрегация данных нескольких сущностей (receipts, commodities), а здесь — простой CRUD одной сущности
- Больше новых файлов/контрактов, чем требуется (противоречит YAGNI)

#### Вариант B3: Использование `DbSet<MerchantEntity>` напрямую в эндпоинте

**Описание:** Эндпоинт получает `ReceiptDbContext` через `[FromServices]` и работает с сущностями напрямую.

**Плюсы:**
- Наименьшее количество кода

**Минусы:**
- Нарушает границы слоёв: Api не должен знать об EF-сущностях
- Инфраструктурная деталь (DbContext) протекает в презентационный слой — плохая тестируемость (NSubstitute не сможет подменить EF-контекст так же легко, как интерфейс репозитория)
- Ломает паттерн, используемый всеми остальными модулями

### Ключевое решение C: Реализация API-группы и проверки прав доступа

#### Вариант C1: Отдельная группа `/api/merchants` с ручной проверкой прав (по паттерну существующих эндпоинтов) — **выбран и реализован**

**Описание:** Новый `Api/Modules/Merchants/MerchantEndpoints.cs` с `MapMerchantEndpoints()`, зарегистрированный в `Program.cs` (`app.MapMerchantEndpoints()`). Каждый метод в начале проверяет `UserContext.UserId` (иначе `Unauthorized`), затем загружает пользователя через `IUserRepository` и проверяет `IsAdmin` (иначе `Forbid`).

**Плюсы:**
- Единый стиль со всем существующим кодом: `CommodityEndpoints`, `ReceiptEndpoints.UpdateMerchantName`, `UserAuthEndpoints` используют ровно этот же паттерн
- Прозрачная логика — права проверяются в том же методе, что и бизнес-логика, нет «магии» фильтров
- Легко тестировать без запуска всего middleware-конвейера (прямые вызовы с `UserContext.SetUserId`)

**Минусы:**
- Дублирование блока проверки прав в каждом методе (несколько строк, но повторяющихся)
- Риск рассинхронизации логики проверки при изменении требований

#### Вариант C2: Вынос проверки прав в общий helper/фильтр (рекомендация из текста задачи)

**Описание:** Создать вспомогательный метод (например, `RequireAdminAsync(HttpContext)` возвращающий `IResult`/результат проверки) или `IEndpointFilter`/middleware, и переиспользовать его во всех группах эндпоинтов.

**Плюсы:**
- Устраняет дублирование (DRY)
- Централизованное место изменения логики прав

**Минусы:**
- Требует рефакторинга **всех** существующих эндпоинтов (`CommodityEndpoints`, `ReceiptEndpoints.UpdateMerchantName`), иначе проект будет иметь два стиля проверки прав — это расширение области задачи (scope creep)
- Через `IEndpointFilter` тестирование становится сложнее (нужен полный pipeline)
- Статический helper с `AsyncLocal` уже существует (`UserContext`) — введение ещё одной абстракции поверх неё избыточно

#### Вариант C3: Расширение существующей группы `/api/receipts`

**Описание:** Добавить эндпоинты в `ReceiptEndpoints` (там уже живёт `PUT /api/receipts/merchants/{id}/name`).

**Плюсы:**
- Не создаёт новый файл

**Минусы:**
- Несогласованная доменная группировка: receipts-группа смешивает две сущности
- Не даёт понятного тега Swagger и префикса для merchants
- Противоречит структурному паттерну проекта, где на модуль — своя группа и свой файл (`CommodityEndpoints`, `UserAuthEndpoints`)

### Прочие решения (варианты рассмотрены кратко)

#### D: DTO для Merchant

- **Вариант D1 (выбран и реализован):** переиспользовать существующий `MerchantDto(Guid Id, string Name, int Category, string? Address, string? Inn)` из `Application/Modules/Receipts/Models/`, не создавая новый — он уже используется в `ReceiptSummaryDto`, а его поля точно покрывают потребности таблицы «Магазины».
- **Вариант D2 (реализован для категорий):** `MerchantCategoryDto(int Id, string Name)` создан в `Application/Modules/Merchants/Models/MerchantCategoryDto.cs` — это новый тип для списка категорий магазинов. Переиспользование товарного DTO-типа категорий (`Category`/`CategoryDto` для commodities) отклонено: категории магазинов имеют собственную кодировку `MerchantCategory` (0–20), отличную от товарной (0–17 + `Other = 255`), а единый тип подтолкнул бы к неверному смешению справочников.

#### E: Frontend-архитектура

- **Вариант E1 (выбран и реализован):** отдельная страница `MerchantsPage` + таблица `MerchantTable` по паттерну `CommoditiesPage`/`CommodityTable`, хук `useMerchants` по паттерну `useCommodities`, API-функции в `api/merchants.ts`, маршрут `/merchants` в `App.tsx`, пункт меню «Магазины» в `Sidebar.tsx` только при `useAdmin().isAdmin`.
- **Вариант E2 (отклонён):** встраивание таблицы в существующую `CommoditiesPage` — смешение разных сущностей в одном разделе ухудшает навигацию и переиспользуемость.

## Решение

Выбраны: **A1** (отдельный хелпер `MerchantCategoryHelper` в Domain), **B1** (расширение репозитория), **C1** (отдельная группа `/api/merchants` с ручной проверкой прав), **D1** (переиспользование `MerchantDto` + `MerchantCategoryDto` для категорий), **E1** (отдельная frontend-страница).

Категории магазинов используют существующее перечисление **`MerchantCategory`** (отдельный справочник, 21 значение, коды 0–20) — **не** `CommodityCategory`, который зарезервирован за категориями товаров (см. «Важное замечание» в Контексте и вариант A4).

### Обоснование

1. **Согласованность с проектом** — все решения повторяют уже принятые в репозитории паттерны: `CommodityCategoryHelper` (ADR 005) → `MerchantCategoryHelper`; `CommodityEndpoints` → `MerchantEndpoints`; `useCommodities`/`CommodityTable` → `useMerchants`/`MerchantTable`.
2. **Соблюдение границ слоёв** — Api не работает с EF-сущностями напрямую, все операции — через `IMerchantRepository`; это защищается `ProjectDependencyTests` (NetArchTest) и сохраняет тестируемость через NSubstitute.
3. **Минимальность изменений (KISS/YAGNI)** — не создаются read-сервисы, фильтры и новые DTO там, где достаточно существующего репозитория и DTO. Отказ от выноса проверки прав в фильтр мотивирован сохранением единого стиля со всем существующим кодом и недопущением scope creep.
4. **Правки в существующем стиле** — неавторизованный пользователь получает `401 Unauthorized`, не-админ — `403 Forbid`, невалидная категория — `400 Bad Request`, отсутствующий магазин — `404 Not Found`. Это соответствует `CommodityEndpoints`.

### Детали решения

**Domain** (`Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/`):
- `IMerchantRepository` расширен: `Task<IReadOnlyCollection<Merchant>> GetAllAsync(CancellationToken)` и `Task UpdateCategoryAsync(Guid merchantId, MerchantCategory category, CancellationToken)`.
- `MerchantCategoryHelper` (файл `MerchantCategoryHelper.cs`) со словарём русских `DisplayNames` (включая `Undefined → "Не указана"`), `GetDisplayName()` (fallback `"Не указана"`) и `GetAll()`.

**Перечисление `MerchantCategory`** (`Domain/Modules/Merchants/MerchantCategory.cs`) — фактический справочник категорий магазинов:

| Код | Значение | Русское название (из `MerchantCategoryHelper`) |
|-----|----------|------------------------------------------------|
| 0 | `Undefined` | Не указана |
| 1 | `GroceryStores` | Продуктовые магазины |
| 2 | `ClothingAndFootwear` | Одежда и обувь |
| 3 | `Electronics` | Электроника |
| 4 | `Cosmetics` | Косметика |
| 5 | `Pharmacies` | Аптеки |
| 6 | `SportingGoods` | Спортивные товары |
| 7 | `ChildrenGoods` | Детские товары |
| 8 | `StationeryAndBooks` | Канцтовары и книги |
| 9 | `PetStores` | Зоомагазины |
| 10 | `HomeGoods` | Товары для дома |
| 11 | `HouseholdGoods` | Хозяйственные товары |
| 12 | `ConstructionAndRepairMaterials` | Строительство и ремонт |
| 13 | `AutomotiveGoods` | Автотовары |
| 14 | `Jewelry` | Ювелирные изделия |
| 15 | `Flowers` | Цветы |
| 16 | `Hobbies` | Хобби |
| 17 | `GardenSupplies` | Садовые товары |
| 18 | `MusicalInstruments` | Музыкальные инструменты |
| 19 | `KitchenAccessories` | Кухонные принадлежности |
| 20 | `HouseholdService` | Бытовые услуги |

**Infrastructure** (`Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantRepository.cs`):
- `GetAllAsync` — `_dbContext.Merchants.AsNoTracking().ToListAsync()` + `MapToDomain()`.
- `UpdateCategoryAsync` — поиск сущности без `AsNoTracking` (нужен трекинг для `SaveChangesAsync`), обновление `entity.Category` (тип `MerchantCategory`, маппится в `integer` через `HasConversion<int>()`); при отсутствии магазина — `InvalidOperationException` (эндпоинт защищается от этого отдельной проверкой `GetByIdAsync` → `404`). DI-регистрация не меняется.

**Application**:
- Новый `MerchantCategoryDto(int Id, string Name)` в `Application/Modules/Merchants/Models/`.
- `MerchantDto` из `Application/Modules/Receipts/Models/` переиспользуется без изменений (поле `Category` имеет тип `int` — код `MerchantCategory`).

**API** (`Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs`):
- Группа `app.MapGroup("/api/merchants")`, тег `Merchants`, регистрация `app.MapMerchantEndpoints()` в `Program.cs`.
- Маршруты (статический `/categories` объявлен **после** динамического `/{merchantId:guid}/category`; это безопасно, т.к. динамический сегмент ограничен констрейнтом `:guid`, и `/categories` не может быть им перехвачен):
  - `GET ""` — `GetAll(limit=50, offset=0, search)` → `MerchantDto[]`, заголовок `X-Total-Count`, сортировка по имени, поиск по подстроке (OrdinalIgnoreCase);
  - `PUT "/{merchantId:guid}/category"` — тело `UpdateMerchantCategoryRequest(int CategoryId)` → `Ok(new { categoryId, categoryName })`;
  - `GET "/categories"` — `ListCategories` → `MerchantCategoryDto[]` из `MerchantCategoryHelper.GetAll()` (21 категория `MerchantCategory` с русскими названиями).
- Валидация: `Enum.IsDefined(typeof(MerchantCategory), categoryId)` (значение `0/Undefined` валидно — сброс категории), `limit > 0`, `offset >= 0`.
- Проверка прав в каждом методе: `UserContext.UserId` → `IUserRepository.GetByIdAsync` → `user.IsAdmin`.

**Frontend** (`Analytics/frontend/src/`):
- `api/merchants.ts`: тип `MerchantDto` (алиас `Merchant` из `types/receipt.ts`), `PaginatedMerchants`; функции `fetchMerchants({limit, offset, search, signal})` (читает `X-Total-Count`), `fetchMerchantCategories()` (возвращает `Category[]` — переиспользуемый интерфейс `Category { id, name }` из `types/commodity.ts`), `updateMerchantCategory(merchantId, categoryId)`.
- `hooks/useMerchants.ts`: хук пагинации с `AbortController`, по образцу `useCommodities`.
- `components/MerchantsPage.tsx`: `useAdmin()` → при `!isAdmin` сообщение «Доступ запрещён»; иначе загрузка списка, `Pagination`, `PageSizeContext`, состояния загрузки/ошибки.
- `components/MerchantTable.tsx`: паттерн редактирования из `CommodityTable` (текущая категория / «Не указана», кнопка «ред.», `<select>` с `autoFocus`, «Сохранение...», `onRefresh` после сохранения). Категории грузит через `fetchMerchantCategories()` и сопоставляет с `merchant.category` (числовой код `MerchantCategory`).
- `App.tsx`: маршрут `/merchants` внутри `<Route element={<Layout />}>`.
- `Sidebar.tsx`: пункт «Магазины» отображается только при `useAdmin().isAdmin`.

**Тесты** (`Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantEndpointsTests.cs`):
- `GetAllMerchants_WithAdminUser_ReturnsMerchantList`, `GetAllMerchants_WithSearch_ReturnsFilteredList`, `GetAllMerchants_WithNonAdminUser_ReturnsForbidden`.
- `UpdateMerchantCategory_WithAdminUser_UpdatesSuccessfully` (проверяет вызов `UpdateCategoryAsync` с `MerchantCategory.GroceryStores`), `UpdateMerchantCategory_WithInvalidCategory_ReturnsBadRequest` (значение 999), `UpdateMerchantCategory_WithNonExistentMerchant_ReturnsNotFound`.
- `GetMerchantCategories_ReturnsAllCategories` — сравнивает результат с `Enum.GetValues<MerchantCategory>()` и `MerchantCategoryHelper.GetDisplayName(...)`.

## Последствия

### Положительные

- Администратор получает полный инструмент управления категориями магазинов, аналогичный существующему для товаров.
- Ноль миграций БД — колонка `category` уже существует и маппится как `int` (`HasConversion<int>()`).
- Код выдержан в едином стиле проекта; тесты эндпоинтов написаны по существующему паттерну (NSubstitute + `UserContext.SetUserId`).
- Бэкенд-часть готова к будущей автокатегоризации товаров по магазину (хелпер и метод `UpdateCategory` переиспользуемы).

### Отрицательные

- Дублирование блока проверки прав администратора в трёх новых методах (принято осознанно в пользу единообразия; кандидат на будущий рефакторинг).
- `GetAllAsync` тянет все магазины в память; пагинация и поиск — в эндпоинте (O(n) по объёму таблицы на каждый запрос).

### Компромиссы

- **Презентационные имена в Domain** (хелпер в Domain-слое) — осознанное следование прецеденту `CommodityCategoryHelper` вместо «чистой» слоистой архитектуры.
- **Ручная проверка прав вместо фильтра** — принят trade-off «единообразие и простота тестирования» против «DRY и централизация прав».
- **Переиспользование `MerchantDto` из модуля Receipts** — лёгкая кросс-модульная ссылка Application→Application принята, т.к. DTO стабилен и уже входит в публичные контракты.
- **Отказ от унификации справочников** — категории магазинов (`MerchantCategory`) и товаров (`CommodityCategory`) остаются двумя разными enum (21 vs 19 значений, разные коды и русские названия) в пользу непротиворечивости и без миграций; унификация — отдельная задача.

### Риски

- **`UpdateCategoryAsync` выбрасывает `InvalidOperationException` при отсутствии магазина** (race condition между проверкой `GetByIdAsync` и вызовом) → необработанное исключение = `500`. **Митигация:** эндпоинт заранее проверяет существование магазина и возвращает `404`; вероятность гонки пренебрежимо мала, при желании можно переписать на возврат `bool`.
- **Рост числа магазинов** может замедлить `GET /api/merchants` из-за фильтрации в памяти. **Митигация:** при появлении реальных объёмов перенести пагинацию/поиск в SQL (вариант B2), контракт эндпоинта (limit/offset/search/X-Total-Count) при этом не изменится.
- **Расхождение русских названий и кодов категорий** между `MerchantCategory` и `CommodityCategory` для похожих значений (например, `Electronics` = 3 в обоих, но `GroceryStores` = 1 ≠ `Food` = 1; «Аптеки» ≠ «Аптека»; у товаров есть `Other = 255` и нет `Jewelry`, `Hobbies` и др.). **Митигация:** при автокатегоризации товаров по магазину потребуется явный маппинг `MerchantCategory → CommodityCategory`, который стоит оформить отдельным ADR (включая решение о перекодировке/унификации справочников).
- **Ошибки загрузки категорий в UI** логируются только в консоль (`console.error` в `MerchantTable`), без сообщения пользователю. **Митигация:** при доработке UX добавить состояние ошибки, как это сделано для списка магазинов на странице.

## Ссылки

- [Задача: Управление категориями магазинов](../tasks/merchant-category-management.md)
- [ADR 005: Справочник категорий товаров — Enum вместо таблицы БД](005-commodity-category-as-enum.md)
- [ADR 006: Глобальное состояние pageSize через React Context](006-commodities-page-global-pagesize.md)
- [ADR 007: Флаг пропуска синхронизации чеков при старте Analytics](007-skip-receipt-synchronization-flag.md)
- Фактический код: `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/MerchantCategory.cs`, `MerchantCategoryHelper.cs`, `Merchant.cs`, `IMerchantRepository.cs`; `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Merchants/Models/MerchantCategoryDto.cs`; `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs`; `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/MerchantRepository.cs`, `Configurations/MerchantConfiguration.cs`; `Analytics/frontend/src/api/merchants.ts`, `components/MerchantTable.tsx`; `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantEndpointsTests.cs`
- [Architecture tests: ProjectDependencyTests](../../Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Architecture/ProjectDependencyTests.cs)
