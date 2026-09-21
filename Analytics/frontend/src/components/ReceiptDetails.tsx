import { useState } from 'react';
import type { ReceiptDetails, ReceiptItem } from '../types/receipt';
import { useAdmin } from '../hooks/useAdmin';
import { updateMerchantName } from '../api/merchants';
import { suggestReceiptCategories, saveReceiptCategories } from '../api/receipts';
import type { ReceiptItemSuggestion } from '../api/receipts';
import { fetchCategories } from '../api/commodities';
import type { Category } from '../types/commodity';
import { renderCategorySelectOptions } from '../utils/categoryOptions';
import { CustomDialog } from './CustomDialog';

interface ReceiptDetailsProps {
  receipt: ReceiptDetails | null;
  onBack: () => void;
  onReceiptRefresh?: () => void;
}

/** Источник «уже присвоена» — товар уже имел категорию в чеке, она не меняется. */
function getSuggestionNote(source: ReceiptItemSuggestion['source']): string | null {
  switch (source) {
    case 'cache':
      return 'по прошлым чекам';
    case 'ai':
      return 'предложено ИИ';
    case 'existing':
      return 'уже присвоена';
    case 'undefined':
      return 'не определена';
    default:
      return null;
  }
}

export function ReceiptDetails({ receipt, onBack, onReceiptRefresh }: ReceiptDetailsProps) {
  const { isAdmin } = useAdmin();
  const [isEditing, setIsEditing] = useState(false);
  const [editingName, setEditingName] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [suggestions, setSuggestions] = useState<ReceiptItemSuggestion[] | null>(null);
  const [suggesting, setSuggesting] = useState(false);
  const [savingCategories, setSavingCategories] = useState(false);
  const [categories, setCategories] = useState<Category[]>([]);
  const [selectedCategories, setSelectedCategories] = useState<Record<string, string>>({});
  const [dialog, setDialog] = useState({
    isOpen: false,
    title: '',
    message: '',
    onConfirm: null as (() => void) | null
  });

  const showDialog = (title: string, message: string, onConfirm?: () => void) => {
    setDialog({
      isOpen: true,
      title,
      message,
      onConfirm: onConfirm || null
    });
  };

  const closeDialog = () => {
    setDialog({
      ...dialog,
      isOpen: false
    });
  };

  if (!receipt) {
    return (
      <div className="receipt-details">
        <h2>Детали чека</h2>
        <p>Чек не найден.</p>
        <button type="button" onClick={onBack} className="back-button">
          Назад к списку чеков
        </button>
        <CustomDialog
          isOpen={dialog.isOpen}
          title={dialog.title}
          message={dialog.message}
          onClose={closeDialog}
          onConfirm={dialog.onConfirm || undefined}
          confirmText={dialog.onConfirm ? "Ок" : "Закрыть"}
        />
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
      showDialog('Ошибка', 'ID магазина отсутствует');
      return;
    }
    
    try {
      setIsLoading(true);
      await updateMerchantName(receipt.merchant.id, editingName);
      // После успешного обновления перезагружаем страницу или обновляем данные
      window.location.reload();
    } catch (error) {
      console.error('Ошибка при обновлении имени магазина:', error);
      showDialog('Ошибка', 'Не удалось обновить имя магазина');
    } finally {
      setIsLoading(false);
    }
  };

  /** Запуск автоматической категоризации: категории справочника + AI-предложения (ничего не сохраняет). */
  const runCategorization = async () => {
    setSuggesting(true);
    try {
      let allCategories = categories;
      if (allCategories.length === 0) {
        allCategories = await fetchCategories();
        setCategories(allCategories);
      }

      const result = await suggestReceiptCategories(receipt.id);

      const byId = new Map(allCategories.map((c) => [c.id, c]));
      const initialSelected: Record<string, string> = {};
      for (const item of result.items) {
        const category = item.categoryId != null ? byId.get(item.categoryId) : undefined;
        initialSelected[item.commodityId] = category ? category.key : '';
      }

      setSuggestions(result.items);
      setSelectedCategories(initialSelected);
    } catch (error) {
      console.error('Ошибка категоризации:', error);
      showDialog('Ошибка', error instanceof Error ? error.message : 'Не удалось определить категории товаров');
    } finally {
      setSuggesting(false);
    }
  };

  const cancelCategorization = () => {
    setSuggestions(null);
    setSelectedCategories({});
  };

  const saveSelectedCategories = async () => {
    setSavingCategories(true);
    try {
      const items = receipt.items.map((item) => ({
        commodityId: item.id,
        category: selectedCategories[item.id] || null,
      }));

      const result = await saveReceiptCategories(receipt.id, items);
      showDialog('Готово', `Сохранено категорий: ${result.updated}.`, () => {
        setSuggestions(null);
        setSelectedCategories({});
        onReceiptRefresh?.();
      });
    } catch (error) {
      console.error('Ошибка сохранения категорий:', error);
      showDialog('Ошибка', error instanceof Error ? error.message : 'Не удалось сохранить категории товаров');
    } finally {
      setSavingCategories(false);
    }
  };

  /** Категории справочника, доступные для выбора: «Не указана» (Undefined) исключена — это не категория. */
  const selectableCategories = categories.filter((c) => c.key !== 'Undefined');

  const suggestionByItem = (item: ReceiptItem) =>
    suggestions?.find((s) => s.commodityId === item.id) ?? null;

  const categorizeModeActive = suggestions !== null;

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
                className="save-merchant-btn secondary"
              >
                {isLoading ? 'Сохранение...' : 'Сохранить'}
              </button>
              <button
                type="button"
                onClick={cancelEditing}
                disabled={isLoading}
                className="cancel-merchant-btn secondary"
              >
                Отмена
              </button>
            </div>
          ) : (
            <div className="merchant-display">
              <div className="merchant-name-edit">
                <span className="merchant-name">{receipt.merchant.name}</span>
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
            </div>
          )}
        </div>
        {receipt.merchant.inn && (
          <div className="summary-item">
            <strong>ИНН:</strong> {receipt.merchant.inn}
          </div>
        )}
        <div className="summary-item">
          <strong>Дата покупки:</strong> {dateFormatter.format(new Date(receipt.purchasedAt))}
        </div>
        <div className="summary-item">
          <strong>Сумма:</strong> {currencyFormatter.format(receipt.totalAmount)}
        </div>
      </div>

      <div className="receipt-items">
        <div className="receipt-items-header">
          <h3>Товары</h3>
          {receipt.items.length > 0 && (
            <div className="categorization-controls">
              {!categorizeModeActive ? (
                <button
                  type="button"
                  onClick={runCategorization}
                  disabled={suggesting}
                  className="secondary"
                >
                  {suggesting ? 'Определяем категории...' : 'Категоризировать'}
                </button>
              ) : (
                <>
                  <button
                    type="button"
                    onClick={saveSelectedCategories}
                    disabled={savingCategories}
                    className="primary"
                  >
                    {savingCategories ? 'Сохранение...' : 'Сохранить категории'}
                  </button>
                  <button
                    type="button"
                    onClick={cancelCategorization}
                    disabled={savingCategories}
                    className="secondary"
                  >
                    Отмена
                  </button>
                </>
              )}
            </div>
          )}
        </div>
        {receipt.items.length > 0 ? (
          <table className="items-table">
            <thead>
              <tr>
                <th>Название</th>
                <th>Количество</th>
                <th>Цена за единицу</th>
                <th>Общая цена</th>
                {categorizeModeActive && <th>Категория</th>}
              </tr>
            </thead>
            <tbody>
              {receipt.items.map((item, index) => {
                const suggestion = suggestionByItem(item);
                const note = suggestion ? getSuggestionNote(suggestion.source) : null;

                return (
                  <tr key={`${item.id}-${index}`}>
                    <td>{item.name}</td>
                    <td>{item.quantity}</td>
                    <td>{currencyFormatter.format(item.unitPrice)}</td>
                    <td>{currencyFormatter.format(item.totalPrice)}</td>
                    {categorizeModeActive && (
                      <td>
                        <select
                          value={selectedCategories[item.id] ?? ''}
                          onChange={(e) =>
                            setSelectedCategories((prev) => ({
                              ...prev,
                              [item.id]: e.target.value,
                            }))
                          }
                          disabled={savingCategories}
                          className="item-category-select"
                          title={suggestion?.error ?? undefined}
                        >
                          <option value="">—</option>
                          {renderCategorySelectOptions(selectableCategories, (c) => c.key)}
                        </select>
                        {note && (
                          <div
                            className={`category-suggestion category-suggestion-${
                              suggestion?.source ?? 'undefined'
                            }`}
                          >
                            {note}
                          </div>
                        )}
                      </td>
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
        ) : (
          <p>Товары не найдены.</p>
        )}
      </div>
      
      <CustomDialog
        isOpen={dialog.isOpen}
        title={dialog.title}
        message={dialog.message}
        onClose={closeDialog}
        onConfirm={dialog.onConfirm || undefined}
        confirmText={dialog.onConfirm ? "Ок" : "Закрыть"}
      />
    </div>
  );
}