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

/** Ответ PUT default-commodity-category: сохранённая пара код/имя; при сбросе оба `null` (ADR-032, п. 6). */
export interface UpdateMerchantDefaultCategoryResult {
  categoryId: number | null;
  categoryName: string | null;
}

/** Ответ POST .../apply: число позиций, получивших категорию; `0` — все уже категоризированы (идемпотентность). */
export interface ApplyMerchantDefaultCategoryResult {
  updated: number;
}

/**
 * Установить или сбросить товарную категорию по умолчанию магазина (ADR-032, п. 6–7; FR-M1).
 * `categoryId === null` — сброс (тело `{ categoryId: null }`); код проверяет сервер
 * (`Enum.IsDefined`, `!= Undefined` → 400). Права: только админ (401/403 различаются ниже).
 * Существующие методы файла (fetchMerchants/updateMerchantCategory/updateMerchantName) не затронуты.
 */
export async function updateMerchantDefaultCategory(
  merchantId: string,
  categoryId: number | null,
): Promise<UpdateMerchantDefaultCategoryResult> {
  const response = await fetch(`/api/merchants/${merchantId}/default-commodity-category`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify({ categoryId }),
  });

  if (!response.ok) {
    throw new Error(
      await readDefaultCategoryError(
        response,
        'Не удалось обновить категорию по умолчанию',
        'Недопустимое значение товарной категории.',
      ),
    );
  }

  return response.json() as Promise<UpdateMerchantDefaultCategoryResult>;
}

/**
 * Дожим категорией по умолчанию существующих позиций магазина без категории
 * (ADR-032, п. 4/6–7; FR-M3) — операция кнопки «Категоризировать существующие».
 * `updated: 0` — успех (все позиции уже категоризированы); 400 — дефолт не настроен.
 */
export async function applyMerchantDefaultCategory(
  merchantId: string,
): Promise<ApplyMerchantDefaultCategoryResult> {
  const response = await fetch(`/api/merchants/${merchantId}/default-commodity-category/apply`, {
    method: 'POST',
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error(
      await readDefaultCategoryError(
        response,
        'Не удалось категоризовать существующие товары',
        'Категория по умолчанию не настроена для этого магазина.',
      ),
    );
  }

  return response.json() as Promise<ApplyMerchantDefaultCategoryResult>;
}

/**
 * Понятная русская ошибка для эндпоинтов дефолтной категории (прецедент `readError`
 * в api/precategorizedCommodities.ts): 401/403/404/400 различаемы на клиенте; для прочих
 * статусов — серверный текст (ошибки API текстовые, ADR-027 п. 7) с защитой от HTML и fallback.
 * Семантика 400 у эндпоинтов разная (PUT — недопустимый код справочника, apply — дефолт
 * не настроен), поэтому сообщение о 400 передаётся вызывающим кодом.
 */
async function readDefaultCategoryError(
  response: Response,
  fallback: string,
  badRequestMessage: string,
): Promise<string> {
  if (response.status === 401) {
    return 'Сессия истекла. Войдите снова.';
  }

  if (response.status === 403) {
    return 'Недостаточно прав для выполнения действия.';
  }

  if (response.status === 404) {
    return 'Магазин не найден. Обновите страницу и попробуйте снова.';
  }

  if (response.status === 400) {
    return badRequestMessage;
  }

  const text = (await response.text().catch(() => '')).trim();
  return text && !text.startsWith('<') ? text : fallback;
}
