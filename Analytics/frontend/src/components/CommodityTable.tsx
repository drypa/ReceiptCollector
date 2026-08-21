import { useCallback, useEffect, useState } from 'react';
import { fetchCategories, updateCommodityCategory } from '../api/commodities';
import type { CommodityItem, Category } from '../types/commodity';

interface CommodityTableProps {
  commodities: CommodityItem[];
  isAdmin: boolean;
  onReceiptClick: (receiptId: string) => void;
  onRefresh: () => void;
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

/**
 * Рендер опций категорий с группировкой по полю group (решение D1 ADR 010).
 * Категории с пустой/отсутствующей группой (старые категории 0–17 и Other=255)
 * выводятся плоским списком — прямые дети <select> (у <optgroup> обязателен
 * непустой label). Порядок групп — по порядку первого появления в массиве.
 */
function renderCategoryOptions(categories: Category[]) {
  const grouped = new Map<string, Category[]>();

  for (const cat of categories) {
    const group = cat.group ?? '';
    const bucket = grouped.get(group) ?? [];
    bucket.push(cat);
    grouped.set(group, bucket);
  }

  return Array.from(grouped.entries()).map(([group, items]) => {
    const options = items.map((cat) => (
      <option key={cat.id} value={String(cat.id)}>
        {cat.name}
      </option>
    ));

    if (group === '') {
      return options;
    }

    return (
      <optgroup key={group} label={group}>
        {options}
      </optgroup>
    );
  });
}

export function CommodityTable({ commodities, isAdmin, onReceiptClick, onRefresh }: CommodityTableProps) {
  const [categories, setCategories] = useState<Category[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);

  useEffect(() => {
    if (isAdmin) {
      fetchCategories()
        .then(setCategories)
        .catch((err) => console.error('Ошибка загрузки категорий:', err));
    }
  }, [isAdmin]);

  const handleEditStart = useCallback((commodityId: string) => {
    setEditingId(commodityId);
  }, []);

  const handleCategoryChange = useCallback(async (commodityId: string, categoryId: string) => {
    const value = categoryId === '' ? null : Number(categoryId);
    setSavingId(commodityId);
    setEditingId(null);

    try {
      await updateCommodityCategory(commodityId, value);
      onRefresh();
    } catch (err) {
      console.error('Ошибка обновления категории:', err);
    } finally {
      setSavingId(null);
    }
  }, [onRefresh]);

  if (commodities.length === 0) {
    return (
      <div className="empty-state">
        <p>Товары не найдены.</p>
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
            <th>Название товара</th>
            <th>Количество</th>
            <th>Цена за ед.</th>
            <th>Стоимость</th>
            {isAdmin && <th>Категория</th>}
          </tr>
        </thead>
        <tbody>
          {commodities.map((commodity) => (
            <tr key={commodity.id}>
              <td>{commodity.merchantName}</td>
              <td>
                <button
                  type="button"
                  onClick={() => onReceiptClick(commodity.receiptId)}
                  className="date-link"
                >
                  {dateFormatter.format(new Date(commodity.purchasedAt))}
                </button>
              </td>
              <td>{commodity.name}</td>
              <td>{commodity.quantity}</td>
              <td>{currencyFormatter.format(commodity.unitPrice)}</td>
              <td>{currencyFormatter.format(commodity.totalPrice)}</td>
              {isAdmin && (
                <td>
                  {savingId === commodity.id ? (
                    <span>Сохранение...</span>
                  ) : editingId === commodity.id ? (
                    <select
                      defaultValue={commodity.categoryId != null ? String(commodity.categoryId) : ''}
                      onChange={(e) => handleCategoryChange(commodity.id, e.target.value)}
                      onBlur={() => setEditingId(null)}
                      autoFocus
                    >
                      <option value="">—</option>
                      {renderCategoryOptions(categories)}
                    </select>
                  ) : (
                    <div className="category-display">
                      <span>{commodity.categoryName ?? '—'}</span>
                      <button
                        type="button"
                        className="edit-category-btn"
                        onClick={() => handleEditStart(commodity.id)}
                        title="Редактировать категорию"
                      >
                        ред.
                      </button>
                    </div>
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
