/**
 * Контракт админского раздела «Предкатегоризация» (ADR-027 п.7, FR-7).
 * Имена категории/статуса приходят с сервера (wire-имена proposed/applied).
 */
export interface PrecategorizedCommodityItem {
  id: string;
  name: string;
  merchantId: string;
  merchantName: string | null;
  proposedCategoryId: number;
  proposedCategoryName: string;
  status: string;
  createdAt: string;
}

export interface PaginatedPrecategorizedCommodities {
  items: PrecategorizedCommodityItem[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}
