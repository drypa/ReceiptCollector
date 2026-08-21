# Декомпозиция: Раздел «Товары» (Commodities) в модуле Analytics

> **Источник**: `docs/tasks/commodities-solution.md` (архитектурное решение)
> **Требования**: `docs/tasks/commodities-page.md`
> **Приоритеты**: P0 — критический блок, P1 — важный, P2 — опциональный/улучшение

---

## Сводка

| Слой | Всего задач | P0 | P1 | P2 | Оценка (ч) |
|------|-------------|----|----|-----|------------|
| Backend: Domain | 2 | 2 | 0 | 0 | 1 |
| Backend: Application | 4 | 3 | 1 | 0 | 1.5 |
| Backend: Infrastructure | 4 | 3 | 1 | 0 | 2.5 |
| Backend: API | 3 | 3 | 0 | 0 | 1.5 |
| Frontend: Инфраструктура | 5 | 5 | 0 | 0 | 2.5 |
| Frontend: API и типы | 3 | 3 | 0 | 0 | 1.5 |
| Frontend: Компоненты | 2 | 2 | 0 | 0 | 3 |
| Frontend: Интеграция | 3 | 3 | 0 | 0 | 2 |
| Проверка | 2 | 2 | 0 | 0 | 1 |
| **Итого** | **28** | **26** | **2** | **0** | **16.5** |

---

## 1. Backend: Domain (2 задачи, P0, ~1 ч)

### 1.1 Создать `Domain/Modules/Commodities/CommodityCategory.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs`

**Что сделать**: Создать enum `CommodityCategory` со значениями (аналог `MerchantCategory`) и статический класс `CommodityCategoryHelper` с отображением в русские имена.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Domain.Modules.Commodities`
- Enum со значениями:
  - `Undefined = 0` — «Не указана»
  - `Food = 1` — «Продукты питания»
  - `ClothingAndFootwear = 2` — «Одежда и обувь»
  - `Electronics = 3` — «Электроника»
  - `CosmeticsAndHygiene = 4` — «Косметика и гигиена»
  - `Pharmacy = 5` — «Аптека»
  - `SportingGoods = 6` — «Товары для спорта»
  - `ChildrenGoods = 7` — «Товары для детей»
  - `StationeryAndBooks = 8` — «Канцтовары и книги»
  - `PetSupplies = 9` — «Зоотовары»
  - `HomeGoods = 10` — «Товары для дома»
  - `ConstructionAndRepair = 11` — «Строительство и ремонт»
  - `AutomotiveGoods = 12` — «Автотовары»
  - `Flowers = 13` — «Цветы»
  - `Other = 14` — «Прочее»
- Класс `CommodityCategoryHelper` (статический) с методами:
  - `GetDisplayName(CommodityCategory category) → string`
  - `GetAll() → IReadOnlyCollection<(CommodityCategory Id, string Name)>`
- Внутренний словарь `Dictionary<CommodityCategory, string>` с русскими названиями

**Зависимости**: нет
**Критерий приёмки**: проект компилируется, `CommodityCategory` и `CommodityCategoryHelper` доступны для использования.

---

### 1.2 Создать `Domain/Modules/Commodities/ICommodityRepository.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/ICommodityRepository.cs`

**Что сделать**: Создать интерфейс репозитория для операций с товарами.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Domain.Modules.Commodities`
- Методы:
  - `Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default)`
  - `Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default)`
- Интерфейс использует существующие модели `Commodity` (из `Domain/Modules/Commodities/Commodity.cs`)

**Зависимости**: задача 1.1 (enum `CommodityCategory`)
**Критерий приёмки**: интерфейс объявлен, ссылается на корректные типы, компилируется.

---

## 2. Backend: Application (4 задачи, ~1.5 ч)

### 2.1 Создать `Application/Modules/Commodities/Contracts/ICommodityReadService.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityReadService.cs`

**Что сделать**: Создать интерфейс read-сервиса для списка товаров с пагинацией.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts`
- Методы (аналогично `IReceiptReadService`):
  - `Task<IReadOnlyCollection<CommodityItemDto>> GetAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken = default)`
  - `Task<int> GetTotalCountAsync(Guid userId, CancellationToken cancellationToken = default)`

**Зависимости**: задача 2.2 (модель `CommodityItemDto`)
**Приоритет**: P0
**Критерий приёмки**: интерфейс объявлен, корректно ссылается на `CommodityItemDto`.

---

### 2.2 Создать `Application/Modules/Commodities/Contracts/ICommodityWriteService.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Contracts/ICommodityWriteService.cs`

**Что сделать**: Создать интерфейс write-сервиса для обновления категории товара.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts`
- Методы:
  - `Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default)`
- Использует `CommodityCategory` из Domain

**Зависимости**: задача 1.1
**Приоритет**: P0
**Критерий приёмки**: интерфейс объявлен, компилируется.

---

### 2.3 Создать `Application/Modules/Commodities/Models/CommodityItemDto.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CommodityItemDto.cs`

**Что сделать**: Создать record DTO для элемента товара в таблице.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Application.Modules.Commodities.Models`
- Поля:
  - `Guid Id`
  - `string MerchantName`
  - `Guid ReceiptId`
  - `DateTime PurchasedAt`
  - `string Name`
  - `decimal Quantity`
  - `decimal UnitPrice`
  - `decimal TotalPrice`
  - `int? CategoryId`
  - `string? CategoryName`
- sealed record

**Зависимости**: нет (не зависит от DTO других модулей)
**Приоритет**: P0
**Критерий приёмки**: DTO объявлен, компилируется.

---

### 2.4 Создать `Application/Modules/Commodities/Models/CategoryDto.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CategoryDto.cs`

**Что сделать**: Создать record DTO для категории товара (список категорий).

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Application.Modules.Commodities.Models`
- Поля:
  - `int Id`
  - `string Name`
- sealed record

**Зависимости**: нет
**Приоритет**: P1 (нужен только для эндпоинта `GET /api/commodities/categories`)
**Критерий приёмки**: DTO объявлен, компилируется.

---

## 3. Backend: Infrastructure (4 задачи, ~2.5 ч)

### 3.1 Создать `Infrastructure/Modules/Commodities/CommodityReadService.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Commodities/CommodityReadService.cs`

**Что сделать**: Реализовать `ICommodityReadService` через EF Core с `Include` навигационных свойств.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Infrastructure.Modules.Commodities`
- `internal sealed class CommodityReadService : ICommodityReadService`
- Конструктор принимает `ReceiptDbContext dbContext`
- `GetAsync`: `_dbContext.Commodities.AsNoTracking().Include(c => c.Receipt).ThenInclude(r => r.Merchant).Where(c => c.Receipt.UserId == userId).OrderByDescending(c => c.Receipt.PurchasedAt).ThenBy(c => c.Name).Skip(offset).Take(limit).Select(...)`
- `GetTotalCountAsync`: `_dbContext.Commodities.AsNoTracking().Where(c => c.Receipt.UserId == userId).CountAsync()`
- В `Select` маппинг в `CommodityItemDto` (TotalPrice = `c.Quantity * c.UnitPrice`)

**Зависимости**: задачи 2.1, 2.3
**Приоритет**: P0
**Критерий приёмки**: сервис реализован, компилируется, запросы корректно используют навигационные свойства.

---

### 3.2 Создать `Infrastructure/Modules/Commodities/CommodityRepository.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/CommodityRepository.cs`

**Что сделать**: Реализовать `ICommodityRepository` через EF Core с обновлением denormalized `CategoryName`.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres` (т.к. используется `ReceiptDbContext`)
- `internal sealed class CommodityRepository : ICommodityRepository`
- Конструктор принимает `ReceiptDbContext dbContext`
- `GetByIdAsync`: `_dbContext.Commodities.AsNoTracking().FirstOrDefaultAsync(c => c.Id == commodityId)` → маппинг в доменную `Commodity` с `Category`
- `UpdateCategoryAsync`:
  1. Получить `CommodityEntity` из БД
  2. Установить `entity.CategoryId = (int)category; entity.CategoryName = CommodityCategoryHelper.GetDisplayName(category);`
  3. `SaveChangesAsync`
  4. Если entity не найден — `InvalidOperationException`

**Зависимости**: задачи 1.1, 1.2
**Приоритет**: P0
**Критерий приёмки**: репозиторий реализован, обновление сохраняет и `CategoryId` и `CategoryName`.

---

### 3.3 Изменить `DependencyInjectionExtensions.cs` — регистрация сервисов

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs`

**Что сделать**: Добавить регистрацию новых сервисов в метод `AddInfrastructure`.

**Детали**:
- Добавить `using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;`
- Добавить `using ReceiptCollector.Analytics.Domain.Modules.Commodities;`
- Добавить `using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;` (если нужно для read service)
- В методе `AddInfrastructure` после существующих регистраций добавить:
  ```csharp
  services.AddScoped<ICommodityReadService, CommodityReadService>();
  services.AddScoped<ICommodityRepository, CommodityRepository>();
  ```
- **Порядок размещения**: после регистрации `IReceiptReadService` и `IMerchantRepository`.

**Зависимости**: задачи 3.1, 3.2
**Приоритет**: P0
**Критерий приёмки**: приложение запускается, DI не выбрасывает исключений при резолве новых сервисов.

---

### 3.4 (Опционально) Добавить EF конфигурацию навигации `CommodityEntity → Receipt` если отсутствует

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Persistence/Postgres/Configurations/CommodityConfiguration.cs`

**Что сделать**: Проверить, что у `CommodityConfiguration` настроена связь `HasOne(c => c.Receipt).WithMany(r => r.Items).HasForeignKey(c => c.ReceiptId)`.

**Детали**:
- Прочитать текущий `CommodityConfiguration.cs` — если связь задана в `ReceiptConfiguration` через `HasMany(r => r.Items).WithOne(i => i.Receipt)`, то ничего не менять.
- Если связь не настроена — добавить в `CommodityConfiguration`:
  ```csharp
  builder.HasOne(c => c.Receipt)
      .WithMany(r => r.Items)
      .HasForeignKey(c => c.ReceiptId);
  ```

**Зависимости**: задача 3.1 (CommodityReadService требует навигации `Commodity → Receipt`)
**Приоритет**: P1 (проверить, возможно уже сконфигурировано)
**Критерий приёмки**: `dotnet build` успешен, EF Core может выполнить `Include(c => c.Receipt)`.

---

## 4. Backend: API (3 задачи, ~1.5 ч)

### 4.1 Создать `Api/Modules/Commodities/CommodityEndpoints.cs`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Commodities/CommodityEndpoints.cs`

**Что сделать**: Создать модуль эндпоинтов с тремя маршрутами.

**Детали**:
- Пространство имён: `ReceiptCollector.Analytics.Api.Modules.Commodities`
- `public static class CommodityEndpoints`
- Метод расширения `MapCommodityEndpoints(this IEndpointRouteBuilder app)`:
  - Группа `app.MapGroup("/api/commodities")` с `WithTags("Commodities")`
  - `GET ""` → `GetAll`
  - `PUT "/{id:guid}/category"` → `UpdateCategory`
  - `GET "/categories"` → `ListCategories`

- `GetAll`:
  - Параметры: `HttpContext`, `[FromServices] ICommodityReadService`, `[FromQuery] int limit = 10`, `[FromQuery] int offset = 0`, `CancellationToken`
  - Проверка `UserContext.UserId`, валидация limit/offset
  - Вызов `service.GetAsync()` + `service.GetTotalCountAsync()`
  - Возврат `Results.Ok(commodities)` с заголовком `X-Total-Count`

- `UpdateCategory`:
  - Параметры: `Guid id`, `[FromBody] UpdateCategoryRequest`, `[FromServices] ICommodityRepository`, `[FromServices] IUserRepository`, `CancellationToken`
  - Проверка аутентификации → `Results.Unauthorized()`
  - Проверка isAdmin → `Results.Forbid()`
  - Проверка существования товара → `Results.NotFound()`
  - Проверка `Enum.IsDefined(typeof(CommodityCategory), request.CategoryId)` → `Results.BadRequest()`
  - Вызов `commodityRepository.UpdateCategoryAsync()`
  - Возврат `Results.Ok(new { categoryId, categoryName })`

- `ListCategories`:
  - Без параметров (кроме DI)
  - `CommodityCategoryHelper.GetAll()` → маппинг в `CategoryDto` → `Results.Ok(categories)`

- `UpdateCategoryRequest`: sealed record `UpdateCategoryRequest(int CategoryId)` — разместить в том же файле после класса эндпоинтов

**Зависимости**: задачи 1.1, 2.1, 2.4, 3.2
**Приоритет**: P0
**Критерий приёмки**: эндпоинты зарегистрированы, swagger отображает 3 новых маршрута.

---

### 4.2 Изменить `Program.cs` — подключить эндпоинты

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Api/Program.cs`

**Что сделать**: Добавить `using` и вызов `MapCommodityEndpoints()`.

**Детали**:
- Добавить `using ReceiptCollector.Analytics.Api.Modules.Commodities;`
- После `app.MapReceiptEndpoints();` добавить `app.MapCommodityEndpoints();`

**Зависимости**: задача 4.1
**Приоритет**: P0
**Критерий приёмки**: при запуске API новые эндпоинты доступны.

---

### 4.3 Исправить `ReceiptReadService.ToReceiptDto()` — передавать `item.CategoryId` вместо `null`

**Файл**: `Analytics/src/ReceiptCollector.Analytics.Infrastructure/Modules/Receipts/ReceiptReadService.cs`

**Что сделать**: В методе `ToReceiptDto` (строка 85) заменить `null` на `item.CategoryId` в конструкторе `ReceiptItemDto`.

**Детали**:
- Строка 85 в `ReceiptReadService.cs`:
  ```csharp
  // Было:
  null))
  // Стало:
  item.CategoryId))
  ```
- Убедиться, что `item` — это `CommodityEntity`, у которого есть свойство `CategoryId` (тип `int?`). Оно маппится на `Guid?` в `ReceiptItemDto`.

**Зависимости**: нет
**Приоритет**: P0 (исправляет баг)
**Критерий приёмки**: `ReceiptItemDto.CategoryId` содержит значение категории, а не `null`.

---

## 5. Frontend: Инфраструктура (5 задач, ~2.5 ч)

### 5.1 Установить `react-router-dom`

**Команда**: В директории `Analytics/frontend/` выполнить:
```bash
npm install react-router-dom
```

**Что сделать**: Добавить зависимость `react-router-dom` в `package.json` и установить её.

**Зависимости**: нет
**Приоритет**: P0
**Критерий приёмки**: `package.json` содержит `"react-router-dom"` в dependencies, `npm ls react-router-dom` показывает установленный пакет.

---

### 5.2 Создать `contexts/PageSizeContext.tsx`

**Файл**: `Analytics/frontend/src/contexts/PageSizeContext.tsx`

**Что сделать**: Создать React Context для глобального `pageSize`, общего для разделов «Чеки» и «Товары».

**Детали**:
- `PAGE_SIZE_OPTIONS = [5, 10, 20, 50, 100] as const`
- `DEFAULT_PAGE_SIZE = 10`
- Интерфейс `PageSizeContextValue`:
  - `pageSize: number`
  - `setPageSize: (size: number) => void`
  - `pageSizeOptions: readonly number[]`
- `PageSizeProvider` — React компонент с `useState(DEFAULT_PAGE_SIZE)`
- `usePageSize()` — хук с проверкой, что используется внутри `PageSizeProvider`

**Зависимости**: нет
**Приоритет**: P0
**Критерий приёмки**: компоненты могут импортировать и использовать `usePageSize()`.

---

### 5.3 Создать `components/Sidebar.tsx` + `components/Sidebar.css`

**Файлы**:
- `Analytics/frontend/src/components/Sidebar.tsx`
- `Analytics/frontend/src/components/Sidebar.css`

**Что сделать**: Создать компонент бокового меню с двумя пунктами: «Чеки» и «Товары».

**Детали**:
- Использовать `NavLink` из `react-router-dom`
- Классы CSS:
  - `.sidebar` — 220px, `height: 100vh`, sticky, border-right
  - `.sidebar-nav ul` — стилизация списка
  - `.sidebar-link` — базовый стиль ссылки
  - `.sidebar-link.active` — выделение активного раздела (синий цвет, border-right)
- Текущий активный раздел выделяется через `NavLink` с `className={({ isActive }) => ...}`

**Зависимости**: задача 5.1 (react-router-dom)
**Приоритет**: P0
**Критерий приёмки**: Sidebar отображается, пункты кликабельны, активный раздел выделен.

---

### 5.4 Создать `components/Layout.tsx`

**Файл**: `Analytics/frontend/src/components/Layout.tsx`

**Что сделать**: Создать компонент layout с боковым меню и `<Outlet />` для вложенных маршрутов.

**Детали**:
- Использовать `Outlet` из `react-router-dom`
- Структура: `<div className="app-layout"><Sidebar /><main className="content-area"><Outlet /></main></div>`
- Стили `.app-layout` и `.content-area` добавить в `App.css`

**Зависимости**: задача 5.3 (Sidebar)
**Приоритет**: P0
**Критерий приёмки**: Layout отображает Sidebar и контент дочернего маршрута.

---

### 5.5 Добавить CSS-стили для layout в `App.css`

**Файл**: `Analytics/frontend/src/App.css`

**Что сделать**: Добавить стили для `.app-layout` и `.content-area` (механически — можно совместить с задачей 8.2, но выделена отдельно для атомарности).

**Детали**:
- `.app-layout { display: flex; min-height: 100vh; }`
- `.content-area { flex: 1; padding: 2.5rem 1.5rem 3rem; max-width: 1200px; }`
- Убедиться, что старые стили `.layout` (в `App.css` строка 1) не конфликтуют — можно оставить для обратной совместимости модуля `ReceiptsPage`.

**Зависимости**: задача 5.4 (Layout использует эти классы)
**Приоритет**: P0
**Критерий приёмки**: layout отображается корректно, контент не наезжает на sidebar.

---

## 6. Frontend: API и типы (3 задачи, ~1.5 ч)

### 6.1 Создать `types/commodity.ts`

**Файл**: `Analytics/frontend/src/types/commodity.ts`

**Что сделать**: Создать TypeScript-типы для товаров и категорий.

**Детали**:
- `CommodityItem` — интерфейс с полями:
  - `id: string`, `merchantName: string`, `receiptId: string`, `purchasedAt: string`
  - `name: string`, `quantity: number`, `unitPrice: number`, `totalPrice: number`
  - `categoryId: number | null`, `categoryName: string | null`
- `Category` — интерфейс: `{ id: number; name: string }`
- `PaginatedCommodities` — интерфейс: `{ commodities: CommodityItem[]; totalItems: number; pageSize: number; currentPage: number }`

**Зависимости**: нет
**Приоритет**: P0
**Критерий приёмки**: типы можно импортировать в других файлах, TypeScript компилируется.

---

### 6.2 Создать `api/commodities.ts`

**Файл**: `Analytics/frontend/src/api/commodities.ts`

**Что сделать**: Создать API-слой для работы с товарами: три функции.

**Детали**:
- `fetchCommodities({ limit, offset, signal })`:
  - `GET /api/commodities?limit=&offset=`
  - Чтение `X-Total-Count` из заголовков
  - Возврат `PaginatedCommodities`
- `fetchCategories()`:
  - `GET /api/commodities/categories`
  - Возврат `Promise<Category[]>`
- `updateCommodityCategory(commodityId, categoryId)`:
  - `PUT /api/commodities/{commodityId}/category`
  - Body: `{ categoryId }`
  - `credentials: 'include'`
  - Возврат `Promise<void>`

**Зависимости**: задача 6.1 (типы)
**Приоритет**: P0
**Критерий приёмки**: функции корректно вызывают API, обрабатывают ошибки, возвращают типизированные результаты.

---

### 6.3 Создать `hooks/useCommodities.ts`

**Файл**: `Analytics/frontend/src/hooks/useCommodities.ts`

**Что сделать**: Создать React-хук для загрузки товаров с пагинацией (аналог `useReceipts.ts`).

**Детали**:
- Сигнатура: `useCommodities({ pageSize = 10 })`
- Состояние: `commodities: CommodityItem[]`, `currentPage`, `totalItems`, `isLoading`, `error`
- `loadPage(page)` — вызывает `fetchCommodities` с `abortRef` для отмены предыдущего запроса
- При изменении `pageSize` → сброс на первую страницу
- Возврат: `{ data, isLoading, error, currentPage, totalPages, totalItems, pageSize, goToPage, nextPage, previousPage, refresh }`
- Логика пагинации идентична `useReceipts.ts`

**Зависимости**: задачи 6.1, 6.2
**Приоритет**: P0
**Критерий приёмки**: хук возвращает данные, корректно обрабатывает загрузку, ошибки, пагинацию.

---

## 7. Frontend: Компоненты (2 задачи, ~3 ч)

### 7.1 Создать `components/CommodityTable.tsx`

**Файл**: `Analytics/frontend/src/components/CommodityTable.tsx`

**Что сделать**: Создать компонент таблицы товаров со всеми колонками и управлением категориями для админа.

**Детали**:
- Props: `{ commodities: CommodityItem[]; isAdmin: boolean; onReceiptClick: (receiptId: string) => void; onRefresh: () => void }`
- Колонки: Магазин, Дата покупки (кнопка с class `date-link` → `onReceiptClick`), Название товара, Количество, Цена за ед., Стоимость, Категория (только `isAdmin`)
- Локализация: `Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB' })`, `Intl.DateTimeFormat('ru-RU', ...)`
- Для админов:
  - `useEffect` при `isAdmin === true` загрузить `fetchCategories()`
  - Режим отображения: название категории + кнопка ✎
  - Режим редактирования: `<select>` с категориями, при выборе → `updateCommodityCategory()`, затем `onRefresh()`
  - `editingId: string | null` — какой товар редактируется
  - `savingId: string | null` — для disabled при сохранении
- Empty state: `<div className="empty-state"><p>Товары не найдены.</p></div>`
- Стили `.category-display` и `.edit-category-btn` добавить в `App.css` (можно совместить с задачей 8.2)

**Зависимости**: задачи 6.1, 6.2
**Приоритет**: P0
**Критерий приёмки**: таблица отображает все колонки, админ видит категории и может редактировать, клик по дате вызывает `onReceiptClick`.

---

### 7.2 Создать `components/CommoditiesPage.tsx`

**Файл**: `Analytics/frontend/src/components/CommoditiesPage.tsx`

**Что сделать**: Создать страницу товаров с хуком `useCommodities`, пагинацией, контролами pageSize и обработкой переходов к деталям чека.

**Детали**:
- Использовать `usePageSize()`, `useCommodities({ pageSize })`, `useAdmin()`
- Использовать `useNavigate()` из `react-router-dom` для перехода `/?receiptId=`
- Хедер: заголовок «Товары», количество, селектор pageSize, кнопка «Обновить»
- Три состояния: загрузка, ошибка, контент
- При загрузке: `<div className="state state-loading"><span className="spinner" /> Загружаем товары...</div>`
- При ошибке: сообщение + кнопка «Попробовать снова»
- Основной контент: `<CommodityTable>` + `<Pagination>`
- `handleReceiptClick = (receiptId: string) => navigate(/?receiptId=${receiptId})`

**Зависимости**: задачи 5.2 (PageSizeContext), 6.3 (useCommodities), 7.1 (CommodityTable)
**Приоритет**: P0
**Критерий приёмки**: страница отображается, пагинация работает, переход к чекам работает.

---

## 8. Frontend: Интеграция (3 задачи, ~2 ч)

### 8.1 Изменить `App.tsx` — роутинг, Provider, Layout

**Файл**: `Analytics/frontend/src/App.tsx`

**Что сделать**: Переписать `App.tsx`: обернуть всё приложение в `BrowserRouter` и `PageSizeProvider`, настроить роуты с `Layout`.

**Детали**:
- Заменить `<ReceiptsPage />` на:
```tsx
<BrowserRouter>
  <PageSizeProvider>
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<ReceiptsPage />} />
        <Route path="/commodities" element={<CommoditiesPage />} />
      </Route>
    </Routes>
  </PageSizeProvider>
</BrowserRouter>
```
- Добавить импорты: `BrowserRouter`, `Routes`, `Route` из `react-router-dom`, `PageSizeProvider`, `Layout`, `CommoditiesPage`
- Удалить импорт `ReceiptsPage` (он теперь используется внутри роутов) — **нет, оставить**, т.к. используется в `Route`
- Оставить `useEffect` для `adminService.initialize()`

**Зависимости**: задачи 5.1, 5.2, 5.4, 7.2
**Приоритет**: P0
**Критерий приёмки**: при переходе на `/` видна страница чеков, на `/commodities` — товары.

---

### 8.2 Изменить `ReceiptsPage.tsx` — использовать `PageSizeContext`

**Файл**: `Analytics/frontend/src/components/ReceiptsPage.tsx`

**Что сделать**: Заменить локальное состояние `pageSize` на `usePageSize()`.

**Детали**:
- Удалить строки:
  - `const DEFAULT_PAGE_SIZE = 10;`
  - `const PAGE_SIZE_OPTIONS = [5, 10, 20, 50, 100];`
  - `const [pageSize, setPageSize] = useState<number>(DEFAULT_PAGE_SIZE);`
- Добавить:
  - `import { usePageSize } from '../contexts/PageSizeContext';`
  - `const { pageSize, setPageSize, pageSizeOptions } = usePageSize();`
- Заменить `{PAGE_SIZE_OPTIONS.map(...)}` на `{pageSizeOptions.map(...)}`
- Убедиться, что использование `setPageSize` остаётся корректным (сейчас используется в `onChange` — должно работать)

**Зависимости**: задача 5.2 (PageSizeContext)
**Приоритет**: P0
**Критерий приёмки**: pageSize переключается глобально, изменения сохраняются между страницами.

---

### 8.3 Добавить обработку `receiptId` из query-параметров в `ReceiptsPage.tsx`

**Файл**: `Analytics/frontend/src/components/ReceiptsPage.tsx`

**Что сделать**: Добавить чтение `receiptId` из URL при загрузке страницы чеков и автоматическое открытие деталей чека.

**Детали**:
- Импортировать `useSearchParams` из `react-router-dom`
- Добавить в компонент:
  ```tsx
  const [searchParams, setSearchParams] = useSearchParams();
  const receiptIdFromUrl = searchParams.get('receiptId');
  ```
- `useEffect`:
  ```tsx
  useEffect(() => {
    if (receiptIdFromUrl) {
      handleReceiptClick(receiptIdFromUrl);
      setSearchParams({}, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [receiptIdFromUrl]);
  ```
- Это обеспечит переход `/commodities → /?receiptId=xxx → детали чека`

**Зависимости**: задача 5.1 (react-router-dom)
**Приоритет**: P0
**Критерий приёмки**: при клике на дату в товарах открываются детали чека с корректными данными.

---

## 9. Проверка (2 задачи, ~1 ч)

### 9.1 Сборка бэкенда

**Команды**:
```bash
cd Analytics
dotnet build
dotnet test
```

**Что проверить**:
- Все проекты компилируются (Domain, Application, Infrastructure, Api, Migrations)
- Существующие тесты проходят
- Нет предупреждений о неиспользуемых типах/методах

**Зависимости**: все задачи 1–4
**Приоритет**: P0
**Критерий приёмки**: `dotnet build` успешен, `dotnet test` — все тесты зелёные.

---

### 9.2 Сборка фронтенда

**Команды**:
```bash
cd Analytics/frontend
npm run build
```

**Что проверить**:
- TypeScript компилируется (tsc)
- Vite собирает бандл
- Нет TS-ошибок (особенно типы для `react-router-dom`)

**Зависимости**: все задачи 5–8
**Приоритет**: P0
**Критерий приёмки**: `npm run build` успешен, бандл создаётся в `dist/`.

---

## Граф зависимостей (порядок выполнения)

```
                   1.1 CommodityCategory (Domain)
                       |
                   1.2 ICommodityRepository (Domain)
                      / \
                     /   \
    2.1+2.3 CommodityReadService    2.2 ICommodityWriteService (Application)
      (Application Contracts+DTO)        \
                     |                    \
    3.1 CommodityReadService (Infra)     3.2 CommodityRepository (Infra)
                     |                    /
                     |                   /
    3.3 DependencyInjectionExtensions ---
                     |
    4.1 CommodityEndpoints (API)    4.3 Fix ReceiptReadService
                     |                   |
    4.2 Program.cs -------------------->
                     |
               (backend build pass)
                     
    5.1 react-router-dom
    5.2 PageSizeContext
    5.3 Sidebar ---> 5.4 Layout ---> 5.5 CSS styles
    6.1 types/commodity.ts
    6.2 api/commodities.ts ---> 6.3 useCommodities hook
                                    |
    7.1 CommodityTable <-------------
    7.2 CommoditiesPage (uses: 5.2, 6.3, 7.1)
                                    |
    8.1 App.tsx <------------------- (routes, Providers)
    8.2 ReceiptsPage (PageSizeContext)
    8.3 ReceiptsPage (receiptId from URL)
                     |
               (frontend build pass)
                     |
    9.1 dotnet build + test
    9.2 npm run build
```

**Критический путь** (минимальная последовательность для рабочего прототипа):
1.1 → 1.2 → 2.1+2.3 → 3.1 → 3.3 → 4.1 → 4.2 → (backend готов)
5.1 → 5.2 → 6.1 → 6.2 → 6.3 → 7.1 → 7.2 → 8.1 → (frontend готов)

**Рекомендуемый порядок для команды**:
1. **День 1** (утро): Задачи 1.1, 1.2, 2.1–2.4, 3.1, 3.2 (Domain + Application + Infrastructure)
2. **День 1** (вечер): Задачи 3.3, 4.1, 4.2, 4.3 (DI + API + Fix) → build
3. **День 2** (утро): Задачи 5.1–5.5, 6.1–6.3 (инфраструктура фронтенда)
4. **День 2** (вечер): Задачи 7.1, 7.2, 8.1–8.3 (компоненты + интеграция)
5. **День 3**: Задачи 9.1, 9.2 (проверка сборки), исправление ошибок

---

## Критерии готовности (checklist из требований)

- [ ] `GET /api/commodities` — список с пагинацией + `X-Total-Count`
- [ ] `PUT /api/commodities/{id}/category` — только для админов
- [ ] `GET /api/commodities/categories` — список категорий
- [ ] `ReceiptItemDto.CategoryId` не `null` (исправлено)
- [ ] Sidebar с пунктами «Чеки» и «Товары» на всех страницах
- [ ] Маршрут `/commodities` работает, сохраняется в закладки
- [ ] Таблица товаров: все колонки по требованиям
- [ ] Клик по дате → детали чека
- [ ] Админы видят и редактируют категорию
- [ ] Обычные пользователи не видят колонку категории
- [ ] pageSize сохраняется между разделами
- [ ] Существующий функционал чеков не сломан
