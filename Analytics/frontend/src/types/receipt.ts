export interface ReceiptSummary {
  id: string;
  merchant: string;
  totalAmount: number;
  purchasedAt: string;
}

export interface PaginatedReceipts {
  receipts: ReceiptSummary[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}
