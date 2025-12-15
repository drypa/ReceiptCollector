import type { ReceiptSummary } from '../types/receipt';

interface ReceiptTableProps {
  receipts: ReceiptSummary[];
  onViewMerchantReceipts?: (merchantId: string) => void;
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

export function ReceiptTable({ receipts, onViewMerchantReceipts }: ReceiptTableProps) {
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
                    onClick={() => onViewMerchantReceipts(receipt.merchantId)}
                    className="merchant-link"
                  >
                    {receipt.merchant}
                  </button>
                ) : (
                  receipt.merchant
                )}
              </td>
              <td>{dateFormatter.format(new Date(receipt.purchasedAt))}</td>
              <td>{currencyFormatter.format(receipt.totalAmount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
