import { useCallback, useEffect, useState } from 'react';
import { fetchMerchantCategories, updateMerchantCategory } from '../api/merchants';
import type { MerchantDto } from '../api/merchants';
import type { Category } from '../types/commodity';

interface MerchantTableProps {
  merchants: MerchantDto[];
  isAdmin: boolean;
  onRefresh: () => void;
}

export function MerchantTable({ merchants, isAdmin, onRefresh }: MerchantTableProps) {
  const [categories, setCategories] = useState<Category[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);

  useEffect(() => {
    if (isAdmin) {
      fetchMerchantCategories()
        .then(setCategories)
        .catch((err) => console.error('Ошибка загрузки категорий магазинов:', err));
    }
  }, [isAdmin]);

  const handleEditStart = useCallback((merchantId: string) => {
    setEditingId(merchantId);
  }, []);

  const handleCategoryChange = useCallback(async (merchantId: string, categoryId: string) => {
    const value = Number(categoryId);
    setSavingId(merchantId);
    setEditingId(null);

    try {
      await updateMerchantCategory(merchantId, value);
      onRefresh();
    } catch (err) {
      console.error('Ошибка обновления категории магазина:', err);
    } finally {
      setSavingId(null);
    }
  }, [onRefresh]);

  if (merchants.length === 0) {
    return (
      <div className="empty-state">
        <p>Магазины не найдены.</p>
      </div>
    );
  }

  return (
    <div className="table-wrapper">
      <table>
        <thead>
          <tr>
            <th>Название</th>
            <th>Адрес</th>
            <th>ИНН</th>
            {isAdmin && <th>Категория</th>}
          </tr>
        </thead>
        <tbody>
          {merchants.map((merchant) => {
            const currentCategory = categories.find((cat) => cat.id === merchant.category);
            return (
              <tr key={merchant.id}>
                <td>{merchant.name}</td>
                <td>{merchant.address ?? '—'}</td>
                <td>{merchant.inn ?? '—'}</td>
                {isAdmin && (
                  <td>
                    {savingId === merchant.id ? (
                      <span>Сохранение...</span>
                    ) : editingId === merchant.id ? (
                      <select
                        defaultValue={String(merchant.category)}
                        onChange={(e) => handleCategoryChange(merchant.id, e.target.value)}
                        onBlur={() => setEditingId(null)}
                        autoFocus
                      >
                        {categories.map((cat) => (
                          <option key={cat.id} value={String(cat.id)}>
                            {cat.name}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <div className="category-display">
                        <span>{currentCategory?.name ?? 'Не указана'}</span>
                        <button
                          type="button"
                          className="edit-category-btn"
                          onClick={() => handleEditStart(merchant.id)}
                          title="Редактировать категорию"
                        >
                          ред.
                        </button>
                      </div>
                    )}
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
