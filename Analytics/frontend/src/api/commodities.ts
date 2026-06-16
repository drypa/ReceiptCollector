import type { PaginatedCommodities, CommodityItem, Category } from '../types/commodity';

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
