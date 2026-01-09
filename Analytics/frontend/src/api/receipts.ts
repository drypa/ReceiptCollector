import type { PaginatedReceipts, ReceiptSummary, ReceiptDetails } from '../types/receipt';

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
    throw new Error(message || 'Не удалось загрузить список чеков');
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

export async function fetchReceiptDetails(id: string): Promise<ReceiptDetails> {
  const response = await fetch(`/api/receipts/${id}`, {
    credentials: 'include',
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось загрузить детали чека');
  }

  return response.json() as Promise<ReceiptDetails>;
}

export async function updateMerchantName(merchantId: string, newName: string): Promise<void> {
  const response = await fetch(`/api/receipts/merchants/${merchantId}/name`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      credentials: 'include',
    },
    body: JSON.stringify({ name: newName }),
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось обновить имя магазина');
  }
}
