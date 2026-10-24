import type {
  PaginatedPrecategorizedCommodities,
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
