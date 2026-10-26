import type {
  PaginatedCommodities,
  CommodityItem,
  Category,
  CommodityCategoryFilter,
  CommodityCategoriesSuggestion,
} from '../types/commodity';

/**
 * Ошибка HTTP-запроса с кодом ответа: страница «Товары» различает 401/403 (права),
 * 409 (устаревшая страница) и сетевые сбои (ADR-028 п. 1.2, режим ошибок).
 */
export class ApiRequestError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiRequestError';
    this.status = status;
  }
}

interface FetchCommoditiesOptions {
  limit: number;
  offset: number;
  signal?: AbortSignal;
  categoryFilter?: CommodityCategoryFilter;
}

export async function fetchCommodities({ limit, offset, signal, categoryFilter = 'any' }: FetchCommoditiesOptions): Promise<PaginatedCommodities> {
  const searchParams = new URLSearchParams({
    limit: limit.toString(),
    offset: offset.toString(),
  });

  if (categoryFilter !== 'any') {
    searchParams.set('categoryFilter', categoryFilter);
  }

  const response = await fetch(`/api/commodities?${searchParams.toString()}`, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось загрузить список товаров');
  }

  const data = (await response.json()) as CommodityItem[];
  const totalHeader =
    response.headers.get('X-Total-Count') ?? response.headers.get('X-Total-Items');
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
    const message = await response.text();
    throw new Error(message || 'Не удалось загрузить категории');
  }

  return response.json() as Promise<Category[]>;
}

export async function updateCommodityCategory(commodityId: string, categoryId: number | null): Promise<void> {
  const response = await fetch(`/api/commodities/${commodityId}/category`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ categoryId }),
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось обновить категорию товара');
  }
}

/**
 * Предложения категорий для порции товаров (ADR-028 п. 1.1):
 * `POST /api/commodities/categories/suggest`. Read-only; частичный результат и
 * `aiAvailable = false` приходят кодом 200 (не 503, C7). AbortSignal проброшен —
 * клиент отменяет in-flight порцию при сохранении/отмене режима.
 */
export async function suggestCommodityCategories(
  commodityIds: string[],
  signal?: AbortSignal,
): Promise<CommodityCategoriesSuggestion> {
  const response = await fetch('/api/commodities/categories/suggest', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ commodityIds }),
    signal,
  });

  if (!response.ok) {
    throw new ApiRequestError(
      response.status,
      (await readErrorText(response)) || `Не удалось определить категории товаров (HTTP ${response.status}).`,
    );
  }

  return response.json() as Promise<CommodityCategoriesSuggestion>;
}

/**
 * Пакетное assign-only сохранение категорий (ADR-028 п. 1.2):
 * `PUT /api/commodities/categories`. `category: null` — «не трогаем» (не сброс, C4);
 * `updated` — только реально изменённые строки (считает сервер, C9).
 */
export async function saveCommodityCategories(
  items: Array<{ commodityId: string; category: string | null }>,
  signal?: AbortSignal,
): Promise<{ updated: number }> {
  const response = await fetch('/api/commodities/categories', {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ items }),
    signal,
  });

  if (!response.ok) {
    throw new ApiRequestError(
      response.status,
      (await readErrorText(response)) || `Не удалось сохранить категории товаров (HTTP ${response.status}).`,
    );
  }

  return response.json() as Promise<{ updated: number }>;
}

/**
 * Текст ошибки: сервер отдаёт ошибки простым текстом (стиль ADR-027 п. 7, без ProblemDetails).
 * Пустое тело (401/403) — пустая строка, вызывающий подставляет запасное сообщение.
 */
async function readErrorText(response: Response): Promise<string> {
  const text = (await response.text().catch(() => '')).trim();
  // HTML-страница (например, от прокси) бесполезна в подсказке.
  return text.startsWith('<') ? '' : text;
}
