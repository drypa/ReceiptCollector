export interface ReceiptSummary {
  id: string;
  merchant: string;
  merchantId: string;
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
  merchant: string;
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
