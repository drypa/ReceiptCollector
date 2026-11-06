import { useCallback, useEffect, useState } from 'react';
import type { KeyboardEvent } from 'react';
import { Link } from 'react-router-dom';
import {
  fetchMerchantCategories,
  updateMerchantCategory,
  updateMerchantDefaultCategory,
  updateMerchantName,
} from '../api/merchants';
import type { MerchantDto } from '../api/merchants';
import { fetchCategories } from '../api/commodities';
import { merchantReceiptsPath } from '../routes';
import type { Category } from '../types/commodity';
import { renderCategorySelectOptions } from '../utils/categoryOptions';
import { useToast } from './Toasts';

interface MerchantTableProps {
  merchants: MerchantDto[];
  isAdmin: boolean;
  onRefresh: () => void;
}

export function MerchantTable({ merchants, isAdmin, onRefresh }: MerchantTableProps) {
  const toast = useToast();
  const [categories, setCategories] = useState<Category[]>([]);
  // Товарный справочник (CommodityCategory) для колонки дефолтной категории;
  // отдельный от `categories` (MerchantCategory) — справочники независимы (FR-M1, ADR-032).
  const [commodityCategories, setCommodityCategories] = useState<Category[]>([]);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [editingNameId, setEditingNameId] = useState<string | null>(null);
  const [nameDraft, setNameDraft] = useState('');
  const [nameError, setNameError] = useState<string | null>(null);
  // Отдельные состояния редактирования дефолтной категории (ADR-032, п. 7):
  // не пересекаются с editingId/editingNameId — одновременно в строке редактируется одно поле.
  const [editingDefaultId, setEditingDefaultId] = useState<string | null>(null);
  const [defaultSavingId, setDefaultSavingId] = useState<string | null>(null);

  useEffect(() => {
    if (isAdmin) {
      fetchMerchantCategories()
        .then(setCategories)
        .catch((err) => console.error('Ошибка загрузки категорий магазинов:', err));
    }
  }, [isAdmin]);

  // Товарный справочник нужен только колонке дефолта (админ); Undefined нельзя задать
  // дефолтом (CHECK в схеме и валидация API), поэтому он исключается из select'а —
  // прецедент PrecategorizedCommodityTable.
  useEffect(() => {
    if (!isAdmin) {
      return;
    }

    fetchCategories()
      .then((all) => setCommodityCategories(all.filter((category) => category.key !== 'Undefined')))
      .catch((err) => console.error('Ошибка загрузки категорий товаров:', err));
  }, [isAdmin]);

  const handleEditStart = useCallback((merchantId: string) => {
    setEditingNameId(null);
    setEditingDefaultId(null);
    setEditingId(merchantId);
  }, []);

  const handleNameEditStart = useCallback((merchant: MerchantDto) => {
    setNameDraft(merchant.name);
    setNameError(null);
    setEditingNameId(merchant.id);
    setEditingId(null);
    setEditingDefaultId(null);
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

  const handleDefaultEditStart = useCallback((merchantId: string) => {
    setEditingId(null);
    setEditingNameId(null);
    setEditingDefaultId(merchantId);
  }, []);

  /**
   * Сохранение дефолтной товарной категории (ADR-032, п. 6–7; FR-M1): пустое значение
   * select'а («— не задана —») = сброс в null; прочее — код из товарного справочника.
   * Тост успеха/ошибки — стиль редактирования строки PrecategorizedCommodityTable;
   * русские тексты 401/403/404/400 формирует API-клиент.
   */
  const handleDefaultCategoryChange = useCallback(
    async (merchantId: string, selectValue: string) => {
      const categoryId = selectValue === '' ? null : Number(selectValue);
      if (selectValue !== '' && !Number.isFinite(categoryId)) {
        return;
      }

      setEditingDefaultId(null);
      setDefaultSavingId(merchantId);

      try {
        const result = await updateMerchantDefaultCategory(merchantId, categoryId);
        toast.success(
          result.categoryName
            ? `Категория по умолчанию обновлена: ${result.categoryName}`
            : 'Категория по умолчанию снята',
        );
        onRefresh();
      } catch (err) {
        console.error('Ошибка обновления категории по умолчанию:', err);
        toast.error(
          err instanceof Error ? err.message : 'Не удалось обновить категорию по умолчанию',
        );
      } finally {
        setDefaultSavingId(null);
      }
    },
    [onRefresh, toast],
  );

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
            {isAdmin && <th>Категория товаров (по умолчанию)</th>}
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
                      <Link to={merchantReceiptsPath(merchant.id)} className="merchant-link">
                        {merchant.name}
                      </Link>
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
                {isAdmin && (
                  <td>
                    {defaultSavingId === merchant.id ? (
                      <span>Сохранение...</span>
                    ) : editingDefaultId === merchant.id ? (
                      <select
                        defaultValue={
                          merchant.defaultCommodityCategoryId === null
                            ? ''
                            : String(merchant.defaultCommodityCategoryId)
                        }
                        onChange={(e) => handleDefaultCategoryChange(merchant.id, e.target.value)}
                        onBlur={() => setEditingDefaultId(null)}
                        autoFocus
                        aria-label={`Категория товаров по умолчанию для «${merchant.name}»`}
                      >
                        <option value="">— не задана —</option>
                        {renderCategorySelectOptions(commodityCategories)}
                      </select>
                    ) : (
                      <div className="category-display">
                        <span>{merchant.defaultCommodityCategoryName ?? 'Не задана'}</span>
                        <button
                          type="button"
                          className="edit-category-btn"
                          onClick={() => handleDefaultEditStart(merchant.id)}
                          title="Редактировать категорию товаров по умолчанию"
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
