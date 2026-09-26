# План: URL-роутинг состояния аналитики и ссылка на «Чеки по магазину»

**Связанный ADR:** [ADR-023](../adr/023-analytics-url-routing.md)
**Файл задачи:** [merchants-receipts-routing.md](../tasks/merchants-receipts-routing.md)

Каждый этап самодостаточен: описанные файлы, код и критерии проверки. Порядок этапов обязателен
(этапы 1→3 можно менять местами, но этапы 4→9 зависят от результата этапа 3).

## Область изменений

| Слой | Файлы |
|---|---|
| Backend (.NET) | `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptEndpoints.cs` |
| Backend тесты | `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/ReceiptEndpointsTests.cs` (новый) |
| Frontend — новые | `src/routes.ts`, `src/hooks/useReceiptDetails.ts`, `src/components/ReceiptsListView.tsx`, `src/components/NotFoundPage.tsx`, `src/components/MerchantReceiptsPage.tsx`, `src/components/ReceiptDetailsPage.tsx` |
| Frontend — изменяемые | `src/api/receipts.ts`, `src/hooks/useReceipts.ts`, `src/components/ReceiptsPage.tsx`, `src/components/MerchantTable.tsx`, `src/components/Sidebar.tsx`, `src/App.tsx` |
| Frontend — удаляемый | `src/hooks/useReceiptsByMerchant.ts` (слияние с `useReceipts`, решение F1) |

**Без изменений:** `package.json`/lock (зависимости не добавляются), конфигурация Vite/TS/ESLint,
`ReceiptTable.tsx`, `ReceiptDetails.tsx`, `MerchantsPage.tsx`, `CommoditiesPage.tsx`, `Layout.tsx`,
`Pagination.tsx`, `Toasts.tsx`, `CustomDialog.tsx`, `PageSizeContext.tsx`, `useAdmin.ts`,
`adminService.ts`, `api/merchants.ts`, `App.css` (переиспользуем существующие классы
`.layout`, `.empty-state`, `.merchant-link`, `.state`, `.state-loading`, `.state-error`),
nginx, docker-compose, Go-бэкенд, бот, миграции БД, автотесты фронтенда.

**Порядок выкатки:** сначала backend (этап 1), затем frontend (этапы 2-9) — см. ADR-023,
раздел «Порядок развёртывания».

---

## Этап 1. Backend: 404 для неизвестного магазина

### 1.1 `ReceiptEndpoints.cs`

Добавить `using ReceiptCollector.Analytics.Domain.Modules.Merchants;` и изменить `GetByMerchant`
(сделать `public static` — как уже сделано в `MerchantEndpoints` ради тестируемости):

```csharp
public static async Task<IResult> GetByMerchant(HttpContext httpContext, Guid merchantId,
    [FromServices] IReceiptReadService service,
    [FromServices] IMerchantRepository merchantRepository,
    [FromQuery] int limit = 10, [FromQuery] int offset = 0, CancellationToken cancellationToken = default)
{
    var userId = UserContext.UserId;
    if (userId is null || userId == Guid.Empty)
    {
        return Results.BadRequest("user is not authenticated.");
    }

    if (limit <= 0)
    {
        return Results.BadRequest("limit must be greater than zero.");
    }

    if (offset < 0)
    {
        return Results.BadRequest("offset cannot be negative.");
    }

    // Неизвестный магазин — 404, чтобы клиент отличал его от «магазин есть, но чеков нет» (ADR-023, H1).
    // Проверка идёт первым запросом, поэтому для несуществующего id выборка чеков не выполняется.
    var merchant = await merchantRepository.GetByIdAsync(merchantId, cancellationToken);
    if (merchant is null)
    {
        return Results.NotFound("Merchant not found.");
    }

    var receipts = await service.GetByMerchantIdAsync(userId.Value, merchantId, limit, offset, cancellationToken);
    var totalCount = await service.GetTotalCountByMerchantIdAsync(userId.Value, merchantId, cancellationToken);
    httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
    return Results.Ok(receipts);
}
```

Текст `"Merchant not found."` — тот же, что уже используется в `MerchantEndpoints.UpdateCategory`
и `UpdateMerchantName`. `Results.NotFound(string)` отдаёт `404` с текстовым телом.

Опционально (рекомендуется): сделать `public static` и `GetById` — фронтенд теперь опирается на его
`404`, и это стоит зафиксировать тестом.

Ничего больше в .NET-коде не меняется: `IMerchantRepository` уже зарегистрирован как scoped
(`src/ReceiptCollector.Analytics.Infrastructure/Configuration/DependencyInjectionExtensions.cs:41`,
`AddScoped<IMerchantRepository, MerchantRepository>()`), зависимость `Api → Domain` разрешена
(`tests/ReceiptCollector.Analytics.Api.Tests/Architecture/ProjectDependencyTests.cs:52-53`,
в списке разрешённых для `Api` есть `ReceiptCollector.Analytics.Domain`), новых эндпоинтов и
миграций нет. `MerchantRepository.GetByIdAsync` возвращает `null` для неизвестного id
(`AsNoTracking().FirstOrDefaultAsync(...) → entity?.MapToDomain()`) — это и даёт `404`.

### 1.2 `tests/ReceiptCollector.Analytics.Api.Tests/ReceiptEndpointsTests.cs` (новый)

Образец — `MerchantEndpointsTests.cs`: NSubstitute + `using var context = UserContext.SetUserId(userId);`
+ прямой вызов `public static` метода эндпоинта. Нужные using'и:

```csharp
using Microsoft.AspNetCore.Http;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Receipts;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;

namespace ReceiptCollector.Analytics.Api.Tests;
```

Подстановки и конструкторы, которые понадобятся:

- `new Merchant(merchantId, "Магазин")` — конструктор `Merchant(Guid id, string name, MerchantCategory category = MerchantCategory.Undefined, …)`;
- `Substitute.For<IReceiptReadService>()`, `Substitute.For<IMerchantRepository>()`, `Substitute.For<DefaultHttpContext>()`;
- `GetByIdAsync(merchantId, Arg.Any<CancellationToken>()).Returns(merchant)` / `.Returns((Merchant?)null)`;
- `GetByMerchantIdAsync(userId, merchantId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(list)`;
- `GetTotalCountByMerchantIdAsync(userId, merchantId, Arg.Any<CancellationToken>()).Returns(total)`;
- `IResult` проверяется как в существующих тестах: `Assert.IsType<NotFound<string>>(result)`,
  `Assert.Equal("Merchant not found.", notFound.Value)`, `Assert.IsType<Ok<IReadOnlyCollection<ReceiptSummaryDto>>>(result)`;
- `X-Total-Count` — через `httpContext.Response.Headers["X-Total-Count"]`.

Минимально необходимые кейсы:

1. `GetByMerchant_UnknownMerchant_ReturnsNotFound`: `merchantRepository.GetByIdAsync(...) → null` →
   `Assert.IsType<NotFound<string>>(result)`, значение `"Merchant not found."`;
   `await service.DidNotReceive().GetByMerchantIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())`,
   `DidNotReceive().GetTotalCountByMerchantIdAsync(...)`;
   `await merchantRepository.Received(1).GetByIdAsync(merchantId, Arg.Any<CancellationToken>())`.
2. `GetByMerchant_ExistingMerchantWithoutReceipts_ReturnsEmptyOk`: `GetByIdAsync → Merchant`,
   `GetByMerchantIdAsync → Array.Empty<ReceiptSummaryDto>()`, `GetTotalCountByMerchantIdAsync → 0` →
   `Ok<IReadOnlyCollection<ReceiptSummaryDto>>` с пустым списком и `X-Total-Count == "0"`
   (критерий приёмки 9: «не найдено» ≠ «пустое состояние»).
3. `GetByMerchant_ExistingMerchant_ReturnsOkWithTotalCountHeader`: `total = 42`, страница из 2
   элементов → `Ok` и `X-Total-Count == "42"` (контракт для существующего магазина не изменился).
4. `GetByMerchant_WithoutAuthenticatedUser_ReturnsBadRequest`: без `UserContext.SetUserId` →
   `BadRequest<string>`, репозитории не вызваны.
5. (рекомендуется) `GetById_UnknownReceipt_ReturnsNotFound`: `GetByIdAsync → null` → `NotFound`.


### 1.3 Проверка этапа

```bash
cd Analytics && dotnet test
```

Ожидание: новые тесты проходят, существующие не сломаны. `dotnet build` — без ошибок.

---

## Этап 2. Frontend: `src/routes.ts` (новый модуль)

Единственное место, где собираются пути (иначе схема размазывается по шести файлам) + проверка
формата идентификатора (нужна из-за `MapFallbackToFile`, см. ADR-023 I1):

```ts
/**
 * Пути маршрутов — единственный источник правды по схеме URL (ADR-023).
 * Соответствует таблице маршрутов в App.tsx.
 */
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Идентификаторы — UUID (Postgres uuid). Невалидный id не отправляется в API (см. ADR-023, I1). */
export function isGuid(value: string | null | undefined): value is string {
  return typeof value === 'string' && GUID_PATTERN.test(value);
}

export function receiptsPath(): string {
  return '/receipts';
}

export function receiptPath(receiptId: string): string {
  return `/receipts/${encodeURIComponent(receiptId)}`;
}

export function commoditiesPath(): string {
  return '/commodities';
}

export function merchantsPath(): string {
  return '/merchants';
}

export function merchantReceiptsPath(merchantId: string): string {
  return `/merchants/${encodeURIComponent(merchantId)}/receipts`;
}
```

---

## Этап 3. Frontend: ошибки API с HTTP-статусом

`src/api/receipts.ts`:

1. Добавить класс ошибки и предикат (в этом же файле — других потребителей пока нет, YAGNI):

```ts
/** Ошибка HTTP-запроса с кодом ответа: страницы различают 404 («Не найдено») и остальные сбои. */
export class HttpError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'HttpError';
    this.status = status;
  }
}

export function isNotFound(error: unknown): boolean {
  return error instanceof HttpError && error.status === 404;
}
```

2. В `fetchReceipts` и `fetchReceiptDetails` заменить `throw new Error(message || '…')` на
   `throw new HttpError(response.status, message || '…')`. Остальную логику (чтение
   `X-Total-Count`, `credentials: 'include'`) не трогать.
3. В `fetchReceiptDetails` добавить необязательный `signal` (нужен для abort в новом хуке):
   `export async function fetchReceiptDetails(id: string, signal?: AbortSignal)` и передать его
   в `fetch(..., { credentials: 'include', signal })`.

**Важно:** не добавлять сюда разбор HTML-ответа и не «лечить» SPA-fallback — невалидные id
отсекаются в точках входа маршрутов (этап 7, `isGuid`).

---

## Этап 4. Frontend: один хук списка чеков

`src/hooks/useReceipts.ts` — добавить необязательный `merchantId` и признак «не найдено»,
воспользовавшись уже существующей поддержкой `merchantId` в `fetchReceipts`:

1. `interface UseReceiptsOptions { pageSize?: number; merchantId?: string }`.
2. `export function useReceipts({ pageSize = 10, merchantId }: UseReceiptsOptions = {})`.
3. `loadPage`: в `[pageSize, merchantId]` deps; в вызов
   `fetchReceipts({ limit: pageSize, offset, signal: controller.signal, merchantId })`;
   в начале `setNotFound(false)` рядом с `setError(null)`.
4. `.catch`: после проверки на `AbortError` —
   `if (isNotFound(fetchError)) { setNotFound(true); setReceipts([]); setTotalItems(0); return; }`,
   иначе прежнее `setError(...)`.
5. Вернуть `notFound` в объекте возврата.
6. Abort при unmount уже есть (cleanup во втором эффекте, строки 62-68) — сохранить.

Удалить `src/hooks/useReceiptsByMerchant.ts` (потребитель только один — `ReceiptsPage`, который
переписывается на этапе 7.1; других импортов нет). Два эффекта, дублирующих `loadPage(1)`, в
`useReceipts` **не трогаем** (второй запрос сам отменяется через `AbortController`) — это не задача
этой правки.

---

## Этап 5. Frontend: хук карточки чека

`src/hooks/useReceiptDetails.ts` (новый) — по образцу `useReceipts` (тот же
`AbortController` + проверка `abortRef.current === controller`):

```ts
import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchReceiptDetails, isNotFound } from '../api/receipts';
import type { ReceiptDetails } from '../types/receipt';

/** Загрузка карточки чека по id из URL: состояния те же, что у списков (ADR-023, F3). */
export function useReceiptDetails(receiptId: string) {
  const [receipt, setReceipt] = useState<ReceiptDetails | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const load = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setIsLoading(true);
    setError(null);
    setNotFound(false);

    fetchReceiptDetails(receiptId, controller.signal)
      .then((details) => {
        setReceipt(details);
      })
      .catch((fetchError) => {
        if (fetchError instanceof DOMException && fetchError.name === 'AbortError') {
          return;
        }
        if (isNotFound(fetchError)) {
          setNotFound(true);
          setReceipt(null);
          return;
        }
        setReceipt(null);
        setError(fetchError instanceof Error ? fetchError.message : 'Неизвестная ошибка');
      })
      .finally(() => {
        if (abortRef.current === controller) {
          setIsLoading(false);
        }
      });
  }, [receiptId]);

  useEffect(() => {
    load();

    return () => {
      abortRef.current?.abort();
    };
  }, [load]);

  return { receipt, isLoading, notFound, error, refresh: load };
}
```

---

## Этап 6. Frontend: переиспользуемые компоненты

### 6.1 `src/components/NotFoundPage.tsx` (новый)

Используется 4 раза: catch-all маршрут, несуществующий магазин, несуществующий/мусорный чек,
неизвестный путь. Пропсы — только заголовок и пояснение (YAGNI: без «действий» в произвольных
вариантах, единственное действие — ссылка на список чеков).

```tsx
import { Link } from 'react-router-dom';
import { receiptsPath } from '../routes';

interface NotFoundPageProps {
  title?: string;
  description?: string;
}

export function NotFoundPage({
  title = 'Не найдено',
  description = 'Страница или запрошенный объект не найден.',
}: NotFoundPageProps) {
  return (
    <div className="layout">
      <header>
        <h1>{title}</h1>
      </header>
      <div className="empty-state">
        <p>{description}</p>
        <Link to={receiptsPath()} className="merchant-link">
          Вернуться к списку чеков
        </Link>
      </div>
    </div>
  );
}
```

`<div className="layout">` (как в `MerchantsPage`), а не `<main>`: `Layout` уже рендерит
`<main className="content-area">`, вложенный `<main>` невалиден.

### 6.2 `src/components/ReceiptsListView.tsx` (новый)

Презентационный компонент: без `useNavigate`, без загрузки данных. Ровно та разметка, что сейчас
в `ReceiptsPage.tsx:96-190` (заголовок, «Найдено чеков», выбор размера страницы, «Обновить»,
состояния загрузки/ошибки, `ReceiptTable`, `Pagination`).

```tsx
import type { ReactNode } from 'react';
import { usePageSize } from '../contexts/PageSizeContext';
import type { useReceipts } from '../hooks/useReceipts';
import { Pagination } from './Pagination';
import { ReceiptTable } from './ReceiptTable';

type ReceiptsListState = ReturnType<typeof useReceipts>;

interface ReceiptsListViewProps {
  title: string;
  list: ReceiptsListState;
  onViewMerchantReceipts: (merchantId: string) => void;
  onReceiptClick: (receiptId: string) => void;
  /** Дополнительное действие в шапке (кнопка «Все чеки»). */
  headerAction?: ReactNode;
}

export function ReceiptsListView({
  title,
  list,
  onViewMerchantReceipts,
  onReceiptClick,
  headerAction,
}: ReceiptsListViewProps) {
  const { setPageSize, pageSizeOptions } = usePageSize();
  const { data, isLoading, error, currentPage, totalPages, totalItems, pageSize, refresh, goToPage, nextPage, previousPage } = list;

  return (
    <div className="layout">
      <header>
        <div>
          <h1>{title}</h1>
          <p>
            Найдено чеков: <strong>{totalItems}</strong>
            {headerAction}
          </p>
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
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </div>
          <button type="button" onClick={refresh} disabled={isLoading}>
            Обновить
          </button>
        </div>
      </header>

      {isLoading && (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем чеки...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить чеки: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      )}

      {!isLoading && !error && (
        <ReceiptTable
          receipts={data}
          onViewMerchantReceipts={onViewMerchantReceipts}
          onReceiptClick={onReceiptClick}
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
    </div>
  );
}
```

Замечания по реализации:

- `ReceiptTable` сама показывает пустое состояние «Чеки не найдены.» при `receipts.length === 0`
  (критерий 9) — дублировать не надо.
- Ветку `notFound` обрабатывает **страница**: если `list.notFound` — рендерится `NotFoundPage`
  вместо списка. Компонент про «не найдено» ничего не знает.
- Всё остальное (`usePageSize`, `.layout`, `.state*`, `.pagination`) — как сейчас, чтобы diff
  экрана был нулевым.

---

## Этап 7. Frontend: три экрана

### 7.1 `src/components/ReceiptsPage.tsx` — переписать целиком

```tsx
import { useNavigate } from 'react-router-dom';
import { usePageSize } from '../contexts/PageSizeContext';
import { useReceipts } from '../hooks/useReceipts';
import { merchantReceiptsPath, receiptPath } from '../routes';
import { ReceiptsListView } from './ReceiptsListView';

export function ReceiptsPage() {
  const navigate = useNavigate();
  const { pageSize } = usePageSize();
  const list = useReceipts({ pageSize });

  return (
    <ReceiptsListView
      title="Мои чеки"
      list={list}
      onViewMerchantReceipts={(merchantId) => navigate(merchantReceiptsPath(merchantId))}
      onReceiptClick={(receiptId) => navigate(receiptPath(receiptId))}
    />
  );
}
```

Удалённое (проверить по diff, что не осталось): `useSearchParams`, `receiptIdFromUrl`, эффект с
`setSearchParams({}, { replace: true })`, `selectedMerchantId`, `selectedReceiptId`,
`receiptDetails`, `loadingReceiptDetails`, `handleReceiptClick`, `handleBackToList`,
`handleBackToAllReceipts`, импорт `useReceiptsByMerchant`, импорт `ReceiptDetails`,
импорт `fetchReceiptDetails`, тернарная цепочка выбора данных, собственный `fetch` карточки.

### 7.2 `src/components/MerchantReceiptsPage.tsx` (новый)

```tsx
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { usePageSize } from '../contexts/PageSizeContext';
import { useReceipts } from '../hooks/useReceipts';
import { isGuid, merchantReceiptsPath, receiptPath, receiptsPath } from '../routes';
import { NotFoundPage } from './NotFoundPage';
import { ReceiptsListView } from './ReceiptsListView';

export function MerchantReceiptsPage() {
  const { merchantId } = useParams<{ merchantId: string }>();

  // Проверка формата — до любых хуков с данными и без запроса (ADR-023, I1).
  if (!isGuid(merchantId)) {
    return (
      <NotFoundPage
        title="Магазин не найден"
        description="Ссылка содержит некорректный идентификатор магазина."
      />
    );
  }

  // key — чтобы смена магазина пересоздавала экран вместе с хуком (ADR-023, G1):
  // react-router не размонтирует компонент при смене параметра в том же маршруте.
  return <MerchantReceiptsView key={merchantId} merchantId={merchantId} />;
}

interface MerchantReceiptsViewProps {
  merchantId: string;
}

function MerchantReceiptsView({ merchantId }: MerchantReceiptsViewProps) {
  const navigate = useNavigate();
  const location = useLocation();
  const { pageSize } = usePageSize();
  const list = useReceipts({ pageSize, merchantId });

  if (list.notFound) {
    return (
      <NotFoundPage
        title="Магазин не найден"
        description="Магазин, указанный в ссылке, не найден."
      />
    );
  }

  const handleViewMerchantReceipts = (targetMerchantId: string) => {
    const path = merchantReceiptsPath(targetMerchantId);
    // На этом же экране не плодим дублирующую запись истории.
    if (path !== location.pathname) {
      navigate(path);
    }
  };

  return (
    <ReceiptsListView
      title="Чеки по магазину"
      list={list}
      headerAction={
        <button
          type="button"
          onClick={() => navigate(receiptsPath())}
          style={{ marginLeft: '1rem' }}
          className="secondary"
        >
          Все чеки
        </button>
      }
      onViewMerchantReceipts={handleViewMerchantReceipts}
      onReceiptClick={(receiptId) => navigate(receiptPath(receiptId))}
    />
  );
}
```

Важно: все хуки вызываются до ранних `return` (правила хуков + `npm run lint`).

### 7.3 `src/components/ReceiptDetailsPage.tsx` (новый)

```tsx
import { useNavigate, useParams } from 'react-router-dom';
import { useReceiptDetails } from '../hooks/useReceiptDetails';
import { isGuid, receiptsPath } from '../routes';
import { NotFoundPage } from './NotFoundPage';
import { ReceiptDetails } from './ReceiptDetails';

export function ReceiptDetailsPage() {
  const { receiptId } = useParams<{ receiptId: string }>();

  if (!isGuid(receiptId)) {
    return (
      <NotFoundPage
        title="Чек не найден"
        description="Ссылка содержит некорректный идентификатор чека."
      />
    );
  }

  return <ReceiptDetailsView key={receiptId} receiptId={receiptId} />;
}

interface ReceiptDetailsViewProps {
  receiptId: string;
}

function ReceiptDetailsView({ receiptId }: ReceiptDetailsViewProps) {
  const navigate = useNavigate();
  const { receipt, isLoading, notFound, error, refresh } = useReceiptDetails(receiptId);

  /**
   * «Назад» возвращает на предыдущий экран, если он был внутри приложения
   * (idx > 0 — признак push-перехода react-router в этом документе),
   * иначе — на общий список чеков (ADR-023, D1).
   */
  const handleBack = () => {
    const idx: number | undefined = window.history.state?.idx ?? undefined;
    if (typeof idx === 'number' && idx > 0) {
      navigate(-1);
      return;
    }

    navigate(receiptsPath());
  };

  if (isLoading) {
    return (
      <div className="layout">
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем детали чека...
        </div>
      </div>
    );
  }

  if (notFound) {
    return (
      <NotFoundPage
        title="Чек не найден"
        description="Чек, указанный в ссылке, не найден."
      />
    );
  }

  if (error) {
    return (
      <div className="layout">
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить чек: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      </div>
    );
  }

  return (
    // Обёртка .layout обязательна: сегодня карточка рендерится внутри <main className="layout">
    // (ReceiptsPage.tsx:167-175), а .receipt-details имеет свои max-width/padding.
    // Без .layout карточка станет шире и с другими отступами (ADR-023, факт 12).
    <div className="layout">
      <ReceiptDetails receipt={receipt} onBack={handleBack} onReceiptRefresh={refresh} />
    </div>
  );
}
```

`ReceiptDetails` (карточка) не меняется: пропсы те же, что и раньше (`receipt`, `onBack`,
`onReceiptRefresh`), а её ветка «`receipt === null` → „Чек не найден.“» остаётся защитным
fallback и страницей не используется. `onReceiptRefresh` после сохранения категорий теперь
перезагружает карточку без перезагрузки страницы (раньше — повторный вызов `handleReceiptClick`).
Все три состояния (загрузка/ошибка/карточка) обёрнуты в `<div className="layout">` — как сейчас
при открытой карточке.

---

## Этап 8. Frontend: маршруты и меню

### 8.1 `src/App.tsx`

Импорты: заменить `import { BrowserRouter, Routes, Route } from 'react-router-dom';` на
`import { BrowserRouter, Navigate, Route, Routes, useSearchParams } from 'react-router-dom';`
и добавить
`import { MerchantReceiptsPage } from './components/MerchantReceiptsPage';`,
`import { ReceiptDetailsPage } from './components/ReceiptDetailsPage';`,
`import { NotFoundPage } from './components/NotFoundPage';`,
`import { isGuid, receiptPath, receiptsPath } from './routes';`.
Остальные импорты и `BrowserRouter`/`ToastProvider`/`PageSizeProvider` — без изменений.

Заменить блок `<Routes>` (внутри `PageSizeProvider`, структура провайдеров не меняется):

```tsx
<Routes>
  <Route element={<Layout />}>
    {/* Совместимость со старыми ссылками: / и /?receiptId={id} (ADR-023, C1). */}
    <Route path="/" element={<RootRedirect />} />
    <Route path="/receipts" element={<ReceiptsPage />} />
    <Route path="/receipts/:receiptId" element={<ReceiptDetailsPage />} />
    <Route path="/commodities" element={<CommoditiesPage />} />
    <Route path="/merchants" element={<MerchantsPage />} />
    <Route path="/merchants/:merchantId/receipts" element={<MerchantReceiptsPage />} />
    <Route path="*" element={<NotFoundPage />} />
  </Route>
</Routes>
```

```tsx
/**
 * Редирект со старого корня «/» на «/receipts».
 * Старая форма «/?receiptId={id}» (например, переход из раздела «Товары»)
 * превращается в «/receipts/{id}». replace — чтобы редирект не попадал в историю.
 */
function RootRedirect() {
  const [searchParams] = useSearchParams();
  const receiptId = searchParams.get('receiptId');

  if (receiptId) {
    return <Navigate to={isGuid(receiptId) ? receiptPath(receiptId) : receiptsPath()} replace />;
  }

  return <Navigate to={receiptsPath()} replace />;
}
```

`RootRedirect` объявить в этом же файле ниже `App` (или выше — по вкусу), **вне** `App`,
чтобы не менять структуру `App`.

### 8.2 `src/components/Sidebar.tsx`

Одна строка: `to="/"` → `to="/receipts"`, **убрать `end`**, чтобы пункт «Чеки» подсвечивался и
на `/receipts`, и на `/receipts/{id}`. Пункт «Магазины» (`to="/merchants"`) не трогаем: без
`end` он подсвечивается и на экране «Чеки по магазину» — это текущее поведение и по критериям
задачи ничего не ломает.

---

## Этап 9. Frontend: ссылка на магазин в таблице «Магазины»

`src/components/MerchantTable.tsx` — в ветке отображения имени (не в режиме редактирования)
заменить `<span>{merchant.name}</span>` на ссылку:

```tsx
import { Link } from 'react-router-dom';
import { merchantReceiptsPath } from '../routes';

// ... внутри merchants.map, ветка «не редактируем и не сохраняем»:
<div className="merchant-name-edit">
  <Link to={merchantReceiptsPath(merchant.id)} className="merchant-link">
    {merchant.name}
  </Link>
  {isAdmin && (
    <button
      type="button"
      className="edit-category-btn"
      onClick={() => handleNameEditStart(merchant)}
      title="Редактировать имя"
    >
      ред.
    </button>
  )}
</div>
```

Класс `.merchant-link` уже определён в `App.css:294` (`color: var(--primary)`,
`text-decoration: underline`) и на `<a>` выглядит так же, как на кнопке в таблице чеков — новых
стилей не нужно.

Не трогать: ветки `savingId === merchant.id` («Сохранение...») и `editingNameId === merchant.id`
(поле ввода + «Сохранить»/«Отмена»). Требование «ссылка присутствует всегда» трактуется как
«кроме режима редактирования» (так оно и сформулировано в задаче); поведение во время
сохранения имени остаётся прежним.

---

## Проверка

```bash
cd Analytics && dotnet test                                  # этап 1
cd Analytics/frontend && npm run build                       # tsc -b + vite build
cd Analytics/frontend && npm run lint                        # eslint
```

Ожидания: `dotnet test` — все тесты проходят (включая 4-5 новых); `npm run build` — без ошибок
(это же проверяет CI в Docker-сборке образа); `npm run lint` — без ошибок и предупреждений
(в частности, без нарушений правил хуков).

Опционально для локальной проверки маршрутов: `npm run dev` (Vite, порт 5173) + любой способ
получить чеки/магазины в БД.

## Ручная приёмка (чек-лист критериев задачи)

Маршруты и переходы:

- [ ] `/receipts` — «Мои чеки», список чеков, пагинация работает.
- [ ] `/merchants` — список магазинов (админ), клик по **имени** открывает
      `/merchants/{id}/receipts`, в адресной строке — id магазина.
- [ ] Правый клик / Ctrl+клик / средняя кнопка по имени магазина — открытие в новой вкладке.
- [ ] F5 на `/merchants/{id}/receipts` — тот же экран (SPA-fallback nginx работает).
- [ ] «Назад» после перехода из `/merchants` возвращает на список магазинов; «Вперёд» — обратно.
- [ ] Кнопка «Магазин» в таблице чеков (кнопка, не ссылка) открывает тот же экран чеков магазина.
- [ ] «Все чеки» на экране «Чеки по магазину» возвращает на `/receipts`; заголовок —
      «Чеки по магазину» **без имени магазина**.
- [ ] Клик по чеку (дата покупки) → `/receipts/{id}`; F5 сохраняет открытый чек.
- [ ] Кнопка «← Назад к списку чеков» в карточке: из списка магазина возвращает в чеки магазина;
      из прямой ссылки в новой вкладке — на `/receipts`.
- [ ] В адресной строке только путь с id (нет `?receiptId`, номера и размера страницы, фильтров).
- [ ] Смена размера страницы и номера страницы не меняет URL.
- [ ] Смена `pageSize` сохраняется при переходах между разделами (регрессия ADR-006: pageSize в
      React Context, не в URL).

Ошибки и краевые случаи:

- [ ] `/merchants/{случайный guid}/receipts` → «Магазин не найден» + ссылка «Вернуться к списку чеков».
- [ ] `/receipts/{случайный guid}` → «Чек не найден» + ссылка на список.
- [ ] `/merchants/{существующий guid}/receipts` для магазина без чеков → пустое состояние
      «Чеки не найдены.», **не** «Не найдено».
- [ ] `/чеки`, `/receipts/abc/def`, `/merchants//receipts`, `/receipts/abc` → «Не найдено».
- [ ] Не-админ: пункт «Магазины» в меню скрыт; прямой заход на `/merchants` → «Доступ запрещён»;
      прямой заход на `/merchants/{id}/receipts` → экран чеков магазина **без** «Доступ запрещён».
- [ ] Быстрое переключение: клик по чеку → «Назад» → «Вперёд», и смена `merchantId` подряд —
      данные всегда соответствуют URL, чужие не показываются.
- [ ] Остановка backend: `/merchants/{существующий guid}/receipts` → блок «Не удалось загрузить
      чеки» с «Попробовать снова», а не «Не найдено».

Совместимость:

- [ ] Старая закладка `/` → адресная строка становится `/receipts`.
- [ ] Старая закладка `/?receiptId={id}` → `/receipts/{id}`, чек открыт.
- [ ] Переход из раздела «Товары» по чеку (внутри приложения использует `/?receiptId=`) работает.
- [ ] Переименование магазина не ломает ранее выданную ссылку на его чеки (в адресе id).

## Где риски (прицельно)

| Место | Риск | Что делать |
|---|---|---|
| `useReceipts` (этап 4) | Регрессия горячего экрана «Мои чеки» при слиянии хуков | Изменение механическое; проверить оба списка, пагинацию, «Обновить», смену `pageSize` |
| `ReceiptsListView` (этап 6) | Потеря/сдвиг разметки (отступы, порядок блоков) | Сверять с `ReceiptsPage.tsx:96-190` построчно; `.layout header` — flex, порядок `h1`/кнопка важен; `<main className="layout">` → `<div className="layout">` (как в `MerchantsPage`) |
| `ReceiptDetailsPage` (этап 7) | Карточка стала шире/с другими отступами — забыли обёртку `.layout` | Все три состояния возвращают `<div className="layout">`; сверить с `ReceiptsPage.tsx:167-175` |
| `App.tsx` (этап 8) | Ранний `return` до хуков в новых экранах → падение `npm run lint` | `isGuid`-проверка только во **внешнем** компоненте; хуки — во внутреннем |
| `Sidebar` (этап 8) | Пропала подсветка «Чеки» | `to="/receipts"` **без** `end`; проверить на `/receipts` и `/receipts/{id}` |
| `ReceiptDetailsPage` (этап 7) | Кнопка «Назад» выкидывает из приложения на прямой ссылке | Проверка `typeof idx === 'number' && idx > 0` + fallback `/receipts`; тест в двух сценариях |
| `ReceiptEndpoints` (этап 1) | 404 там, где раньше был `200 + []` (регресс старого фронта) | Выкатка сначала backend; окно между выкатками минутное |
| `api/receipts.ts` (этап 3) | `HttpError` ломает существующие `catch (error instanceof Error …)` | `HttpError extends Error` — `instanceof Error` остаётся истинным; реальные потребители ошибок `api/receipts.ts`: `useReceipts.catch`, `ReceiptsPage.handleReceiptClick` (уходит в `console.error`) и новый `useReceiptDetails`. `instanceof Error` в `ReceiptDetails` относится к `api/commodities` и не затрагивается |

## Чек-лист ревью

- [ ] В `App.tsx` ровно 7 маршрутов, таблица совпадает с ADR-023.
- [ ] Ни одного `useState`, дублирующего `merchantId`/`receiptId`, и ни одного `setSearchParams`.
- [ ] `useReceiptsByMerchant.ts` удалён, импортов на него не осталось (`grep -r useReceiptsByMerchant src` пусто).
- [ ] `ReceiptTable.tsx` и `ReceiptDetails.tsx` не изменены (`git diff --stat`).
- [ ] `App.css` не изменён.
- [ ] Новые тесты `ReceiptEndpointsTests.cs` покрывают 404 / пустой список / заголовок / без auth.
- [ ] `git diff` не содержит изменений в `CommoditiesPage.tsx`, `MerchantsPage.tsx`, nginx, Go.
