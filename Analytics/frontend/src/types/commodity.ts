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
