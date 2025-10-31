import type { ReceiptSummary } from '../types/receipt';

export async function fetchReceipts(signal?: AbortSignal): Promise<ReceiptSummary[]> {
  const response = await fetch('/api/receipts', {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось загрузить список чеков');
  }

  const data = (await response.json()) as ReceiptSummary[];
  return data;
}
