export interface CommodityItem {
  id: string;
  merchantName: string;
  receiptId: string;
  purchasedAt: string;
  name: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  categoryId: number | null;
  categoryName: string | null;
}

export interface Category {
  id: number;
  /** Имя enum CommodityCategory (например, "Food") — значение <select> и строковая категория в контракте PUT. */
  key: string;
  name: string;
  group?: string;
}

export type CommodityCategoryFilter = 'any' | 'uncategorized' | 'undefined';

export interface PaginatedCommodities {
  commodities: CommodityItem[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}

/**
 * Источник предложения категории (контракт POST /api/commodities/categories/suggest,
 * ADR-028 п. 1.1). Совпадает с источником suggest чека (ADR-009/027);
 * аддитивных значений сервер не добавляет.
 */
export type CategorizationSource = 'existing' | 'cache' | 'precategorized' | 'ai' | 'undefined';

/**
 * Одна запись порционного suggest (ADR-028 п. 1.1): по одной на каждую найденную строку,
 * включая повторяющиеся названия; порядок не гарантирован, сопоставление по commodityId.
 * Wire-формат идентичен ReceiptItemSuggestion чека. `source` шире union — устойчивость к
 * будущим аддитивным значениям сервера.
 */
export interface CommodityCategorizationItem {
  commodityId: string;
  name: string;
  categoryId: number | null;
  categoryName: string | null;
  source: CategorizationSource | string;
  error: string | null;
}

/** Ответ порционного suggest: items + флаг настройки ИИ (aiAvailable=false ⇒ не 503, C7). */
export interface CommodityCategoriesSuggestion {
  items: CommodityCategorizationItem[];
  aiAvailable: boolean;
}
