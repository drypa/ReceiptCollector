import type { Merchant } from '../types/receipt';
import type { Category } from '../types/commodity';

export type MerchantDto = Merchant;

export interface PaginatedMerchants {
  merchants: MerchantDto[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}

interface FetchMerchantsOptions {
  limit: number;
  offset: number;
  search?: string;
  signal?: AbortSignal;
}

export async function fetchMerchants({ limit, offset, search, signal }: FetchMerchantsOptions): Promise<PaginatedMerchants> {
  const searchParams = new URLSearchParams({
    limit: limit.toString(),
    offset: offset.toString(),
  });

  if (search) {
    searchParams.set('search', search);
  }

  const response = await fetch(`/api/merchants?${searchParams.toString()}`, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось загрузить список магазинов');
  }

  const data = (await response.json()) as MerchantDto[];
  const totalHeader =
    response.headers.get('X-Total-Count') ?? response.headers.get('X-Total-Items');
  const parsedTotal = totalHeader ? Number.parseInt(totalHeader, 10) : Number.NaN;
  const totalItems = Number.isFinite(parsedTotal) ? parsedTotal : offset + data.length;

  return {
    merchants: data,
    totalItems,
    pageSize: limit,
    currentPage: Math.max(1, Math.floor(offset / limit) + 1),
  };
}

export async function fetchMerchantCategories(): Promise<Category[]> {
  const response = await fetch('/api/merchants/categories', {
    credentials: 'include',
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось загрузить категории магазинов');
  }

  return response.json() as Promise<Category[]>;
}

export async function updateMerchantCategory(merchantId: string, categoryId: number): Promise<void> {
  const response = await fetch(`/api/merchants/${merchantId}/category`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ categoryId }),
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось обновить категорию магазина');
  }
}

export async function updateMerchantName(merchantId: string, newName: string): Promise<void> {
  const response = await fetch(`/api/merchants/${merchantId}/name`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ name: newName }),
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось обновить имя магазина');
  }
}
