import { useCallback, useEffect, useState } from 'react';
import type { KeyboardEvent } from 'react';
import {
  fetchMerchantCategories,
  updateMerchantCategory,
  updateMerchantName,
} from '../api/merchants';
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
  const [editingNameId, setEditingNameId] = useState<string | null>(null);
  const [nameDraft, setNameDraft] = useState('');
  const [nameError, setNameError] = useState<string | null>(null);

  useEffect(() => {
    if (isAdmin) {
      fetchMerchantCategories()
        .then(setCategories)
        .catch((err) => console.error('Ошибка загрузки категорий магазинов:', err));
    }
  }, [isAdmin]);

  const handleEditStart = useCallback((merchantId: string) => {
    setEditingNameId(null);
    setEditingId(merchantId);
  }, []);

  const handleNameEditStart = useCallback((merchant: MerchantDto) => {
    setNameDraft(merchant.name);
    setNameError(null);
    setEditingNameId(merchant.id);
    setEditingId(null);
  }, []);

  const handleNameSave = useCallback(
    async (merchantId: string) => {
      const trimmed = nameDraft.trim();
      if (!trimmed) {
        setNameError('Имя магазина не может быть пустым');
        return;
      }

      if (trimmed.length > 256) {
        setNameError('Имя не должно превышать 256 символов');
        return;
      }

      setSavingId(merchantId);
      setNameError(null);

      try {
        await updateMerchantName(merchantId, trimmed);
        setEditingNameId(null);
        onRefresh();
      } catch (err) {
        console.error('Ошибка обновления имени магазина:', err);
        setNameError('Не удалось обновить имя магазина. Попробуйте ещё раз.');
      } finally {
        setSavingId(null);
      }
    },
    [nameDraft, onRefresh],
  );

  const handleNameCancel = useCallback(() => {
    setEditingNameId(null);
    setNameDraft('');
    setNameError(null);
  }, []);

  const handleNameKeyDown = useCallback(
    (e: KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Escape') {
        handleNameCancel();
      }
    },
    [handleNameCancel],
  );

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
                <td>
                  {savingId === merchant.id ? (
                    <span>Сохранение...</span>
                  ) : editingNameId === merchant.id ? (
                    <div>
                      <div className="merchant-edit-controls">
                        <input
                          type="text"
                          value={nameDraft}
                          onChange={(e) => setNameDraft(e.target.value)}
                          maxLength={256}
                          autoFocus
                          disabled={savingId === merchant.id}
                          className="merchant-name-input"
                          onKeyDown={handleNameKeyDown}
                        />
                        <button
                          type="button"
                          onClick={() => handleNameSave(merchant.id)}
                          disabled={savingId === merchant.id}
                          className="save-merchant-btn secondary"
                        >
                          Сохранить
                        </button>
                        <button
                          type="button"
                          onClick={handleNameCancel}
                          disabled={savingId === merchant.id}
                          className="cancel-merchant-btn secondary"
                        >
                          Отмена
                        </button>
                      </div>
                      {nameError && (
                        <span className="form-error" role="alert">
                          {nameError}
                        </span>
                      )}
                    </div>
                  ) : (
                    <div className="merchant-name-edit">
                      <span>{merchant.name}</span>
                      {isAdmin && (
                        <button
                          type="button"
                          className="edit-category-btn"
                          onClick={() => handleNameEditStart(merchant)}
                          title="Редактировать имя"
                        >
                          ред.
                        </button>
                      )}
                    </div>
                  )}
                </td>
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
