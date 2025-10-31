import type { PaginatedReceipts, ReceiptSummary } from '../types/receipt';

interface FetchReceiptsOptions {
  limit: number;
  offset: number;
  signal?: AbortSignal;
}

export async function fetchReceipts({ limit, offset, signal }: FetchReceiptsOptions): Promise<PaginatedReceipts> {
  const searchParams = new URLSearchParams({
    limit: limit.toString(),
    offset: offset.toString(),
  });

  const response = await fetch(`/api/receipts?${searchParams.toString()}`, {
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
