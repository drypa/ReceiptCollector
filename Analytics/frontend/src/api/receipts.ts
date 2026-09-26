import type { PaginatedReceipts, ReceiptSummary, ReceiptDetails } from '../types/receipt';

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

interface FetchReceiptsOptions {
  limit: number;
  offset: number;
  signal?: AbortSignal;
  merchantId?: string;
}

export async function fetchReceipts({ limit, offset, signal, merchantId }: FetchReceiptsOptions): Promise<PaginatedReceipts> {
  const searchParams = new URLSearchParams({
    limit: limit.toString(),
    offset: offset.toString(),
  });

  let url = '/api/receipts';
  if (merchantId) {
    url = `/api/receipts/by-merchant/${merchantId}`;
  }

  const response = await fetch(`${url}?${searchParams.toString()}`, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new HttpError(response.status, message || 'Не удалось загрузить список чеков');
  }

  const data = (await response.json()) as ReceiptSummary[];
  const totalHeader =
    response.headers.get('X-Total-Count') ?? response.headers.get('X-Total-Items');
  const parsedTotal = totalHeader ? Number.parseInt(totalHeader, 10) : Number.NaN;
  const totalItems = Number.isFinite(parsedTotal) ? parsedTotal : offset + data.length;

  return {
    receipts: data,
    totalItems,
    pageSize: limit,
    currentPage: Math.max(1, Math.floor(offset / limit) + 1),
  };
}

export async function fetchReceiptDetails(id: string, signal?: AbortSignal): Promise<ReceiptDetails> {
  const response = await fetch(`/api/receipts/${id}`, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new HttpError(response.status, message || 'Не удалось загрузить детали чека');
  }

  return response.json() as Promise<ReceiptDetails>;
}

/** Источник предложенной категории (контракт POST /api/receipts/{id}/categories/suggest). */
export type CategorizationSource = 'existing' | 'cache' | 'ai' | 'undefined';

export interface ReceiptItemSuggestion {
  commodityId: string;
  name: string;
  categoryId: number | null;
  categoryName: string | null;
  source: CategorizationSource;
  error: string | null;
}

export interface ReceiptSuggestResult {
  receiptId: string;
  items: ReceiptItemSuggestion[];
}

export async function suggestReceiptCategories(receiptId: string): Promise<ReceiptSuggestResult> {
  const response = await fetch(`/api/receipts/${receiptId}/categories/suggest`, {
    method: 'POST',
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(await extractErrorMessage(response, 'Не удалось определить категории товаров'));
  }

  return response.json() as Promise<ReceiptSuggestResult>;
}

async function extractErrorMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = await response.json();
    if (typeof body?.error === 'string' && body.error.length > 0) {
      return body.error;
    }
  } catch {
    // тело не JSON — используем сырой текст
  }

  const text = await response.text();
  return text || fallback;
}

export interface SaveCategoriesItem {
  commodityId: string;
  /** Имя enum CommodityCategory или null для сброса категории. */
  category: string | null;
}

export interface SaveCategoriesResult {
  receiptId: string;
  updated: number;
}

export async function saveReceiptCategories(
  receiptId: string,
  items: SaveCategoriesItem[],
): Promise<SaveCategoriesResult> {
  const response = await fetch(`/api/receipts/${receiptId}/categories`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ items }),
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось сохранить категории товаров');
  }

  return response.json() as Promise<SaveCategoriesResult>;
}
