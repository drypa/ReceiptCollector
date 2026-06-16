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
  name: string;
}

export interface PaginatedCommodities {
  commodities: CommodityItem[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}
