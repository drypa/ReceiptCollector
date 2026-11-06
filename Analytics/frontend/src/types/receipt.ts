export interface Merchant {
  id: string;
  name: string;
  category: number;
  address: string | null;
  inn: string | null;
  /**
   * Код товарной категории по умолчанию (справочник `CommodityCategory`) либо `null` — не задана
   * (ADR-032, FR-M1). Хранится отдельно от `category` магазина (другой справочник `MerchantCategory`).
   * Прочие поля не менялись — поля аддитивны к wire-контракту `MerchantDto` (T-07).
   */
  defaultCommodityCategoryId: number | null;
  /**
   * Отображаемое имя категории по умолчанию (`CommodityCategoryHelper.GetDisplayName`) либо `null` —
   * не задана (ADR-032, п. 2: имя формируется на сервере, в справочнике БД не лежит).
   */
  defaultCommodityCategoryName: string | null;
}

export interface ReceiptSummary {
  id: string;
  merchant: Merchant;
  totalAmount: number;
  purchasedAt: string;
}

export interface ReceiptItem {
  id: string;
  name: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  categoryId: number | null;
}

export interface ReceiptDetails {
  id: string;
  merchant: Merchant;
  totalAmount: number;
  purchasedAt: string;
  items: ReceiptItem[];
}

export interface PaginatedReceipts {
  receipts: ReceiptSummary[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}
