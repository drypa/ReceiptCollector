# Архитектурное решение: Раздел «Товары» (Commodities) в модуле Analytics

## 1. Обзор изменений

### Что добавляется

| Слой | Новое | Изменения |
|------|-------|-----------|
| **Domain** | `CommodityCategory` (enum), `ICommodityRepository` | — |
| **Application** | `ICommodityReadService`, `ICommodityWriteService`; DTO: `CommodityItemDto`, `CategoryDto` | `IReceiptReadService` — исправить `ReceiptItemDto.CategoryId` (сейчас всегда `null`) |
| **Infrastructure** | `CommodityReadService`, `CommodityRepository` | `DependencyInjectionExtensions` — регистрация новых сервисов |
| **API** | `CommodityEndpoints`: `GET /api/commodities`, `PUT /api/commodities/{id}/category`, `GET /api/commodities/categories` | `Program.cs` — вызов `MapCommodityEndpoints()` |
| **Database** | Нет (схема уже содержит `category_id`, `category_name` в `commodities`) | Возможно новая миграция SQL для заполнения справочника категорий |
| **Frontend** | `CommoditiesPage`, `CommodityTable`, `Sidebar`, `PageSizeContext`; `api/commodities.ts`, `types/commodity.ts`, `hooks/useCommodities.ts` | `App.tsx` — роутинг; `ReceiptsPage` — вынести pageSize в контекст; `main.tsx` — обернуть в `BrowserRouter` и `PageSizeProvider` |
| **Зависимости** | `react-router-dom` | `package.json` |

### Что НЕ меняется (существующий функционал)
- `ReceiptsPage` — поведение и внешний вид сохраняются, изменяется только источник pageSize
- `ReceiptTable`, `Pagination`, `ReceiptDetails` — остаются без изменений
- `UserContext`, `UserAuthCookieMiddleware`, проверка аутентификации — без изменений
- Модель `ReceiptItemDto` — добавляется только корректное значение `CategoryId`, API-контракт расширяется обратно-совместимо

---

## 2. Детальная архитектура бэкенда

### 2.1 Слой Domain

#### Новый файл: `Domain/Modules/Commodities/CommodityCategory.cs`

```csharp
namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

public enum CommodityCategory
{
    Undefined = 0,
    Food = 1,                         // Продукты питания
    ClothingAndFootwear = 2,          // Одежда и обувь
    Electronics = 3,                  // Электроника
    CosmeticsAndHygiene = 4,          // Косметика и гигиена
    Pharmacy = 5,                     // Аптека
    SportingGoods = 6,                // Товары для спорта
    ChildrenGoods = 7,                // Товары для детей
    StationeryAndBooks = 8,           // Канцтовары и книги
    PetSupplies = 9,                  // Зоотовары
    HomeGoods = 10,                   // Товары для дома
    ConstructionAndRepair = 11,       // Строительство и ремонт
    AutomotiveGoods = 12,             // Автотовары
    Flowers = 13,                     // Цветы
    Other = 14                        // Прочее
}

public static class CommodityCategoryHelper
{
    private static readonly Dictionary<CommodityCategory, string> DisplayNames = new()
    {
        { CommodityCategory.Undefined, "Не указана" },
        { CommodityCategory.Food, "Продукты питания" },
        { CommodityCategory.ClothingAndFootwear, "Одежда и обувь" },
        { CommodityCategory.Electronics, "Электроника" },
        { CommodityCategory.CosmeticsAndHygiene, "Косметика и гигиена" },
        { CommodityCategory.Pharmacy, "Аптека" },
        { CommodityCategory.SportingGoods, "Товары для спорта" },
        { CommodityCategory.ChildrenGoods, "Товары для детей" },
        { CommodityCategory.StationeryAndBooks, "Канцтовары и книги" },
        { CommodityCategory.PetSupplies, "Зоотовары" },
        { CommodityCategory.HomeGoods, "Товары для дома" },
        { CommodityCategory.ConstructionAndRepair, "Строительство и ремонт" },
        { CommodityCategory.AutomotiveGoods, "Автотовары" },
        { CommodityCategory.Flowers, "Цветы" },
        { CommodityCategory.Other, "Прочее" },
    };

    public static string GetDisplayName(CommodityCategory category)
        => DisplayNames.GetValueOrDefault(category, "Не указана");

    public static IReadOnlyCollection<(CommodityCategory Id, string Name)> GetAll()
        => DisplayNames.Select(kv => (kv.Key, kv.Value)).ToList();
}
```

**Обоснование**: Аналогично `MerchantCategory`. Enum обеспечивает type-safety, а статический словарь — отображение в удобочитаемые имена. Не требует создания таблицы в БД и миграции схемы.

#### Новый файл: `Domain/Modules/Commodities/ICommodityRepository.cs`

```csharp
public interface ICommodityRepository
{
    Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default);
    Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default);
}
```

**Обоснование**: Единственная операция записи в этой задаче — обновление категории товара. Выделен отдельный репозиторий, а не нагружается `IReceiptRepository`, так как операция специфична.

### 2.2 Слой Application

#### Новый файл: `Application/Modules/Commodities/Contracts/ICommodityReadService.cs`

```csharp
public interface ICommodityReadService
{
    Task<IReadOnlyCollection<CommodityItemDto>> GetAsync(
        Guid userId, int limit, int offset, CancellationToken cancellationToken = default);
    
    Task<int> GetTotalCountAsync(Guid userId, CancellationToken cancellationToken = default);
}
```

#### Новый файл: `Application/Modules/Commodities/Contracts/ICommodityWriteService.cs`

```csharp
public interface ICommodityWriteService
{
    Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default);
}
```

#### Новый файл: `Application/Modules/Commodities/Models/CommodityItemDto.cs`

```csharp
public sealed record CommodityItemDto(
    Guid Id,
    string MerchantName,
    Guid ReceiptId,
    DateTime PurchasedAt,
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    int? CategoryId,
    string? CategoryName);
```

**Обоснование**: Содержит все колонки, требуемые задачей. `CategoryId` и `CategoryName` могут быть `null` — категория не назначена. `ReceiptId` нужен для перехода к деталям чека.

#### Новый файл: `Application/Modules/Commodities/Models/CategoryDto.cs`

```csharp
public sealed record CategoryDto(int Id, string Name);
```

#### Изменение: `ReceiptItemDto` — исправление `CategoryId`

```csharp
// Было:
null

// Стало:
item.CategoryId
```

**Обоснование**: Существующий баг — `categoryId` в `ReceiptItemDto` всегда `null`, хотя в `CommodityEntity` поле `CategoryId` уже заполняется и сохраняется. Исправление обеспечит отображение категорий в деталях чека.

### 2.3 Слой Infrastructure

#### Новый файл: `Infrastructure/Modules/Commodities/CommodityReadService.cs`

```csharp
internal sealed class CommodityReadService : ICommodityReadService
{
    private readonly ReceiptDbContext _dbContext;

    public CommodityReadService(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyCollection<CommodityItemDto>> GetAsync(
        Guid userId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Commodities
            .AsNoTracking()
            .Include(c => c.Receipt)
            .ThenInclude(r => r.Merchant)
            .Where(c => c.Receipt.UserId == userId)
            .OrderByDescending(c => c.Receipt.PurchasedAt)
            .ThenBy(c => c.Name)
            .Skip(offset)
            .Take(limit)
            .Select(c => new CommodityItemDto(
                c.Id,
                c.Receipt.Merchant.Name,
                c.ReceiptId,
                c.Receipt.PurchasedAt,
                c.Name,
                c.Quantity,
                c.UnitPrice,
                c.Quantity * c.UnitPrice,
                c.CategoryId,
                c.CategoryName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> GetTotalCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Commodities
            .AsNoTracking()
            .Where(c => c.Receipt.UserId == userId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
```

**Обоснование**: 
- Использует существующие навигационные свойства EF (Commodity → Receipt → Merchant)
- Фильтрация по `UserId` через навигацию (не нужно `CurrentUserId`)
- Пагинация идентична существующему `ReceiptReadService`
- Используется snake_case именование (настроено в `UseSnakeCaseNamingConvention`)

#### Новый файл: `Infrastructure/Modules/Commodities/CommodityRepository.cs`

```csharp
internal sealed class CommodityRepository : ICommodityRepository
{
    private readonly ReceiptDbContext _dbContext;

    public CommodityRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Commodities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == commodityId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null) return null;

        return new Commodity(
            entity.Id,
            entity.ReceiptId,
            entity.Name,
            entity.Quantity,
            entity.UnitPrice,
            entity.Nds,
            entity.NdsSum,
            entity.CategoryId.HasValue
                ? new Category(entity.CategoryId.Value, entity.CategoryName ?? "")
                : null);
    }

    public async Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Commodities
            .FirstOrDefaultAsync(c => c.Id == commodityId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
            throw new InvalidOperationException($"Commodity with id '{commodityId}' not found.");

        entity.CategoryId = (int)category;
        entity.CategoryName = CommodityCategoryHelper.GetDisplayName(category);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

**Обоснование**: 
- `GetByIdAsync` возвращает доменную модель Commodity с категорией (для проверки существования)
- `UpdateCategoryAsync` напрямую работает с Entity Framework
- Denormalized `CategoryName` обновляется одновременно с `CategoryId` для избежания join'ов

#### Изменение: `DependencyInjectionExtensions.cs`

```csharp
// Добавить:
services.AddScoped<ICommodityReadService, CommodityReadService>();
services.AddScoped<ICommodityRepository, CommodityRepository>();
```

### 2.4 Слой API

#### Новый файл: `Api/Modules/Commodities/CommodityEndpoints.cs`

```csharp
namespace ReceiptCollector.Analytics.Api.Modules.Commodities;

public static class CommodityEndpoints
{
    public static IEndpointRouteBuilder MapCommodityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commodities");
        group.WithTags("Commodities");

        group.MapGet("", GetAll);                                    // GET /api/commodities?limit=10&offset=0
        group.MapPut("/{id:guid}/category", UpdateCategory);         // PUT /api/commodities/{id}/category
        group.MapGet("/categories", ListCategories);                 // GET /api/commodities/categories

        return app;
    }

    // GET /api/commodities
    private static async Task<IResult> GetAll(
        HttpContext httpContext,
        [FromServices] ICommodityReadService service,
        [FromQuery] int limit = 10,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
            return Results.BadRequest("User is not authenticated.");

        if (limit <= 0) return Results.BadRequest("limit must be greater than zero.");
        if (offset < 0) return Results.BadRequest("offset cannot be negative.");

        var commodities = await service.GetAsync(userId.Value, limit, offset, cancellationToken);
        var totalCount = await service.GetTotalCountAsync(userId.Value, cancellationToken);

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(commodities);
    }

    // PUT /api/commodities/{id}/category
    private static async Task<IResult> UpdateCategory(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        [FromServices] ICommodityRepository commodityRepository,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        // Аутентификация
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
            return Results.Unauthorized();

        // Проверка прав администратора
        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
            return Results.Forbid();

        // Проверка существования товара
        var commodity = await commodityRepository.GetByIdAsync(id, cancellationToken);
        if (commodity is null)
            return Results.NotFound("Commodity not found.");

        // Проверка допустимости категории
        if (!Enum.IsDefined(typeof(CommodityCategory), request.CategoryId))
            return Results.BadRequest("Invalid category.");

        var category = (CommodityCategory)request.CategoryId;
        await commodityRepository.UpdateCategoryAsync(id, category, cancellationToken);

        return Results.Ok(new { categoryId = request.CategoryId, categoryName = CommodityCategoryHelper.GetDisplayName(category) });
    }

    // GET /api/commodities/categories
    private static IResult ListCategories()
    {
        var categories = CommodityCategoryHelper.GetAll()
            .Select(c => new CategoryDto((int)c.Id, c.Name))
            .ToList();
        return Results.Ok(categories);
    }
}

public sealed record UpdateCategoryRequest(int CategoryId);
```

#### Изменение: `Program.cs`

```csharp
// Добавить после MapUserAuthEndpoints():
app.MapCommodityEndpoints();
```

#### Изменение: `ReceiptReadService.ToReceiptDto()` — исправление CategoryId

В методе `ToReceiptDto`, строка создания `ReceiptItemDto`:
```csharp
// Было:
null

// Стало:
item.CategoryId
```

---

## 3. Детальная архитектура фронтенда

### 3.1 Выбор библиотеки роутинга: `react-router-dom` v7

**Обоснование**: 
- Текущее приложение не имеет роутинга — все находится на одной странице
- Для поддержки `/commodities` и сохранения URL в закладки браузера нужен роутер
- `react-router-dom` — стандарт де-факто для React-приложений, стабилен, хорошо документирован
- Версия 7 — актуальна для React 19

### 3.2 Глобальное состояние pageSize: React Context

**Обоснование**: pageSize должен сохраняться при переключении между разделами «Чеки» и «Товары». React Context — простейший механизм для shared state без внешних зависимостей.

### 3.3 Структура новых и изменённых файлов

#### Новый файл: `frontend/src/contexts/PageSizeContext.tsx`

```tsx
import { createContext, useContext, useState, type ReactNode } from 'react';

const PAGE_SIZE_OPTIONS = [5, 10, 20, 50, 100] as const;
const DEFAULT_PAGE_SIZE = 10;

interface PageSizeContextValue {
  pageSize: number;
  setPageSize: (size: number) => void;
  pageSizeOptions: readonly number[];
}

const PageSizeContext = createContext<PageSizeContextValue | undefined>(undefined);

export function PageSizeProvider({ children }: { children: ReactNode }) {
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);

  return (
    <PageSizeContext.Provider value={{ pageSize, setPageSize, pageSizeOptions: PAGE_SIZE_OPTIONS }}>
      {children}
    </PageSizeContext.Provider>
  );
}

export function usePageSize(): PageSizeContextValue {
  const context = useContext(PageSizeContext);
  if (!context) throw new Error('usePageSize must be used within a PageSizeProvider');
  return context;
}
```

**Обоснование**: 
- Единый источник истины для `pageSize` и `PAGE_SIZE_OPTIONS`
- Provider оборачивает всё приложение
- Хук `usePageSize` используется в `ReceiptsPage` и `CommoditiesPage`

#### Изменение: `ReceiptsPage.tsx`

```tsx
// Удалить:
const DEFAULT_PAGE_SIZE = 10;
const PAGE_SIZE_OPTIONS = [5, 10, 20, 50, 100];
const [pageSize, setPageSize] = useState<number>(DEFAULT_PAGE_SIZE);

// Добавить:
import { usePageSize } from '../contexts/PageSizeContext';
const { pageSize, setPageSize, pageSizeOptions } = usePageSize();

// Заменить:
{PAGE_SIZE_OPTIONS.map(...)} на {pageSizeOptions.map(...)}
```

#### Новый файл: `frontend/src/types/commodity.ts`

```ts
export interface CommodityItem {
  id: string;
  merchantName: string;
  receiptId: string;
  purchasedAt: string;
  name: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  categoryId: number | null;
  categoryName: string | null;
}

export interface Category {
  id: number;
  name: string;
}

export interface PaginatedCommodities {
  commodities: CommodityItem[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}
```

#### Новый файл: `frontend/src/api/commodities.ts`

```ts
import type { CommodityItem, Category, PaginatedCommodities } from '../types/commodity';

interface FetchCommoditiesOptions {
  limit: number;
  offset: number;
  signal?: AbortSignal;
}

export async function fetchCommodities({ limit, offset, signal }: FetchCommoditiesOptions): Promise<PaginatedCommodities> {
  const searchParams = new URLSearchParams({
    limit: limit.toString(),
    offset: offset.toString(),
  });

  const response = await fetch(`/api/commodities?${searchParams.toString()}`, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось загрузить список товаров');
  }

  const data = (await response.json()) as CommodityItem[];
  const totalHeader = response.headers.get('X-Total-Count') ?? response.headers.get('X-Total-Items');
  const parsedTotal = totalHeader ? Number.parseInt(totalHeader, 10) : Number.NaN;
  const totalItems = Number.isFinite(parsedTotal) ? parsedTotal : offset + data.length;

  return {
    commodities: data,
    totalItems,
    pageSize: limit,
    currentPage: Math.max(1, Math.floor(offset / limit) + 1),
  };
}

export async function fetchCategories(): Promise<Category[]> {
  const response = await fetch('/api/commodities/categories', {
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error('Не удалось загрузить список категорий');
  }

  return response.json() as Promise<Category[]>;
}

export async function updateCommodityCategory(commodityId: string, categoryId: number): Promise<void> {
  const response = await fetch(`/api/commodities/${commodityId}/category`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ categoryId }),
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось обновить категорию товара');
  }
}
```

#### Новый файл: `frontend/src/hooks/useCommodities.ts`

```ts
// Аналогичен useReceipts.ts, но вызывает fetchCommodities()
// Сигнатура:
export function useCommodities({ pageSize = 10 }: { pageSize?: number } = {}) {
  // ...
  // Возвращает: data, isLoading, error, currentPage, totalPages, totalItems, pageSize, goToPage, nextPage, previousPage, refresh
}
```

**Обоснование**: Хук с тем же интерфейсом, что и `useReceipts`, для единообразия.

#### Новый файл: `frontend/src/components/Sidebar.tsx`

```tsx
import { NavLink } from 'react-router-dom';
import './Sidebar.css';

export function Sidebar() {
  return (
    <aside className="sidebar">
      <nav className="sidebar-nav">
        <ul>
          <li>
            <NavLink to="/" end className={({ isActive }) => isActive ? 'sidebar-link active' : 'sidebar-link'}>
              Чеки
            </NavLink>
          </li>
          <li>
            <NavLink to="/commodities" className={({ isActive }) => isActive ? 'sidebar-link active' : 'sidebar-link'}>
              Товары
            </NavLink>
          </li>
        </ul>
      </nav>
    </aside>
  );
}
```

#### Новый файл: `frontend/src/components/Sidebar.css`

```css
.sidebar {
  width: 220px;
  min-width: 220px;
  height: 100vh;
  background: rgba(255, 255, 255, 0.95);
  border-right: 1px solid rgba(148, 163, 184, 0.2);
  padding: 2rem 0;
  position: sticky;
  top: 0;
}

.sidebar-nav ul {
  list-style: none;
  margin: 0;
  padding: 0;
}

.sidebar-link {
  display: block;
  padding: 0.75rem 1.5rem;
  text-decoration: none;
  color: #475569;
  font-weight: 500;
  font-size: 0.95rem;
  transition: background 0.2s ease, color 0.2s ease;
  border-right: 3px solid transparent;
}

.sidebar-link:hover {
  background: rgba(99, 102, 241, 0.08);
  color: #1e293b;
}

.sidebar-link.active {
  background: rgba(99, 102, 241, 0.12);
  color: var(--primary);
  border-right-color: var(--primary);
  font-weight: 600;
}
```

#### Новый файл: `frontend/src/components/Layout.tsx`

```tsx
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';

export function Layout() {
  return (
    <div className="app-layout">
      <Sidebar />
      <main className="content-area">
        <Outlet />
      </main>
    </div>
  );
}
```

#### Изменение: `App.css` — новые стили для layout

```css
.app-layout {
  display: flex;
  min-height: 100vh;
}

.content-area {
  flex: 1;
  padding: 2.5rem 1.5rem 3rem;
  max-width: 1200px;
}
```

#### Новый файл: `frontend/src/components/CommoditiesPage.tsx`

```tsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useCommodities } from '../hooks/useCommodities';
import { usePageSize } from '../contexts/PageSizeContext';
import { useAdmin } from '../hooks/useAdmin';
import { Pagination } from './Pagination';
import { CommodityTable } from './CommodityTable';

export function CommoditiesPage() {
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();
  const { isAdmin } = useAdmin();
  const navigate = useNavigate();
  
  const {
    data,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalItems,
    refresh,
    goToPage,
    nextPage,
    previousPage,
  } = useCommodities({ pageSize });

  const handleReceiptClick = (receiptId: string) => {
    // Переход к деталям чека — пока используем ту же логику
    // Можно передать через query param или через навигацию
    navigate(`/?receiptId=${receiptId}`);
  };

  return (
    <>
      <header>
        <div>
          <h1>Товары</h1>
          <p>Найдено товаров: <strong>{totalItems}</strong></p>
        </div>
        <div className="controls">
          <div className="page-size-selector">
            <label htmlFor="page-size-select">Строк на странице: </label>
            <select
              id="page-size-select"
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
              disabled={isLoading}
            >
              {pageSizeOptions.map((size) => (
                <option key={size} value={size}>{size}</option>
              ))}
            </select>
          </div>
          <button type="button" onClick={refresh} disabled={isLoading}>Обновить</button>
        </div>
      </header>

      {isLoading && (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем товары...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить товары: {error}</p>
          <button type="button" onClick={refresh}>Попробовать снова</button>
        </div>
      )}

      {!isLoading && !error && (
        <CommodityTable
          commodities={data}
          isAdmin={isAdmin}
          onReceiptClick={handleReceiptClick}
          onRefresh={refresh}
        />
      )}

      {!isLoading && !error && (
        <Pagination
          currentPage={currentPage}
          totalPages={totalPages}
          onPageChange={goToPage}
          onNext={nextPage}
          onPrevious={previousPage}
        />
      )}
    </>
  );
}
```

#### Новый файл: `frontend/src/components/CommodityTable.tsx`

**Ключевые колонки**: Магазин, Дата покупки (кликабельная ссылка), Название товара, Количество, Цена за единицу, Стоимость, Категория (только для админов с выпадающим списком).

```tsx
import { useState, useEffect } from 'react';
import type { CommodityItem, Category } from '../types/commodity';
import { fetchCategories, updateCommodityCategory } from '../api/commodities';

interface CommodityTableProps {
  commodities: CommodityItem[];
  isAdmin: boolean;
  onReceiptClick: (receiptId: string) => void;
  onRefresh: () => void;
}

const currencyFormatter = new Intl.NumberFormat('ru-RU', {
  style: 'currency',
  currency: 'RUB',
  minimumFractionDigits: 2,
});

const dateFormatter = new Intl.DateTimeFormat('ru-RU', {
  year: 'numeric',
  month: 'long',
  day: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

export function CommodityTable({ commodities, isAdmin, onReceiptClick, onRefresh }: CommodityTableProps) {
  const [categories, setCategories] = useState<Category[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);

  useEffect(() => {
    if (isAdmin) {
      fetchCategories()
        .then(setCategories)
        .catch(console.error);
    }
  }, [isAdmin]);

  const handleCategoryChange = async (commodityId: string, categoryId: number) => {
    setSavingId(commodityId);
    try {
      await updateCommodityCategory(commodityId, categoryId);
      setEditingId(null);
      onRefresh();
    } catch (error) {
      console.error('Failed to update category:', error);
    } finally {
      setSavingId(null);
    }
  };

  if (commodities.length === 0) {
    return (
      <div className="empty-state">
        <p>Товары не найдены.</p>
      </div>
    );
  }

  return (
    <div className="table-wrapper">
      <table>
        <thead>
          <tr>
            <th>Магазин</th>
            <th>Дата покупки</th>
            <th>Название товара</th>
            <th>Количество</th>
            <th>Цена за ед.</th>
            <th>Стоимость</th>
            {isAdmin && <th>Категория</th>}
          </tr>
        </thead>
        <tbody>
          {commodities.map((commodity) => (
            <tr key={commodity.id}>
              <td>{commodity.merchantName}</td>
              <td>
                <button
                  type="button"
                  onClick={() => onReceiptClick(commodity.receiptId)}
                  className="date-link"
                >
                  {dateFormatter.format(new Date(commodity.purchasedAt))}
                </button>
              </td>
              <td>{commodity.name}</td>
              <td>{commodity.quantity}</td>
              <td>{currencyFormatter.format(commodity.unitPrice)}</td>
              <td>{currencyFormatter.format(commodity.totalPrice)}</td>
              {isAdmin && (
                <td>
                  {editingId === commodity.id ? (
                    <select
                      value={commodity.categoryId ?? ''}
                      onChange={(e) => {
                        const val = e.target.value;
                        if (val) handleCategoryChange(commodity.id, Number(val));
                      }}
                      disabled={savingId === commodity.id}
                      autoFocus
                      onBlur={() => setEditingId(null)}
                    >
                      <option value="">—</option>
                      {categories.map((cat) => (
                        <option key={cat.id} value={cat.id}>{cat.name}</option>
                      ))}
                    </select>
                  ) : (
                    <div className="category-display">
                      <span>{commodity.categoryName ?? '—'}</span>
                      <button
                        type="button"
                        className="edit-category-btn"
                        onClick={() => setEditingId(commodity.id)}
                        disabled={savingId === commodity.id}
                        title="Изменить категорию"
                      >
                        ✎
                      </button>
                    </div>
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
```

#### Новые стили для CommodityTable: добавить в `App.css`

```css
.category-display {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.edit-category-btn {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--primary);
  font-size: 1rem;
  padding: 0.2rem 0.4rem;
  border-radius: 4px;
  transition: background 0.2s ease;
}

.edit-category-btn:hover {
  background: rgba(99, 102, 241, 0.12);
}
```

#### Изменение: `App.tsx`

```tsx
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { PageSizeProvider } from './contexts/PageSizeContext';
import { Layout } from './components/Layout';
import { ReceiptsPage } from './components/ReceiptsPage';
import { CommoditiesPage } from './components/CommoditiesPage';
import { adminService } from './services/adminService';
import { useEffect } from 'react';

export function App() {
  useEffect(() => {
    adminService.initialize();
  }, []);

  return (
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
  );
}

export default App;
```

#### Изменение: `main.tsx` (остаётся без изменений)

`main.tsx` не требует изменений, так как `BrowserRouter` теперь в `App.tsx`.

#### Изменение: обработка перехода к деталям чека из товаров

В `ReceiptsPage` нужно обрабатывать query-параметр `receiptId` из URL. При клике на дату в `CommoditiesPage`, навигация идёт на `/?receiptId=xxx`. В `ReceiptsPage`:

```tsx
import { useSearchParams } from 'react-router-dom';

// В компоненте:
const [searchParams, setSearchParams] = useSearchParams();
const receiptIdFromUrl = searchParams.get('receiptId');

useEffect(() => {
  if (receiptIdFromUrl) {
    handleReceiptClick(receiptIdFromUrl);
    // Очищаем query-параметр, чтобы детали не открывались при обновлении
    setSearchParams({}, { replace: true });
  }
}, [receiptIdFromUrl]); // eslint-disable-line react-hooks/exhaustive-deps
```

---

## 4. Миграции базы данных

**Никаких изменений схемы не требуется.** Таблица `commodities` уже содержит столбцы:
- `category_id integer` — для хранения ID категории
- `category_name varchar(128)` — для хранения denormalized названия категории

Это было предусмотрено в начальной миграции `20241019160000_initial_create.sql`.

Опционально: SQL-скрипт для предзаполнения справочника категорий (если когда-либо понадобится таблица `commodity_categories`). В текущем решении справочник реализован как enum в коде.

---

## 5. Порядок реализации (шаги)

### Шаг 1: Backend — Domain и Application

1. Создать `Domain/Modules/Commodities/CommodityCategory.cs` (enum + helper)
2. Создать `Domain/Modules/Commodities/ICommodityRepository.cs` (интерфейс)
3. Создать `Application/Modules/Commodities/Contracts/ICommodityReadService.cs`
4. Создать `Application/Modules/Commodities/Contracts/ICommodityWriteService.cs`
5. Создать `Application/Modules/Commodities/Models/CommodityItemDto.cs`
6. Создать `Application/Modules/Commodities/Models/CategoryDto.cs`

### Шаг 2: Backend — Infrastructure

7. Создать `Infrastructure/Modules/Commodities/CommodityReadService.cs`
8. Создать `Infrastructure/Modules/Commodities/CommodityRepository.cs`
9. Изменить `DependencyInjectionExtensions.cs` — добавить регистрацию новых сервисов

### Шаг 3: Backend — API

10. Создать `Api/Modules/Commodities/CommodityEndpoints.cs`
11. Изменить `Program.cs` — добавить `app.MapCommodityEndpoints()`
12. Исправить `ReceiptReadService.ToReceiptDto()` — передавать `item.CategoryId` вместо `null`

### Шаг 4: Backend — Сборка и проверка

13. `dotnet build` — убедиться, что проект компилируется
14. `dotnet test` — убедиться, что существующие тесты проходят

### Шаг 5: Frontend — Инфраструктура

15. Установить `react-router-dom`: `npm install react-router-dom`
16. Создать `src/contexts/PageSizeContext.tsx`
17. Создать `src/components/Sidebar.tsx` и `Sidebar.css`
18. Создать `src/components/Layout.tsx`

### Шаг 6: Frontend — API и типы

19. Создать `src/types/commodity.ts`
20. Создать `src/api/commodities.ts`
21. Создать `src/hooks/useCommodities.ts`

### Шаг 7: Frontend — Страница товаров

22. Создать `src/components/CommodityTable.tsx` (таблица с колонками + категория для админа)
23. Создать `src/components/CommoditiesPage.tsx` (страница с хуком, пагинацией, контролами)
24. Добавить CSS-стили для `.category-display`, `.edit-category-btn`, `.app-layout`, `.content-area`

### Шаг 8: Frontend — Интеграция

25. Изменить `App.tsx` — обернуть в `BrowserRouter` и `PageSizeProvider`, добавить роуты
26. Изменить `ReceiptsPage.tsx` — использовать `usePageSize()` вместо локального состояния
27. Добавить обработку `receiptId` из query-параметров в `ReceiptsPage.tsx`

### Шаг 9: Frontend — Сборка и проверка

28. `npm run build` — убедиться, что фронтенд собирается
29. Запустить `./up.dev.sh` и проверить:
   - Sidebar отображается на обоих разделах
   - Размер страницы сохраняется между разделами
   - Дата покупки кликабельна → открываются детали чека
   - Администратор видит колонку категории с возможностью редактирования
   - Обычный пользователь не видит колонку категории
   - Все существующие функции чеков продолжают работать

---

## 6. Компромиссы (Trade-offs) и альтернативы

| Решение | Альтернатива | Обоснование |
|---------|-------------|-------------|
| **CommodityCategory enum** (в коде) | Таблица `commodity_categories` в БД | Enum проще, не требует миграции, согласован с `MerchantCategory`. Если потребуется редактирование категорий админами — таблица будет добавлена позже. |
| **React Context для pageSize** | Redux / Zustand / URL-параметр | Context — simplest solution для single shared value. URL-параметр (pageSize=20) неудобен при переключении разделов в одном сеансе. |
| **react-router-dom** | TanStack Router / wouter | Де-факто стандарт в React-экосистеме, совместимость с React 19, поддержка NavLink. |
| **Sidebar on every page** | Только на основных страницах | Требование задачи: sidebar «всегда доступен, пока пользователь аутентифицирован». |

---

## 7. ADR (Architecture Decision Record)

Поскольку решение затрагивает несколько слоёв и вводит новые паттерны, рекомендуется создать ADR:

- `docs/adr/005-commodities-feature-architecture.md` — общее решение по архитектуре раздела товаров
- `docs/adr/006-commodity-category-as-enum.md` — решение справочника категорий товаров

---

## 8. Критерии готовности (Checklist)

- [ ] `GET /api/commodities` возвращает список товаров с пагинацией и `X-Total-Count`
- [ ] `PUT /api/commodities/{id}/category` доступен только админам
- [ ] `GET /api/commodities/categories` возвращает список предопределённых категорий
- [ ] `ReceiptItemDto.CategoryId` больше не `null` (исправлено)
- [ ] Sidebar с пунктами «Чеки» и «Товары» на всех страницах
- [ ] Маршрут `/commodities` работает и сохраняется в закладки
- [ ] Таблица товаров отображает все колонки согласно требованиям
- [ ] Клик по дате открывает детали чека
- [ ] Администраторы видят колонку «Категория» с выпадающим списком
- [ ] Обычные пользователи не видят колонку «Категория»
- [ ] Размер страницы (pageSize) сохраняется при переключении между разделами
- [ ] Существующий функционал чеков продолжает работать без изменений
