export interface Merchant {
  id: string;
  name: string;
  category: number;
  address: string | null;
  inn: string | null;
}

export interface ReceiptSummary {
  id: string;
  merchant: Merchant;
  totalAmount: number;
  purchasedAt: string;
}

export interface ReceiptItem {
  name: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  categoryId: string | null;
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
