import type { ReceiptSummary } from '../types/receipt';

interface ReceiptTableProps {
  receipts: ReceiptSummary[];
  onViewMerchantReceipts?: (merchantId: string) => void;
  onReceiptClick?: (receiptId: string) => void;
}

const currencyFormatter = new Intl.NumberFormat('ru-RU', {
  style: 'currency',
  currency: 'RUB',
  minimumFractionDigits: 2,
});

const dateFormatter = new Intl.DateTimeFormat('ru-RU', {
  year: 'numeric',
  month: 'long',
  day: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

export function ReceiptTable({ receipts, onViewMerchantReceipts, onReceiptClick }: ReceiptTableProps) {
  if (receipts.length === 0) {
    return (
      <div className="empty-state">
        <p>Чеки не найдены.</p>
      </div>
    );
  }

  return (
    <div className="table-wrapper">
      <table>
        <thead>
          <tr>
            <th>Магазин</th>
            <th>Дата покупки</th>
            <th>Сумма</th>
          </tr>
        </thead>
        <tbody>
          {receipts.map((receipt) => (
            <tr key={receipt.id}>
              <td>
                {onViewMerchantReceipts ? (
                  <button
                    type="button"
                    onClick={() => onViewMerchantReceipts(receipt.merchant.id)}
                    className="merchant-link"
                  >
                    {receipt.merchant.name}
                  </button>
                ) : (
                  receipt.merchant.name
                )}
              </td>
              <td>
                {onReceiptClick ? (
                  <button
                    type="button"
                    onClick={() => onReceiptClick(receipt.id)}
                    className="date-link"
                  >
                    {dateFormatter.format(new Date(receipt.purchasedAt))}
                  </button>
                ) : (
                  dateFormatter.format(new Date(receipt.purchasedAt))
                )}
              </td>
              <td>{currencyFormatter.format(receipt.totalAmount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
