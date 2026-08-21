# 9. Автоматическая категоризация товаров в чеке (Auto Commodity Categorization)

## Статус

Принято (перевалидировано под решения заказчика, раздел 7 задачи; реализация запланирована; декомпозиция — в [docs/plans/auto-commodity-categorization.md](../plans/auto-commodity-categorization.md))

## Контекст

Пользователь ведёт категории товаров вручную или не ведёт вовсе, поэтому аналитика по категориям расходов неполная. Требуется дать пользователю возможность одним действием получить предложения категорий для всех позиций чека с обязательным подтверждением перед сохранением.

**Направление решения (утверждено заказчиком и коренным образом отличается от первоначальной редакции этого ADR):**

- Новые эндпоинты категоризации добавляются **в проект Analytics (.NET 8)**, а не в Go-backend. Вся дальнейшая работа ведётся в Analytics-проекте.
- Analytics обращается к MongoDB **только для синхронизации и только в режиме «read only»** (только чтение). Никакой записи в MongoDB.
- Вся дальнейшая работа происходит с **существующими данными в PostgreSQL**; категории позиций чека проставляются именно в реляционной БД (PostgreSQL).
- webUI располагается в **`Analytics/frontend`**, и именно его нужно дорабатывать (а не какой-то отдельный `webapp/`).
- Перечень категорий позиций чека определён в **`Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs`** — именно этот список категорий используется для категоризации (ИИ должен присваивать одну из этих категорий).

Ключевые факты о текущем коде, на которых строится решение:

- **Analytics — четырёхпроектное .NET-решение**: `ReceiptCollector.Analytics.Api`, `.Application`, `.Domain`, `.Infrastructure`, а также `ReceiptCollector.Analytics.Migrations` (SQL-скрипты, `MigrationRunner`) и тесты `Analytics/tests/ReceiptCollector.Analytics.Api.Tests`. Слоистость защищается тестами архитектуры `Architecture/ProjectDependencyTests.cs` (Domain не зависит ни от чего; Application — только от Domain; Infrastructure — от Application и Domain; Api — от всех трёх).
- **HTTP API Analytics** — minimal APIs: группы эндпоинтов в `Api/Modules/` (`ReceiptEndpoints` — группа `/api/receipts`, `CommodityEndpoints` — группа `/api/commodities`, `MerchantEndpoints` — `/api/merchants`, `UserAuthEndpoints`), регистрируются в `Program.cs` (`app.MapReceiptEndpoints()`, `app.MapCommodityEndpoints()` и т.д.). Авторизация — через `UserContext.UserId` (AsyncLocal, устанавливается `UserAuthCookieMiddleware`); неавторизованный пользователь получает `401 Unauthorized`, проверки прав — в каждом методе (паттерн ADR 008).
- **Owner-scoping в PostgreSQL**: `ReceiptDbContext` имеет глобальный query filter `r.UserId == CurrentUserId`; read-сервисы (`IReceiptReadService.GetByIdAsync(userId, receiptId)`) фильтруют по `userId`. Чек другого пользователя не доступен → `404`.
- **Синхронизация MongoDB → PostgreSQL**: `MongoReceiptBatchLoader` (read-only: только `Find().Skip().Limit()`) → `MongoReceiptMapper.Map/MapItem` → `ReceiptRepository.AddAsync`. В `MongoReceiptDocumentDto.ReceiptItemDto` есть поле `Categories` (7 значений backend), но при маппинге оно **игнорируется** (в `MapItem` передаётся `null`); категории в PostgreSQL проставляются отдельно (ADR 005, администратор вручную). MongoDB используется исключительно как источник сырых данных, записей в неё Analytics не делает.
- **PostgreSQL-схема для категорий уже готова**: таблица `commodities` содержит колонки `category_id integer` и `category_name varchar(128)` (миграция `Migrations/Scripts/20241019160000_initial_create.sql`). Доменная сущность `Commodity` (`Domain/Modules/Commodities/Commodity.cs`) имеет `Category? Category` и метод `AssignCategory(Category)`; `CommodityEntity` (`Persistence/Postgres/ReceiptEntity.cs`) — `CategoryId`/`CategoryName`; `ICommodityRepository.UpdateCategoryAsync(commodityId, CommodityCategory)` уже реализован. Существующий эндпоинт ручной категоризации товара — `PUT /api/commodities/{id:guid}/category` (`CommodityEndpoints`).
- **Справочник категорий**: `CommodityCategory` — enum, **43 значения (0–17, 18–41, `Other = 255`)** (`Undefined = 0`, `Food = 1`, `Clothing = 2`, `Electronics = 3`, `CosmeticsAndHygiene = 4`, `Pharmacy = 5`, `SportingGoods = 6`, `ChildrenGoods = 7`, `StationeryAndBooks = 8`, `PetSupplies = 9`, `HomeGoods = 10`, `ConstructionAndRepair = 11`, `AutomotiveGoods = 12`, `Flowers = 13`, `Fuel = 14`, `Alcohol = 15`, `Tools = 16`, `Footwear = 17`, `Beverages = 18`, `Groceries = 19`, `Meat = 20`, `Poultry = 21`, `FishAndSeafood = 22`, `Dairy = 23`, `Eggs = 24`, `Vegetables = 25`, `Fruits = 26`, `Bakery = 27`, `Confectionery = 28`, `ReadyMeals = 29`, `FastFood = 30`, `TollRoads = 31`, `PublicTransport = 32`, `RailwayTickets = 33`, `AirTickets = 34`, `Taxi = 35`, `Carsharing = 36`, `Parking = 37`, `Tobacco = 38`, `Telecommunication = 39`, `Utilities = 40`, `Entertainment = 41`, `Other = 255`; состав расширен ADR 010 — [Расширение справочника категорий товаров](010-commodity-categories-expansion.md)) + статический `CommodityCategoryHelper` (русские `DisplayNames`, `GetDisplayName()`, `GetAll()`). Список отдаётся эндпоинтом `GET /api/commodities/categories` (`CategoryDto[]`). Это единственный источник истины для категоризации в данной задаче; 7 значений backend (`purchase.Category`) в сценарии не используются.
- **webUI**: `Analytics/frontend/` — React 19 + Vite + TypeScript. Страницы в `src/components/`: `ReceiptsPage` (список чеков, детали — `ReceiptDetails`), `CommoditiesPage`/`CommodityTable` (ручная категоризация товаров, паттерн select), `MerchantsPage`/`MerchantTable`. API-клиенты — `src/api/` (`receipts.ts`, `commodities.ts`, `merchants.ts`) с `credentials: 'include'`. `ReceiptDetails.tsx` показывает позиции чека без колонки категории. Отдельного каталога `webapp/` в репозитории нет.
- **Nginx**: `nginx.prod.conf` и `nginx.dev.conf` проксируют **весь** `/api` на Analytics (`analytics:5039` / `host.docker.internal:5039`), а `/` — на frontend. Новые эндпоинты категоризации автоматически попадают под существующий прокси — **изменения nginx не требуются**.
- **Backend (Go) и Telegram-бот** в сценарии не участвуют: категоризация выполняется в Analytics, gRPC-контракты и команды бота не меняются, backend не изменяется.

### Бизнес-требования

1. **UC-1 (инициация)** — пользователь из webUI запрашивает категоризацию чека; система применяет алгоритм приоритетов «существующая категория → кэш → ИИ» и возвращает предложения, **ничего не сохраняя** (FR-3.2, FR-4.1).
2. **UC-2/UC-3 (подтверждение/правка)** — webUI предзаполняет категории, пользователь может изменить их вручную и сохраняет по явному действию (FR-3.3–FR-3.5).
3. **UC-4 (частичный успех)** — сбой ИИ по позиции не ломает запрос: позиция помечается «не определена», остальные обрабатываются (FR-2.4, NFR-2.2).
4. **UC-5 (повторное использование)** — совпадение названия с ранее категоризованной позицией не вызывает ИИ (FR-1.1–FR-1.4, NFR-3.1).
5. **NFR-1 (конфигурируемость)** — адрес/таймаут ИИ задаются конфигурацией (appsettings / env), без пересборки.
6. **NFR-2 (производительность)** — категоризация не блокирует другие операции с чеками; ограничение параллельных вызовов ИИ.
7. **UC-6/FR-5 (список всех позиций с серверной фильтрацией)** — webUI показывает списочное представление **всех позиций всех чеков** пользователя; фильтры «позиции без категории» и «позиции с `CommodityCategory.Undefined`» применяются **на сервере**; выдача owner-scoped и с пагинацией (решение заказчика, см. ключевое решение I).

## Рассмотренные варианты

### Ключевое решение A: Размещение логики категоризации

#### Вариант A1: Analytics (.NET) — **выбран**

**Описание:** Логика (нормализация, поиск по кэшу, вызов ИИ, rate limiting, HTTP-эндпоинты) размещается в Analytics-проекте. Позиции и категории живут в PostgreSQL (`commodities.category_id`), куда они уже мигрируются из MongoDB на этапе синхронизации. ИИ-клиент — в Infrastructure-слое, эндпоинты — в Api.

**Плюсы:**
- Соответствует утверждённому направлению решения (новые эндпоинты — в Analytics, категории — в PostgreSQL).
- Данные позиций с категориями (`Commodity.Category`, таблица `commodities`) уже находятся в PostgreSQL — сохраняем в «родную» БД без кросс-сервисных вызовов.
- У Analytics есть развитый webUI (`Analytics/frontend`), справочник `CommodityCategory` с хелпером, паттерны эндпоинтов, репозиториев, read-сервисов и тестов.
- Owner-scoping уже реализован на уровне `ReceiptDbContext` (query filter) и `IReceiptReadService` — защита доступа к чеку не требует нового кода.
- MongoDB остаётся строго read-only (только синхронизация) — граница «MongoDB → PostgreSQL» не нарушается.

**Минусы:**
- Analytics получает новую зону ответственности — интеграцию с внешним ИИ (принято; в Infrastructure-слое, изолировано за интерфейсом).

#### Вариант A2: Backend (Go)

**Описание:** Логика категоризации и новые эндпоинты размещаются в Go-backend, категории сохраняются в MongoDB (`purchase.Categories`).

**Минусы:**
- Противоречит утверждённому направлению решения (эндпоинты — в Analytics, категории — в PostgreSQL).
- Сохранение в MongoDB ломает принятую границу «MongoDB → PostgreSQL (только чтение источника)» и порождает двустороннюю зависимость данных между сервисами.
- Справочник `CommodityCategory` (43 значения) живёт в Analytics; backend пришлось бы дублировать логику и справочник.
- webUI — `Analytics/frontend`; для вызова backend пришлось бы менять nginx-маршрутизацию (сейчас весь `/api` идёт на Analytics).
- Отвергнут по явному указанию заказчика.

#### Вариант A3: Отдельный микросервис категоризации

**Описание:** Выделенный сервис, читающий MongoDB и вызывающий ИИ.

**Плюсы:**
- Изоляция нагрузки ИИ.

**Минусы:**
- Овер-инжиниринг для задачи (YAGNI): ещё один сервис, деплой, TLS, сеть, наблюдаемость.
- Противоречит требованию «вся работа — в Analytics-проекте».

### Ключевое решение B: Справочник категорий для ИИ

#### Вариант B1: `CommodityCategory` (43 значения, без `Undefined` — 42 категории в промте) — **выбран**

**Описание:** ИИ возвращает категорию строго из перечисления `CommodityCategory` (`Food, Clothing, Electronics, CosmeticsAndHygiene, Pharmacy, SportingGoods, ChildrenGoods, StationeryAndBooks, PetSupplies, HomeGoods, ConstructionAndRepair, AutomotiveGoods, Flowers, Fuel, Alcohol, Tools, Footwear, Beverages, Groceries, Meat, Poultry, FishAndSeafood, Dairy, Eggs, Vegetables, Fruits, Bakery, Confectionery, ReadyMeals, FastFood, TollRoads, PublicTransport, RailwayTickets, AirTickets, Taxi, Carsharing, Parking, Tobacco, Telecommunication, Utilities, Entertainment, Other` — расширен по ADR 010), в формате JSON по **имени** значения enum — `{"category": "Food"}` (числовой формат `{"category_id": N}` отклонён решением заказчика). Промт генерируется из `CommodityCategoryHelper.GetAll()` (исключая `Undefined = 0`); валидация ответа — `Enum.TryParse` по имени значения + `Enum.IsDefined` по тому же перечислению. Результат сохраняется в `commodities.category_id`/`category_name` через `CommodityCategoryHelper.GetDisplayName`. **Каркас решения не меняется при расширении справочника (ADR 010):** JSON `{"category": "Name"}`, валидация `Enum.TryParse`/`Enum.IsDefined`, генерация промта из `GetAll()` без `Undefined` и приоритеты «existing → cache → ai» остаются прежними.

**Плюсы:**
- Сохранение в PostgreSQL без маппинга и потерь.
- Единый источник истины — существующий enum (`CommodityCategory.cs`), заданный требованием заказчика.
- Пользовательский опыт непротиворечив: предложенное = сохранённое; UI работает с `GET /api/commodities/categories`.
- Достаточно точная гранулярность для аналитики по категориям расходов.

**Минусы:**
- ИИ-ответ на 42 категории требует few-shot примеров и эвристик в промте для устойчивости качества (компенсируется строгой валидацией, `source = undefined` при невалидном ответе и ручной коррекцией администратора).

#### Вариант B2: 7 значений backend (`purchase.Category`)

**Описание:** ИИ возвращает категорию из `food, alcohol, clothes, shoes, medicine, home_appliance, entertainment`.

**Минусы:**
- Эти значения не существуют в Analytics/PostgreSQL; для сохранения в `commodities.category_id` потребовался бы неоднозначный маппинг 7→19 (теряется точность, `entertainment` отсутствует вообще).
- Противоречит явному указанию заказчика использовать `CommodityCategory`.
- Грубая гранулярность — менее точная аналитика.

#### Вариант B3: Унификация справочников (`MerchantCategory` + `CommodityCategory`) в рамках задачи

**Описание:** Свести `MerchantCategory` (21 значение) и `CommodityCategory` (43 значения) в единый справочник и перекодировать данные.

**Решение заказчика: не требуется.** Вопрос закрыт (раздел 7 задачи): унификация справочников в рамках данной задачи и в планах **не выполняется** и **не планируется**. Справочники остаются независимыми (`MerchantCategory` — магазины, `CommodityCategory` — товары); ИИ работает только с `CommodityCategory`.

**Минусы (если бы выполнялась):**
- Перекодировка данных в PostgreSQL, правки двух модулей и UI — расширение области задачи (scope creep).
- Блокирует быструю поставку базового сценария.

### Ключевое решение C: Хранилище «ранее присвоенных категорий» (кэш для FR-1.1)

#### Вариант C1: Таблица PostgreSQL `commodity_category_assignments`, сквозной масштаб для всех пользователей — **выбран**

**Описание:** Новая таблица в той же БД PostgreSQL (см. п. 2 «Детали решения»), строки: `id`, `normalized_name`, `name`, `category_id`, `category_name`, `updated_at`. **Без `user_id`** — кэш сквозной для всех пользователей (решение заказчика, раздел 7 задачи). Уникальный индекс **только по `normalized_name`**. Запись обновляется при каждом сохранении подтверждённых категорий (FR-4.2).

**Плюсы:**
- Поиск совпадения — один точечный `FirstOrDefault` по уникальному индексу (O(1)).
- Нормализованное имя хранится один раз и гарантированно стабильно (NFR-3.1).
- Не зависит от структуры чеков; работает даже если чек удалён.
- Следует паттерну проекта: репозиторий поверх EF-таблицы (как `CommodityRepository`, `MerchantRepository`).
- **Сквозной масштаб (без `user_id`)** — выше доля попаданий кэша и меньше вызовов ИИ; категория, однажды присвоенная товару, предлагается всем пользователям при совпадении нормализованного названия (UC-5, решение заказчика).
- Не затрагивает MongoDB (остаётся read-only).

**Минусы:**
- Требуется одна новая миграция БД (SQL-скрипт в `ReceiptCollector.Analytics.Migrations`).
- Категория, присвоенная одним пользователем, влияет на предложения другому — осознанное решение заказчика (приватность принесена в жертву полноте кэша и экономии вызовов ИИ).

#### Вариант C2: Поиск по всем товарам пользователя в `commodities`

**Описание:** На каждый запрос сканировать позиции всех чеков пользователя в поисках совпадения имени.

**Плюсы:**
- Нет новой таблицы.

**Минусы:**
- Сканирование всех `commodities` пользователя на каждый запрос (рост с числом позиций).
- Невозможна нормализация «на лету» без изменения данных; требуется индекс по `name` + фильтр по `user_id` (через `receipts`).
- Смешение ответственности репозиториев товаров и кэша.

#### Вариант C3: In-memory кэш в процессе Analytics

**Описание:** Держать историю категоризаций в памяти сервиса.

**Плюсы:**
- Максимальная скорость.

**Минусы:**
- Потеря при рестарте (не согласовано с PostgreSQL — источником истины).
- При нескольких экземплярах — рассинхронизация; не масштабируется.

#### Вариант C4: Глобальный масштаб кэша (без `user_id`) — **выбран (объединён с C1)**

**Описание:** Искать совпадения по всем пользователям, без привязки к `user_id`.

**Причина выбора:** решение заказчика (раздел 7 задачи) — кэш ранее присвоенных категорий **сквозной для всех пользователей**; per-user масштаб **отклонён**. Вопрос приватности закрыт заказчиком: влияние категорий одного пользователя на предложения другому признано приемлемым ради полноты кэша и экономии вызовов ИИ. Вариант реализуется в рамках C1 (таблица без `user_id`, уникальный индекс по `normalized_name`).

### Ключевое решение D: Синхронная vs асинхронная обработка запроса

#### Вариант D1: Синхронный запрос/ответ — **выбран**

**Описание:** `POST /api/receipts/{id}/categories/suggest` выполняет категоризацию и возвращает предложения в теле ответа. Все вызовы ИИ по позициям выполняются конкурентно в рамках запроса, ограниченные семафором (`AI:Concurrency`) и общим таймаутом.

**Плюсы:**
- Минимальная инфраструктура: один эндпоинт, один ответ — соответствует критериям приёмки («система возвращает список», «ответ отображается в webUI»).
- NFR-2.1 соблюдается: ASP.NET Core обрабатывает каждый HTTP-запрос в отдельном Task, категоризация не блокирует другие операции с чеками; ресурсы ограничены семафором.
- Частичный успех (UC-4) естественно выражается в одном ответе (per-item `source`/`error`).

**Минусы:**
- Время ответа зависит от числа позиций и латентности ИИ; при необходимости увеличивается `proxy_read_timeout` nginx для этого маршрута (прокси уже направляет `/api` на Analytics).

#### Вариант D2: Асинхронный job + polling

**Описание:** `POST` создаёт задание и возвращает `202` + `jobId`; фоновый Task обрабатывает позиции; `GET` отдаёт статус и результат.

**Плюсы:**
- Жёстко соответствует NFR-2.1 «в фоне/асинхронно».
- Нет ограничений по времени HTTP-ответа.

**Минусы:**
- Дополнительная инфраструктура: таблица заданий, TTL-очистка, поллинг-эндпоинт, состояние в UI.
- Усложняет UX (прогресс-бары/автообновление) без требования бизнеса.
- Противоречит простоте существующих эндпоинтов Analytics (YAGNI).

**Зафиксирован путь эволюции:** если латентность ИИ или число позиций в чеке вырастут настолько, что синхронный ответ перестанет укладываться в таймауты, перейти на D2, сохранив контракт `POST` и добавив `GET /api/receipts/{id}/categories/jobs/{jobId}`. Контракты предложений и сохранения от этого не зависят.

### Ключевое решение E: Схема HTTP API

#### Вариант E1: Два эндпоинта в Analytics — **выбран**

**Описание:** Оба эндпоинта живут в Analytics в группе `/api/receipts` (новый файл `Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs`, регистрация `app.MapReceiptCategorizationEndpoints()` в `Program.cs`), используют существующий паттерн авторизации (`UserContext.UserId` → `401`) и owner-scoped доступ (`IReceiptReadService.GetByIdAsync(userId, id)` → `404`). Позиции идентифицируются по `commodityId` (GUID из `commodities.id`).

- `POST /api/receipts/{id:guid}/categories/suggest` — запуск категоризации и возврат предложений (запуск + получение предложений одной операцией; ничего не сохраняется).
- `PUT /api/receipts/{id:guid}/categories` — сохранение подтверждённых/изменённых категорий (массовое обновление `commodities.category_id`).

**Плюсы:**
- Минимальный набор контрактов при синхронной модели (D1).
- Пути согласованы с существующей группой `/api/receipts` и констрейнтом `{id:guid}` (как `GET /api/receipts/{id:guid}`).
- Маршрутизация nginx не меняется: весь `/api` уже проксируется на Analytics.

**Минусы:**
- «Получение предложений» не выделено отдельным GET — при переходе на D2 добавится третий эндпоинт.

#### Вариант E2: Три эндпоинта (запуск / получение / сохранение)

**Описание:** Отдельно `POST .../categorize` (запуск), `GET .../suggestions` (результат), `PUT .../categories` (сохранение).

**Плюсы:**
- Дословно повторяет список из первоначальной постановки.

**Минусы:**
- Избыточно при синхронной обработке: запуск и получение — один ответ; третий эндпоинт ничего не добавляет, кроме состояния между вызовами (которого нет).

#### Вариант E3: Эндпоинты в backend (Go)

**Описание:** API категоризации публикует Go-backend, категории пишутся в MongoDB.

**Причина отклонения:** см. решение A2 — противоречит утверждённому направлению; потребовались бы изменения nginx-маршрутизации (весь `/api` идёт на Analytics) и запись в MongoDB.

### Ключевое решение F: ИИ-клиент и конфигурация

#### Вариант F1: Собственный HTTP-клиент в Infrastructure, конфиг через Options pattern — **выбран**

**Описание:** Контракт `IAiClient` — в Application; реализация `OpenAiCompatibleAiClient` — в Infrastructure (`Infrastructure/Modules/Commodities/` или `Infrastructure/AI/`). Запрос — `POST {AI:BaseUrl}` в OpenAI-совместимом формате чата, таймаут из конфигурации, разбор ответа и строгая валидация JSON-схемы, ретрай только на сетевые ошибки. Конфигурация — через стандартный .NET Options pattern (`Infrastructure/Configuration/Options/AiOptions.cs`, секция `AI`), значения из `appsettings*.json` и переменных окружения (`AI__BaseUrl` и т.д.) — без пересборки (NFR-1).

**Плюсы:**
- Полный контроль над промтом, валидацией и ретраями.
- OpenAI-совместимый формат поддерживается локальными LLM-серверами (Ollama, vLLM, llama.cpp, Qwen) — просто поднять и замокать в тестах.
- Соответствует паттерну проекта: опции — в `Infrastructure/Configuration/Options/` (как `MongoReceiptSourceOptions`, `PostgresOptions`).
- Слой Infrastructure уже зависит от Application/Domain — интерфейс из Application тестируется через NSubstitute.

**Минусы:**
- Небольшой объём собственного кода адаптера (принят).

#### Вариант F2: Сторонний SDK для LLM

**Описание:** Использовать готовый клиент (например, `OpenAI` .NET SDK).

**Плюсы:**
- Меньше собственного кода.

**Минусы:**
- Лишняя зависимость ради тонкой обёртки над HTTP; формат может «прибивать» к конкретному вендору; в репозитории нет прецедентов внешних LLM-SDK.

### Ключевое решение G: Роль MongoDB

#### Вариант G1: MongoDB только для чтения при синхронизации — **выбран**

**Описание:** Категоризация не касается MongoDB. Существующий `MongoReceiptBatchLoader` работает строго на чтение (только `Find`), записей в MongoDB не выполняется. Поле `categories` (7 значений backend) в `MongoReceiptDocumentDto.ReceiptItemDto` по-прежнему игнорируется при маппинге — категории позиций живут исключительно в PostgreSQL (`commodities.category_id`). При необходимости (для строгости) в нагрузке чтения можно использовать `readPreference=secondaryPreferred` в connection string — изменение конфигурации, не кода.

**Плюсы:**
- Соответствует утверждённому требованию «read only» и существующей архитектуре (MongoDB — источник сырых данных, PostgreSQL — нормализованные данные).
- Нет риска рассинхронизации и гонок с backend-воркерами, которые пишут в MongoDB.

**Минусы:**
- Исторически присвоенные в MongoDB категории (7 значений) в PostgreSQL не переносятся — решение заказчика: миграция не выполняется и не планируется (вопрос закрыт). Категоризация стартует с чистой базы PostgreSQL, учитываются только категории в `commodities.category_id`.

#### Вариант G2: Запись категорий в MongoDB

**Описание:** Сохранять категории в поле `purchase.Categories` в MongoDB.

**Причина отклонения:** прямое нарушение утверждённого требования (MongoDB — только чтение, категории — в PostgreSQL); двусторонняя зависимость данных между сервисами.

### Ключевое решение H: Участие backend (Go), Telegram-бота и nginx

#### H1: Backend (Go) и Telegram-бот не изменяются — **по условию задачи**

Go-backend не участвует: gRPC-контракты, воркеры и HTTP API backend остаются как есть. Telegram-бот в сценарии не участвует.

#### H2: Nginx не изменяется — **выбран**

`nginx.prod.conf`/`nginx.dev.conf` уже проксируют весь `/api` на Analytics (`analytics:5039`), а `/` — на `Analytics/frontend`. Новые эндпоинты `/api/receipts/{id}/categories/*` и расширенный `GET /api/commodities` (список позиций с фильтрацией, решение I1) попадают под существующий location `/api` автоматически. При необходимости (медленный ИИ) — только увеличить `proxy_read_timeout` в секции `/api`.

**Порт Analytics не меняется** — остаётся `5039` (как в nginx-конфигурации). Вопрос закрыт решением заказчика (раздел 7 задачи): данная задача **не затрагивает конфигурацию портов** (`launchSettings.json`/`Dockerfile EXPOSE` — вне области задачи).

### Ключевое решение I: Списочное представление всех позиций всех чеков с серверной фильтрацией (UC-6/FR-5)

#### Вариант I1: Расширение `GET /api/commodities` параметром `categoryFilter` — **выбран**

**Описание:** Существующий эндпоинт `GET /api/commodities` (группа `/api/commodities`, `CommodityEndpoints`) уже возвращает **все позиции всех чеков** текущего пользователя с пагинацией (`limit`/`offset`, заголовок `X-Total-Count`) через `ICommodityReadService.GetAsync(userId, limit, offset)` (owner-scoping по `userId` + query filter `ReceiptDbContext`). Расширяем его необязательным query-параметром `categoryFilter`:

- `categoryFilter=any` (по умолчанию) — без фильтрации (текущее поведение).
- `categoryFilter=uncategorized` — **позиции без категории**: `commodities.category_id IS NULL` (категория никогда не назначалась).
- `categoryFilter=undefined` — **позиции с `CommodityCategory.Undefined`**: `commodities.category_id = (int)CommodityCategory.Undefined = 0` (категория явно сброшена в «Не указана»; может появиться через существующий `PUT /api/commodities/{id}/category` с `CategoryId = 0`).
- Невалидное значение → `400 Bad Request`.

**Как сервер отличает «нет категории» от `Undefined`:** колонка `commodities.category_id` — nullable (`int?` в `CommodityEntity`, `Category?` в домене). «Нет категории» = `category_id IS NULL`; «`Undefined`» = `category_id = 0`. Это два разных предиката фильтрации. Новый код категоризации при сбросе пишет `NULL` (а не `0`), поэтому оба состояния могут сосуществовать в данных.

**Плюсы:**
- Переиспользует существующий контракт, пагинацию, owner-scoping и `ICommodityReadService` — минимальные изменения (KISS/YAGNI).
- Не дублирует логику списков и не создаёт новый read-сервис.
- nginx не меняется (маршрут уже проксируется на Analytics).

**Минусы:**
- Расширение существующего эндпоинта добавляет ветвление фильтра в read-сервис (приемлемо; реализуется одним `Where`-предикатом).

#### Вариант I2: Отдельные эндпоинты под каждый фильтр

**Описание:** Два отдельных маршрута (например, `GET /api/commodities/uncategorized` и `GET /api/commodities/undefined`).

**Минусы:**
- Дублирование логики пагинации и списка; хуже расширяемость (новый фильтр = новый маршрут).
- Расходится с единым паттерном «один список + параметры» в проекте (`GET /api/commodities`, `GET /api/merchants`).

#### Вариант I3: Фильтрация на клиенте (webUI)

**Описание:** Отдавать все позиции и фильтровать в React.

**Причина отклонения:** прямое нарушение требования FR-5.2 — фильтрация **обязательно на сервере**; не масштабируется на больших объёмах (весь список в память браузера).

## Решение

Выбраны: **A1** (логика в Analytics, .NET), **B1** (ИИ работает с `CommodityCategory`, 43 значения, без `Undefined` — 42 категории в промте; формат ответа — **имя enum**: `{"category": "Food"}`), **C1** (таблица `commodity_category_assignments` в PostgreSQL, **сквозной масштаб для всех пользователей — без `user_id`**, уникальный индекс по `normalized_name`; вариант C4 объединён с C1), **D1** (синхронный запрос/ответ), **E1** (HTTP-эндпоинты в `/api/receipts` + расширение `GET /api/commodities` параметром `categoryFilter`, решение I1), **F1** (собственный HTTP-клиент, Options pattern), **G1** (MongoDB — только чтение при синхронизации), **H1/H2** (backend и бот не меняются; nginx не меняется, порт Analytics — `5039`, без изменений), **I1** (список всех позиций с серверной фильтрацией).

### Обоснование

1. **Согласованность с утверждённым направлением** — новые эндпоинты в Analytics, категории в PostgreSQL, MongoDB read-only, webUI в `Analytics/frontend`, справочник из `CommodityCategory.cs`.
2. **Следование существующим паттернам Analytics** — группы эндпоинтов minimal API, `UserContext.UserId` + owner-scoped read-сервис, репозиторий поверх EF (`CommodityRepository`), Options pattern, хелпер `CommodityCategoryHelper`, тесты NSubstitute + `UserContext.SetUserId`.
3. **Минимальность (KISS/YAGNI)** — нет нового сервиса, асинхронных очередей, маппинг-слоёв и SDK; используется существующая схема `commodities.category_id`; единственная новая таблица — кэш ранее присвоенных категорий (без `user_id`); списочное представление — расширение существующего `GET /api/commodities` параметром фильтра, без нового read-сервиса.
4. **Отказоустойчивость (UC-4/NFR-2)** — частичные результаты на уровне позиции; семафор ограничивает нагрузку на ИИ; таймауты не дают зависнуть запросу.
5. **Правки в существующем стиле** — неавторизованный → `401`, отсутствие чека/не владелец → `404` (owner-scoped read-сервис), невалидная категория → `400`, ИИ не сконфигурирован → `503`.

### Детали решения

#### 1. Новые файлы в Analytics (по слоям)

| Слой / файл | Назначение |
|-------------|------------|
| `Domain/Modules/Commodities/CommodityNameNormalizer.cs` | `NormalizeName(string) string`: lowercase + trim + схлопывание пробелов — NFR-3.1 |
| `Domain/Modules/Commodities/CommodityCategoryAssignment.cs` | Доменная сущность записи кэша: `NormalizedName`, `Name`, `CategoryId`, `CategoryName`, `UpdatedAt` (без `UserId` — кэш сквозной) |
| `Domain/Modules/Commodities/ICategoryAssignmentRepository.cs` | Контракт кэша: `GetByNormalizedNameAsync(normalizedName, ct)`, `UpsertAsync(name, normalizedName, category, ct)` — без `userId` |
| `Application/Modules/Commodities/Contracts/IAiClient.cs` | Контракт ИИ: `SuggestAsync(name, categoryList, ct) → (CommodityCategory, error)` |
| `Application/Modules/Commodities/Contracts/ICommodityCategorizationService.cs` | Контракт сервиса: `SuggestForReceiptAsync(userId, receiptId, ct)`, `SaveCategoriesAsync(userId, receiptId, updates, ct)` |
| `Application/Modules/Commodities/Models/CategorizationModels.cs` | DTO: `CategorizedItemDto { CommodityId, Name, Category, CategoryName, Source, Error }`, `SaveCategoryUpdate { CommodityId, Category }` — категория передаётся **именем** enum (`"Food"`) или `null` |
| `Infrastructure/Configuration/Options/AiOptions.cs` | Опции ИИ (секция `AI`) |
| `Infrastructure/AI/OpenAiCompatibleAiClient.cs` | ИИ-клиент (см. п. 4) |
| `Infrastructure/Modules/Commodities/CategoryAssignmentRepository.cs` | Репозиторий кэша поверх EF (см. п. 2) |
| `Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` | Оркестрация алгоритма (см. п. 3) |
| `Api/Modules/Receipts/ReceiptCategorizationEndpoints.cs` | Эндпоинты E1 (см. п. 5), регистрация `app.MapReceiptCategorizationEndpoints()` в `Program.cs` |
| `Api/Modules/Commodities/CommodityEndpoints.cs` | Расширение `GET /api/commodities` параметром `categoryFilter` (решение I1, см. п. 5) |
| `Migrations/Scripts/<timestamp>_add_commodity_category_assignments.sql` | Миграция таблицы кэша (см. п. 2) |

Также затрагиваются: `Application/Modules/Receipts/Models/ReceiptDetailsDto.cs` (добавить `Id` в `ReceiptItemDto` — для идентификации позиций при сохранении), `Application/Modules/Commodities/Contracts/ICommodityReadService.cs` и `Infrastructure/Modules/Commodities/CommodityReadService.cs` (параметр фильтра `categoryFilter` в `GetAsync`/`GetTotalCountAsync`), `Infrastructure/Configuration/DependencyInjectionExtensions.cs` (DI-регистрации).

#### 2. Таблица `commodity_category_assignments` (кэш ранее присвоенных категорий)

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

- **Без `user_id`** — кэш сквозной для всех пользователей (решение заказчика); уникальный индекс **только по `normalized_name`** (одна запись на нормализованное имя; последняя подтверждённая категория выигрывает).
- Методы репозитория: `GetByNormalizedNameAsync(normalizedName)` — `FirstOrDefault` по `normalized_name`; `UpsertAsync(name, normalizedName, category)` — поиск по уникальному индексу + `SaveChangesAsync` (или `ExecuteUpdate` при желании).
- Запись обновляется при каждом сохранении подтверждённых категорий (FR-4.2).

#### 3. Алгоритм категоризации (`CommodityCategorizationService`)

Для каждой позиции чека по порядку:
1. **Существующая категория позиции**: если у `Commodity` уже есть `Category` (`category_id` задан) — предложить её, `source = "existing"`, ИИ не вызывается (это расширение FR-1.1: ранее присвоенные категории позиции приоритетнее всего).
2. **Кэш**: `normalized = NormalizeName(item.Name)`; `GetByNormalizedNameAsync(normalized)` (сквозной кэш, без `userId`) — при попадании предложить сохранённую категорию, `source = "cache"`.
3. **ИИ**: при промахе вызвать клиент (с учётом `AI:Concurrency` и таймаута). Валидный ответ → `source = "ai"`; сбой/невалидный JSON → `source = "undefined"`, `error` заполнен, остальные позиции обрабатываются дальше (UC-4).

Ответ эндпоинта — список предложений по всем позициям (включая позиции без категории). **Ничего не сохраняется.**

#### 4. ИИ-клиент и конфигурация

Конфигурация (Options pattern, секция `AI`; env-переменные через `AI__BaseUrl` и т.д.; хардкод запрещён — NFR-1):

| Опция (appsettings) | env | По умолчанию | Назначение |
|---------------------|-----|--------------|------------|
| `AI:BaseUrl` | `AI__BaseUrl` | пусто (обязательна для работы ИИ) | Адрес/эндпоинт модели (NFR-1.2) |
| `AI:Model` | `AI__Model` | `qwen` | Имя модели (передаётся в теле запроса) |
| `AI:Timeout` | `AI__Timeout` | `00:00:10` | Таймаут одного вызова ИИ (NFR-1.2) |
| `AI:Concurrency` | `AI__Concurrency` | `3` | Макс. одновременных вызовов ИИ на запрос (NFR-2.3) |
| `AI:ApiKey` | `AI__ApiKey` | пусто (опционально) | Заголовок `Authorization: Bearer` |

Промт (фиксированный шаблон; **список категорий генерируется из `CommodityCategoryHelper.GetAll()` без `Undefined`** — 42 категории, состав актуален после ADR 010; пример ниже иллюстративный, а не фиксированный хардкод):
```
Ты — сервис категоризации товаров из чеков. Отнеси товар к одной из категорий:
<СПИСОК ИЗ GetAll() БЕЗ Undefined, например: Прочая еда, Одежда, Электроника, ...>
Ответь строго в формате JSON: {"category": "<имя категории из перечисленного списка>"},
например {"category": "Food"}. Указывай категорию ИМЕНЕМ значения, а не числовым кодом.

Эвристики (обязательны):
- «Кофе в зёрнах / молотый / растворимый / капсулы», «Чай в пакетиках / листовой» → Groceries (Бакалея);
  «Капучино», «Латте», «Американо», «Кофе с собой» → Beverages (Напитки).
- Разграничивай схожие категории: Meat (Мясо) vs Poultry (Птица) — по виду сырья;
  Vegetables (Овощи) vs Fruits (Фрукты); Bakery (Хлеб и выпечка) vs Confectionery (Кондитерские изделия)
  (торты/пирожные из кондитерской — Confectionery, хлеб/батоны/булочки — Bakery);
  ReadyMeals (Готовая еда и кулинария) vs FastFood (Фастфуд) (бургеры/шаурма/пицца навынос — FastFood,
  салаты и готовые блюда магазина/доставки — ReadyMeals); Groceries (Бакалея) vs Food (Прочая еда)
  (бакалея — упакованные продукты для приготовления, Food — прочие продукты без своей подкатегории).

Примеры (few-shot, несколько пар «название товара → категория»):
- "Кофе в зёрнах 250г" → Groceries
- "Капучино 0.3" → Beverages
- "Филе куриное" → Poultry
- "Стейк говяжий" → Meat
- "Хлеб Бородинский" → Bakery
- "Шоколад Alpen Gold" → Confectionery
- "Шаурма с курицей" → FastFood
- "Салат Оливье кулинария" → ReadyMeals
```

Запрос — `POST {AI:BaseUrl}` (OpenAI-совместимый формат):
```json
{
  "model": "qwen",
  "messages": [
    {"role": "system", "content": "<промт>"},
    {"role": "user", "content": "<название товара>"}
  ],
  "temperature": 0,
  "response_format": {"type": "json_object"}
}
```

Ответ:
```json
{"choices": [{"message": {"content": "{\"category\":\"Food\"}"}}]}
```

Валидация (FR-2.2, FR-2.3):
- Таймаут запроса → `AI:Timeout`, контекст запроса также ограничивает общее время.
- `content` парсится как JSON, обязательное поле `category` — **имя** значения enum `CommodityCategory` (например, `"Food"`). Валидация: `Enum.TryParse<CommodityCategory>(category, ignoreCase: true, out var parsed)` (по имени значения) + `Enum.IsDefined(parsed)` + `parsed != CommodityCategory.Undefined`. Числовой формат `{"category_id": N}` не принимается (отклонён решением заказчика).
- Невалидный JSON, неизвестное имя категории или числовой формат → ошибка позиции (`source = "undefined"`), ответ ИИ не применяется.
- Ретраи: **только** на сетевые ошибки/таймаут — 1 повтор; невалидный JSON не ретраится (детерминированная ошибка модели). Ответ на открытый вопрос 3.

#### 5. HTTP API

Оба эндпоинта регистрируются в группе `/api/receipts` (`ReceiptCategorizationEndpoints`, `Program.cs`); `userId` берётся из `UserContext.UserId` (неавторизован → `401`). Защита данных — owner-scoped `IReceiptReadService.GetByIdAsync(userId, receiptId)`: не владелец / нет чека → `404`.

**`POST /api/receipts/{id:guid}/categories/suggest`** — запуск категоризации + получение предложений.
- Владелец: `IReceiptReadService.GetByIdAsync(userId, id)` → при отсутствии `404 Not Found`; позиций нет → `200` с пустым `items`.
- ИИ не сконфигурирован (`AI:BaseUrl` пуст) → `503 Service Unavailable`.
- Ответ `200`:
```json
{
  "receiptId": "3fa85f64-...",
  "items": [
    {
      "commodityId": "3fa85f64-...",
      "name": "Молоко",
      "category": "Food",
      "categoryName": "Прочая еда",
      "source": "existing",
      "error": null
    },
    {
      "commodityId": "3fa85f64-...",
      "name": "Неизвестный товар",
      "category": null,
      "categoryName": null,
      "source": "undefined",
      "error": "ai timeout"
    }
  ]
}
```
- `category` передаётся **именем** значения enum `CommodityCategory` (`"Food"`), а не числовым кодом (решение заказчика); `categoryName` — русское название для отображения; `null` = категория не предложена.
- `source` ∈ `existing | cache | ai | undefined` (FR-3.6 — UI по нему индицирует «предложено»). Сохранения нет (FR-3.2).

**`PUT /api/receipts/{id:guid}/categories`** — сохранение подтверждённых/изменённых категорий.
- Владелец: `404` при отсутствии чека.
- Тело:
```json
{
  "items": [
    {"commodityId": "3fa85f64-...", "category": "Food"},
    {"commodityId": "3fa85f64-...", "category": null}
  ]
}
```
- Валидация: каждый `commodityId` принадлежит чеку владельца → иначе `400`; каждый `category` — имя значения `CommodityCategory` (`Enum.TryParse` по имени + `Enum.IsDefined`, не `Undefined`; `null` допустим — сброс в `category_id = NULL`) → иначе `400`.
- Действие: для каждой позиции — `ICommodityRepository.UpdateCategoryAsync(commodityId, category)` (существующий метод, обновляет `category_id`/`category_name`); для каждой обновлённой позиции с назначенной категорией — `UpsertAsync(name, normalizedName, category)` в `commodity_category_assignments` (FR-4.2, чтобы FR-1.1 работал далее).
- Ответ `200`: `{"receiptId": "...", "updated": <число обновлённых позиций>}`.

**`GET /api/commodities?limit=50&offset=0&categoryFilter=any|uncategorized|undefined`** — списочное представление **всех позиций всех чеков** пользователя с серверной фильтрацией (решение I1, UC-6/FR-5).
- Owner-scoping: `ICommodityReadService.GetAsync(userId, ...)` (query filter `ReceiptDbContext` + фильтр по `userId`); неавторизован → `401`.
- `categoryFilter` (необязателен, по умолчанию `any`):
  - `any` — все позиции (текущее поведение);
  - `uncategorized` — `commodities.category_id IS NULL` («позиции без категории», FR-5.3);
  - `undefined` — `commodities.category_id = (int)CommodityCategory.Undefined` («позиции с `CommodityCategory.Undefined`», FR-5.3);
  - невалидное значение → `400`.
- Пагинация — существующая (`limit`/`offset`, заголовок `X-Total-Count`); выдаются только позиции чеков текущего пользователя (FR-5.4).
- Ответ — `CommodityItemDto[]` (поля включают `CategoryId`/`CategoryName`; `null` = «без категории»). Позиции из списка категоризируются существующими механизмами (UC-1…UC-5, `PUT /api/commodities/{id}/category`).

#### 6. UX-флоу в webUI (`Analytics/frontend`)

1. На странице чека (`ReceiptDetails.tsx`) кнопка «Категоризировать» → `POST /api/receipts/{id}/categories/suggest`.
2. Отображение списка позиций с колонкой «Категория»: предзаполненный select из `CommodityCategory` (список — `GET /api/commodities/categories`), индикатор «предложено» для `source = ai|cache` (FR-3.3, FR-3.6).
3. Редактирование категорий вручную (FR-3.4), позиции с `source = undefined` помечаются «категория не определена» (UC-4) и сохраняются без изменений, если пользователь не выбрал категорию сам.
4. Кнопка «Сохранить» → `PUT /api/receipts/{id}/categories` с полным списком выборов (FR-3.5).
5. После успешного ответа — обновление данных чека. Telegram-бот не участвует.

6. **Список всех позиций (UC-6/FR-5):** страница/таблица всех позиций всех чеков (на базе `CommoditiesPage`/`CommodityTable`) с фильтром «без категории» / «`CommodityCategory.Undefined`» / «все». Фильтр передаётся серверу через `categoryFilter` (`GET /api/commodities?categoryFilter=...`); пагинация — как в существующей `CommoditiesPage`. Из списка доступны ручная категоризация (`PUT /api/commodities/{id}/category`) и переход на страницу чека для запуска категоризации (UC-1).

## Ответы на открытые вопросы (раздел 7 задачи)

1. **Справочник категорий.** ИИ возвращает категорию из **`CommodityCategory`** (`Analytics/src/.../Domain/Modules/Commodities/CommodityCategory.cs`, **43 значения: 0–17, 18–41, `Other = 255`; в промт без `Undefined` попадают 42 категории**; состав расширен ADR 010). **Унификация `MerchantCategory`/`CommodityCategory` не требуется** — вопрос закрыт решением заказчика (раздел 7 задачи), действий по унификации в рамках этой задачи и в планах нет.
2. **Кэш ранее присвоенных категорий.** Новая таблица PostgreSQL `commodity_category_assignments` с уникальным индексом **по `normalized_name`**, **без `user_id`**; масштаб — **сквозной для всех пользователей** (решение заказчика). Per-user масштаб **отклонён**.
3. **Формат ответа ИИ.** Категория передаётся **именем** значения enum: `{"category": "Food"}`; числовой формат `{"category_id": N}` **отклонён** решением заказчика. Парсинг по имени через `Enum.TryParse` + `Enum.IsDefined`. Промт и JSON-схема зафиксированы (п. 4); fallback — 1 повтор только при сетевых ошибках, при невалидном JSON повтор не выполняется, позиция помечается «не определена».
4. **Место подтверждения.** **webUI = `Analytics/frontend`** (страница деталей чека). UX длинных списков — редактирование всего списка позиций на одной странице с индикацией «предложено» (FR-3.6). Бот в сценарии не участвует.
5. **Какие позиции участвуют.** **Все позиции чека.** Позиции с уже заданными категориями не вызывают ИИ (`source = existing`), но возвращаются в списке, чтобы пользователь мог их изменить.
6. **Права доступа.** Только владелец чека — owner-scoped доступ уже реализован в `IReceiptReadService.GetByIdAsync` и глобальном query filter `ReceiptDbContext` (`r.UserId == CurrentUserId`).
7. **Исторические категории в MongoDB** (поле `categories`, 7 значений backend). **Вопрос закрыт решением заказчика: миграция/маппинг категорий из MongoDB не выполняется и не планируется.** Категоризация стартует с чистой базы PostgreSQL; учитываются только категории, уже проставленные в `commodities.category_id` (как `source = existing`). Отдельной задачи на перенос категорий из MongoDB нет.
8. **Различие «без категории» и `CommodityCategory.Undefined`.** «Без категории» = `commodities.category_id IS NULL`; «`Undefined`» = `category_id = (int)CommodityCategory.Undefined = 0`. Сервер различает их предикатами фильтра `categoryFilter=uncategorized` / `categoryFilter=undefined` (решение I1). Новый код сохранения при сбросе пишет `NULL`, а не `0`.

## Последствия

### Положительные

- Пользователь получает полный цикл «предложить → подтвердить → сохранить» с предзаполнением и ручной правкой (UC-1…UC-5) в существующем webUI Analytics.
- Списочное представление всех позиций всех чеков с серверной фильтрацией (UC-6/FR-5) — быстрый поиск некатегоризированных позиций и их закрытие, аналитика наполняется полнее.
- Ноль изменений схемы `commodities`: категории пишутся в уже существующие колонки `category_id`/`category_name` (ADR 005).
- MongoDB остаётся строго read-only (только синхронизация) — граница данных не нарушается, backend и бот не затрагиваются.
- Кэш-таблица (сквозная для всех пользователей) уменьшает число вызовов ИИ (FR-1.1/UC-5) и делает поведение стабильным (нормализация); доля попаданий выше, чем при per-user кэше.
- Частичный успех и конфигурируемость ИИ (Options pattern / env) соответствуют NFR-1, NFR-2.
- Код выдержан в стиле проекта: группа эндпоинтов minimal API, репозиторий поверх EF, read-сервисы, Options pattern, тесты NSubstitute.

### Отрицательные

- Одна новая миграция БД (`commodity_category_assignments`) и её запуск до эксплуатации категоризации.
- Синхронный `POST .../suggest` может длиться дольше стандартных таймаутов при большом числе позиций → при необходимости увеличивается `proxy_read_timeout` nginx (секция `/api`).
- Исторические категории (7 значений) из MongoDB в PostgreSQL не переносятся — решение заказчика (миграция не выполняется и не планируется); категоризация стартует с чистой базы PostgreSQL.

### Компромиссы

- **Синхронный ответ вместо асинхронного job'а** — простота и соответствие критериям приёмки против жёсткой трактовки NFR-2.1 «в фоне»; эволюция на D2 зафиксирована.
- **Сквозной кэш (без `user_id`) вместо per-user** — полнота кэша и меньше вызовов ИИ против приватности предложений; вопрос закрыт решением заказчика (приватность осознанно принесена в жертву, предложения всегда требуют явного подтверждения).
- **42 категории `CommodityCategory` (без `Undefined`) без унификации с `MerchantCategory`** — детализированный справочник даёт осмысленную аналитику; унификация не требуется (решение заказчика). Цена детализации — необходимость few-shot примеров и эвристик в промте для разграничения схожих категорий (ADR 010, решение E1).
- **MongoDB read-only** — чистая граница данных; исторические категории MongoDB не переносятся (решение заказчика, миграция не планируется).

### Риски

- **Долгий ответ `POST .../suggest`** при большом чеке и медленном ИИ (таймауты клиента/nginx). **Митигация:** `AI:Timeout` и `AI:Concurrency` из конфигурации, общий контекст запроса, при необходимости `proxy_read_timeout` 120s; при росте — переход на D2.
- **Некорректный/нестабильный ответ ИИ.** **Митигация:** строгая валидация JSON и категории (FR-2.3), отсутствие автоматического сохранения (FR-3.2), per-item ошибки (UC-4).
- **Гонка при upsert кэша** (параллельные сохранения одной позиции). **Митигация:** уникальный индекс по `normalized_name`; последняя подтверждённая запись выигрывает.
- **Сквозной кэш: «чужая» категория в предложении** — предложение может отражать категорию, присвоенную другим пользователем. **Митигация:** решение заказчика; предложение всегда предзаполнено и требует явного подтверждения (FR-3.2), пользователь может изменить категорию вручную (FR-3.4).
- **Смешение состояний «без категории» (`category_id IS NULL`) и «`Undefined`» (`category_id = 0`)** при фильтрации/сохранении. **Митигация:** явные предикаты фильтра `categoryFilter=uncategorized` / `categoryFilter=undefined` (решение I1); новый код сброса пишет `NULL`, а не `0`; тесты покрывают оба состояния.
- **`ReceiptItemDto` не содержит `Id` позиции** — доработка DTO обязательна для сохранения по `commodityId` (учтено в плане, задача 5).
- **Рассогласование типов frontend/backend** (`categoryId: string | null` в `types/receipt.ts` против `int?` в DTO) — исправляется в рамках интеграции с webUI (задача 8 плана).

## Ссылки

- [Задача: Автоматическая категоризация товаров в чеке](../tasks/auto-commodity-categorization.md)
- [ADR 005: Справочник категорий товаров — Enum вместо таблицы БД](005-commodity-category-as-enum.md)
- [ADR 008: Управление категориями магазинов](008-merchant-category-management.md) (в т.ч. раздел «Риски» об унификации справочников)
- [ADR 010: Расширение справочника категорий товаров](010-commodity-categories-expansion.md) (новый состав справочника, 43 значения; влияет на количество категорий в промте и требует few-shot/эвристик)
- [План реализации: Автоматическая категоризация товаров](../plans/auto-commodity-categorization.md)
- Фактический код: `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs`, `Commodity.cs`, `ICommodityRepository.cs`; `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs`, `Modules/Receipts/ReceiptEndpoints.cs`, `Modules/Commodities/CommodityEndpoints.cs`, `Modules/Users/UserContext.cs`; `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Receipts/Models/ReceiptDetailsDto.cs`; `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Synchronization/MongoReceiptMapper.cs`, `Synchronization/ReceiptSynchronizationService.cs`, `DataSources/Mongo/MongoReceiptBatchLoader.cs`, `DataSources/Mongo/MongoReceiptDocumentDto.cs`, `Persistence/Postgres/ReceiptEntity.cs`, `Persistence/Postgres/CommodityRepository.cs`, `Configuration/DependencyInjectionExtensions.cs`; `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/20241019160000_initial_create.sql`; `Analytics/frontend/src/components/ReceiptDetails.tsx`, `api/receipts.ts`, `types/receipt.ts`; `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/Architecture/ProjectDependencyTests.cs`; `nginx.prod.conf`, `nginx.dev.conf`
