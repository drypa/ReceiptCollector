import type { ReceiptDetails } from '../types/receipt';

interface ReceiptDetailsProps {
  receipt: ReceiptDetails | null;
  onBack: () => void;
}

export function ReceiptDetails({ receipt, onBack }: ReceiptDetailsProps) {
  if (!receipt) {
    return (
      <div className="receipt-details">
        <h2>Детали чека</h2>
        <p>Чек не найден.</p>
        <button type="button" onClick={onBack} className="back-button">
          Назад к списку чеков
        </button>
      </div>
    );
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

  return (
    <div className="receipt-details">
      <div className="receipt-header">
        <button type="button" onClick={onBack} className="back-button">
          ← Назад к списку чеков
        </button>
        <h2>Детали чека</h2>
      </div>

      <div className="receipt-summary">
        <div className="summary-item">
          <strong>Id:</strong> {receipt.id}
        </div>
        <div className="summary-item">
          <strong>Магазин:</strong> {receipt.merchant}
        </div>
        <div className="summary-item">
          <strong>Дата покупки:</strong> {dateFormatter.format(new Date(receipt.purchasedAt))}
        </div>
        <div className="summary-item">
          <strong>Сумма:</strong> {currencyFormatter.format(receipt.totalAmount)}
        </div>
      </div>

      <div className="receipt-items">
        <h3>Товары</h3>
        {receipt.items.length > 0 ? (
          <table className="items-table">
            <thead>
              <tr>
                <th>Название</th>
                <th>Количество</th>
                <th>Цена за единицу</th>
                <th>Общая цена</th>
              </tr>
            </thead>
            <tbody>
              {receipt.items.map((item, index) => (
                <tr key={index}>
                  <td>{item.name}</td>
                  <td>{item.quantity}</td>
                  <td>{currencyFormatter.format(item.unitPrice)}</td>
                  <td>{currencyFormatter.format(item.totalPrice)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <p>Товары не найдены.</p>
        )}
      </div>
    </div>
  );
}