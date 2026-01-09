import { useState } from 'react';
import type { ReceiptDetails } from '../types/receipt';
import { useAdmin } from '../hooks/useAdmin';
import { updateMerchantName } from '../api/receipts';

interface ReceiptDetailsProps {
  receipt: ReceiptDetails | null;
  onBack: () => void;
}

export function ReceiptDetails({ receipt, onBack }: ReceiptDetailsProps) {
  const { isAdmin } = useAdmin();
  const [isEditing, setIsEditing] = useState(false);
  const [editingName, setEditingName] = useState('');
  const [isLoading, setIsLoading] = useState(false);

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

  const startEditing = () => {
    setEditingName(receipt.merchant.name);
    setIsEditing(true);
  };

  const cancelEditing = () => {
    setIsEditing(false);
    setEditingName('');
  };

  const saveMerchantName = async () => {
    if (!receipt.merchant.id) {
      alert('ID магазина отсутствует');
      return;
    }
    
    try {
      setIsLoading(true);
      await updateMerchantName(receipt.merchant.id, editingName);
      // После успешного обновления перезагружаем страницу или обновляем данные
      window.location.reload();
    } catch (error) {
      console.error('Ошибка при обновлении имени магазина:', error);
      alert('Не удалось обновить имя магазина');
    } finally {
      setIsLoading(false);
    }
  };

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
          <strong>Магазин:</strong>
          {isEditing ? (
            <div className="merchant-edit-controls">
              <input
                type="text"
                value={editingName}
                onChange={(e) => setEditingName(e.target.value)}
                disabled={isLoading}
                className="merchant-name-input"
              />
              <button
                type="button"
                onClick={saveMerchantName}
                disabled={isLoading}
                className="save-merchant-btn"
              >
                {isLoading ? 'Сохранение...' : 'Сохранить'}
              </button>
              <button
                type="button"
                onClick={cancelEditing}
                disabled={isLoading}
                className="cancel-merchant-btn"
              >
                Отмена
              </button>
            </div>
          ) : (
            <div className="merchant-display">
              <span>{receipt.merchant.name}</span>
              {isAdmin && (
                <button
                  type="button"
                  onClick={startEditing}
                  className="edit-merchant-btn"
                >
                  Редактировать
                </button>
              )}
            </div>
          )}
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