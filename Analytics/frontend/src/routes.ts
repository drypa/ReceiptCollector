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
