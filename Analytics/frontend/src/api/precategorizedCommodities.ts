import type {
  PaginatedPrecategorizedCommodities,
  PrecategorizedBulkConfirmResult,
  PrecategorizedBulkDeclineResult,
  PrecategorizedCommodityItem,
} from '../types/precategorizedCommodity';

interface FetchPrecategorizedCommoditiesOptions {
  limit: number;
  offset: number;
  search?: string;
  signal?: AbortSignal;
}

/** Список предложений очереди (только proposed) с общим количеством из X-Total-Count (FR-5.1). */
export async function fetchPrecategorizedCommodities({
  limit,
  offset,
  search,
  signal,
}: FetchPrecategorizedCommoditiesOptions): Promise<PaginatedPrecategorizedCommodities> {
  const searchParams = new URLSearchParams({
    limit: limit.toString(),
    offset: offset.toString(),
  });

  if (search) {
    searchParams.set('search', search);
  }

  const response = await fetch(`/api/precategorized-commodities?${searchParams.toString()}`, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    throw new Error(await readError(response, 'Не удалось загрузить предложения предкатегоризации'));
  }

  const items = (await response.json()) as PrecategorizedCommodityItem[];
  const totalHeader = response.headers.get('X-Total-Count');
  const parsedTotal = totalHeader ? Number.parseInt(totalHeader, 10) : Number.NaN;
  const totalItems = Number.isFinite(parsedTotal) ? parsedTotal : offset + items.length;

  return {
    items,
    totalItems,
    pageSize: limit,
    currentPage: Math.max(1, Math.floor(offset / limit) + 1),
  };
}

/** Изменение предложенной категории без применения к товарам (FR-5.2). */
export async function updatePrecategorizedCategory(id: string, categoryId: number): Promise<void> {
  const response = await fetch(`/api/precategorized-commodities/${id}/category`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ categoryId }),
  });

  if (!response.ok) {
    throw new Error(await readError(response, 'Не удалось изменить категорию предложения'));
  }
}

export interface ConfirmPrecategorizedResult {
  id: string;
  categoryId: number;
  categoryName: string;
  updatedCommodities: number;
}

/** Подтверждение предложения: массовое обновление товаров и запись в кэш (FR-6). */
export async function confirmPrecategorized(id: string): Promise<ConfirmPrecategorizedResult> {
  const response = await fetch(`/api/precategorized-commodities/${id}/confirm`, {
    method: 'POST',
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(await readError(response, 'Не удалось подтвердить предложение'));
  }

  return response.json() as Promise<ConfirmPrecategorizedResult>;
}

/** Отклонение предложения — строка удаляется (FR-5.4). */
export async function declinePrecategorized(id: string): Promise<void> {
  const response = await fetch(`/api/precategorized-commodities/${id}`, {
    method: 'DELETE',
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(await readError(response, 'Не удалось отклонить предложение'));
  }
}

/**
 * Единый локализованный текст сбоя массовой операции (ADR-036 §1.5). Английский текст
 * `500` сервера («rolled back; no rows were applied») служит диагностике и в тост
 * администратору не подставляется.
 */
const BULK_OPERATION_ERROR_MESSAGE = 'Операция не выполнена, изменения не применены.';

/**
 * Массовое подтверждение строк текущей страницы (ADR-036 §1.1): ровно один POST на весь
 * список id, вся страница обрабатывается на сервере одной транзакцией. Порционности,
 * повторов и прогресса на клиенте нет (решения заказчика 4–5).
 */
export async function confirmPrecategorizedBatch(
  ids: string[],
): Promise<PrecategorizedBulkConfirmResult> {
  return postPrecategorizedBulk<PrecategorizedBulkConfirmResult>('batch/confirm', ids);
}

/**
 * Массовое отклонение строк текущей страницы (ADR-036 §1.2): ровно один POST,
 * одна транзакция на сервере; в ответе нет `updatedCommodities` — отклонение
 * не меняет товары.
 */
export async function declinePrecategorizedBatch(
  ids: string[],
): Promise<PrecategorizedBulkDeclineResult> {
  return postPrecategorizedBulk<PrecategorizedBulkDeclineResult>('batch/decline', ids);
}

/**
 * Общий каркас массовой операции: один `fetch` с телом `{ ids }` (D1 — категория
 * берётся сервером из снимка строки, клиент её не передаёт). Ошибки:
 * `401`/`403` — уже существующие локализованные ветки `readError`; `400` — текст
 * валидации сервера (§1.3); `5xx`, обрыв соединения и неразбираемый ответ —
 * локализованный текст «изменения не применены» (§1.5): транзакция на сервере
 * атомарна, поэтому при техническом сбое ни одна строка не применена, а повторный
 * запуск безопасен.
 */
async function postPrecategorizedBulk<TResult>(
  segment: 'batch/confirm' | 'batch/decline',
  ids: string[],
): Promise<TResult> {
  // «Пустой список не отправляем» — единственная клиентская проверка ids (план, задача 05):
  // сервер тоже отклоняет пустой список (400, §1.3), но guard не даёт послать заведомо
  // бессмысленный запрос, если вызывающий код ошибся (UI блокирует кнопки на пустой
  // странице — задачи 07–08).
  if (ids.length === 0) {
    throw new Error(BULK_OPERATION_ERROR_MESSAGE);
  }

  let response: Response;

  try {
    response = await fetch(`/api/precategorized-commodities/${segment}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      credentials: 'include',
      body: JSON.stringify({ ids }),
    });
  } catch {
    // Сетевой сбой/обрыв соединения: исход для клиента неизвестен, но транзакция
    // атомарна — честный ответ один: «изменения не применены» (ADR-036 §1.5).
    throw new Error(BULK_OPERATION_ERROR_MESSAGE);
  }

  if (!response.ok) {
    // 5xx — всегда локализованный текст, серверный в тост не подставляем (§1.5).
    if (response.status >= 500) {
      throw new Error(BULK_OPERATION_ERROR_MESSAGE);
    }

    throw new Error(await readError(response, BULK_OPERATION_ERROR_MESSAGE));
  }

  try {
    return (await response.json()) as TResult;
  } catch {
    // Успешный код, но неразбираемое тело — тот же технический сбой (§1.5).
    throw new Error(BULK_OPERATION_ERROR_MESSAGE);
  }
}

async function readError(response: Response, fallback: string): Promise<string> {
  // Ошибки API — текстовые (ADR-027 п.7). 409/403 поясняем понятнее, чем сырой текст сервера.
  if (response.status === 409) {
    return 'Предложение уже подтверждено и не может быть изменено.';
  }

  if (response.status === 403) {
    return 'Недостаточно прав для выполнения действия.';
  }

  if (response.status === 401) {
    return 'Сессия истекла. Войдите снова.';
  }

  const text = (await response.text().catch(() => '')).trim();
  return text && !text.startsWith('<') ? text : fallback;
}
