import { useEffect, useState } from 'react';
import type { ReceiptDetails, ReceiptItem } from '../types/receipt';
import { useAdmin } from '../hooks/useAdmin';
import {
  updateMerchantName,
  updateMerchantDefaultCategory,
  applyMerchantDefaultCategory,
} from '../api/merchants';
import { suggestReceiptCategories, saveReceiptCategories } from '../api/receipts';
import type { ReceiptItemSuggestion, SaveCategoriesItem } from '../api/receipts';
import { fetchCategories } from '../api/commodities';
import type { Category } from '../types/commodity';
import { renderCategorySelectOptions } from '../utils/categoryOptions';
import { getSuggestionNote } from '../utils/categorizationSource';
import { CustomDialog } from './CustomDialog';
import { useToast } from './Toasts';

interface ReceiptDetailsProps {
  receipt: ReceiptDetails | null;
  onBack: () => void;
  onReceiptRefresh?: () => void;
}

// Подпись-источник предложения вынесена в utils/categorizationSource (ADR-028 п. 9) —
// общий источник для «Деталей чека» и страницы «Товары»; текст и поведение не изменились.

/**
 * Источники, которые журнал решений считает предложением категории (ADR-033 п. 3,
 * «Явно зафиксированные семантики»): `existing` (категория уже была у позиции) и
 * `undefined` (предложения нет) снимка не дают — по ним события не пишутся (FR-1/FR-5b).
 */
const SUGGESTION_SNAPSHOT_SOURCES: ReadonlySet<string> = new Set([
  'ai',
  'precategorized',
  'cache',
  'merchant-default',
]);

export function ReceiptDetails({ receipt, onBack, onReceiptRefresh }: ReceiptDetailsProps) {
  const { isAdmin } = useAdmin();
  const toast = useToast();
  const [isEditing, setIsEditing] = useState(false);
  const [editingName, setEditingName] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  // Т-10 (ADR-032, п. 7): настройка товарной категории по умолчанию (селект) и кнопка дожима
  // в блоке магазина. Отдельные состояния — не пересекаются с редактированием имени (isEditing).
  const [editingDefaultCategory, setEditingDefaultCategory] = useState(false);
  const [defaultCategorySaving, setDefaultCategorySaving] = useState(false);
  const [applyingExisting, setApplyingExisting] = useState(false);
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

  // Справочник категорий нужен и вне режима категоризации (отображение названий
  // в таблице товаров), поэтому загружаем его при открытии чека, а не только
  // внутри runCategorization.
  useEffect(() => {
    if (!receipt?.id) return;

    fetchCategories()
      .then(setCategories)
      .catch((error) => {
        console.error('Ошибка загрузки категорий:', error);
      });
  }, [receipt?.id]);

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

  /**
   * Сохранение/сброс товарной категории по умолчанию магазина (ADR-032, п. 6–7; FR-M1) —
   * пустое значение select'а («— не задана —») = null. После успеха — onReceiptRefresh:
   * поля receipt.merchant.defaultCommodityCategory* и видимость кнопки дожима обновляются
   * с сервера (прецеденты: MerchantTable T-09, onReceiptRefresh в saveSelectedCategories).
   */
  const saveDefaultCategory = async (selectValue: string) => {
    if (!receipt.merchant.id) {
      showDialog('Ошибка', 'ID магазина отсутствует');
      return;
    }

    const categoryId = selectValue === '' ? null : Number(selectValue);
    if (selectValue !== '' && !Number.isFinite(categoryId)) {
      return;
    }

    setEditingDefaultCategory(false);
    setDefaultCategorySaving(true);
    try {
      const result = await updateMerchantDefaultCategory(receipt.merchant.id, categoryId);
      toast.success(
        result.categoryName
          ? `Категория по умолчанию обновлена: ${result.categoryName}`
          : 'Категория по умолчанию снята',
      );
      onReceiptRefresh?.();
    } catch (error) {
      console.error('Ошибка обновления категории по умолчанию:', error);
      toast.error(
        error instanceof Error ? error.message : 'Не удалось обновить категорию по умолчанию',
      );
    } finally {
      setDefaultCategorySaving(false);
    }
  };

  /**
   * Дожим категорией по умолчанию существующих позиций магазина без категории
   * (ADR-032, п. 4; FR-M3) — позиции всех пользователей (E1), содержательные категории
   * не перезаписываются. updated = 0 — успех (идемпотентность, повторное нажатие):
   * информационный тост без перезагрузки чека (данные не изменились). Ошибки
   * (401/403/404/400/сеть) — toast.error русским сообщением API-клиента
   * (прецедент saveSelectedCategories).
   */
  const applyExistingCategories = async () => {
    if (!receipt.merchant.id) {
      showDialog('Ошибка', 'ID магазина отсутствует');
      return;
    }

    setApplyingExisting(true);
    try {
      const { updated } = await applyMerchantDefaultCategory(receipt.merchant.id);
      if (updated === 0) {
        toast.success('Все позиции уже категоризированы');
      } else {
        toast.success(`Обновлено товаров: ${updated}`);
        onReceiptRefresh?.();
      }
    } catch (error) {
      console.error('Ошибка категоризации существующих товаров:', error);
      toast.error(
        error instanceof Error ? error.message : 'Не удалось категоризовать существующие товары',
      );
    } finally {
      setApplyingExisting(false);
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

  /** Категории справочника, доступные для выбора: «Не указана» (Undefined) исключена — это не категория. */
  const selectableCategories = categories.filter((c) => c.key !== 'Undefined');

  const saveSelectedCategories = async () => {
    setSavingCategories(true);
    try {
      // Снимок «что человек видел на экране» (ADR-033 п. 3, решение A1) — аддитивные поля
      // элемента сохранения. Без suggest снимка нет: payload побайтово прежний (FR-5, NFR-2).
      // Источник истины «Undefined — не категория» — selectableCategories (как и в select'ах).
      const categoryKeyById = new Map(
        selectableCategories.map((category) => [category.id, category.key]),
      );
      const suggestionByCommodityId = new Map(
        (suggestions ?? []).map((suggestion) => [suggestion.commodityId, suggestion]),
      );

      const items: SaveCategoriesItem[] = receipt.items.map((item) => {
        const saved: SaveCategoriesItem = {
          commodityId: item.id,
          category: selectedCategories[item.id] || null,
        };

        const suggestion = suggestionByCommodityId.get(item.id);
        if (
          !suggestion ||
          suggestion.categoryId == null ||
          !SUGGESTION_SNAPSHOT_SOURCES.has(suggestion.source)
        ) {
          return saved;
        }

        const suggestedCategory = categoryKeyById.get(suggestion.categoryId);
        if (!suggestedCategory) {
          return saved;
        }

        // Снимок не зависит от выбранной категории: при сбросе («—») он тоже уходит —
        // сервер запишет событие с final = Undefined (edge case задачи).
        return {
          ...saved,
          suggestedCategory,
          suggestionSource: suggestion.source,
        };
      });

      const result = await saveReceiptCategories(receipt.id, items);
      setSuggestions(null);
      setSelectedCategories({});
      onReceiptRefresh?.();
      toast.success(`Сохранено категорий: ${result.updated}`);
    } catch (error) {
      console.error('Ошибка сохранения категорий:', error);
      toast.error(
        error instanceof Error ? error.message : 'Не удалось сохранить категории товаров'
      );
    } finally {
      setSavingCategories(false);
    }
  };

  /**
   * Дефолт магазина задан (ADR-032, п. 7): сервер отдаёт валидный код (не NULL и не Undefined —
   * безопасное чтение A1). Кнопка «Категоризировать существующие» показывается только при
   * админе и этом условии (иначе apply гарантированно ответил бы 400).
   */
  const hasDefaultCommodityCategory = Boolean(receipt.merchant.defaultCommodityCategoryId);

  const categoryById = new Map(categories.map((c) => [c.id, c]));

  /** Название категории товара по справочнику; null — категория не указана (null/0/undefined) или не найдена. */
  const categoryNameForItem = (item: ReceiptItem): string | null => {
    if (item.categoryId == null || item.categoryId === 0) return null;
    return categoryById.get(item.categoryId)?.name ?? null;
  };

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
              {isAdmin && (
                <div className="merchant-default-category">
                  <span className="merchant-default-category-label">
                    Категория товаров по умолчанию:
                  </span>
                  {defaultCategorySaving ? (
                    <span className="merchant-default-category-value">Сохранение...</span>
                  ) : editingDefaultCategory ? (
                    <select
                      defaultValue={
                        receipt.merchant.defaultCommodityCategoryId === null
                          ? ''
                          : String(receipt.merchant.defaultCommodityCategoryId)
                      }
                      onChange={(e) => saveDefaultCategory(e.target.value)}
                      onBlur={() => setEditingDefaultCategory(false)}
                      autoFocus
                      aria-label="Категория товаров по умолчанию"
                    >
                      <option value="">— не задана —</option>
                      {renderCategorySelectOptions(selectableCategories)}
                    </select>
                  ) : (
                    <span className="merchant-default-category-value">
                      {receipt.merchant.defaultCommodityCategoryName ?? 'Не задана'}
                    </span>
                  )}
                  {!editingDefaultCategory && !defaultCategorySaving && (
                    <button
                      type="button"
                      className="edit-category-btn"
                      onClick={() => setEditingDefaultCategory(true)}
                      title="Редактировать категорию товаров по умолчанию"
                    >
                      ред.
                    </button>
                  )}
                  {hasDefaultCommodityCategory && (
                    <button
                      type="button"
                      className="secondary"
                      onClick={applyExistingCategories}
                      disabled={applyingExisting || defaultCategorySaving}
                    >
                      {applyingExisting ? 'Категоризация...' : 'Категоризовать существующие'}
                    </button>
                  )}
                </div>
              )}
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
          <div className="table-wrapper">
            <table className="items-table">
              <thead>
                <tr>
                  <th>Название</th>
                  <th>Количество</th>
                  <th>Цена за единицу</th>
                  <th>Общая цена</th>
                  <th>Категория</th>
                </tr>
              </thead>
              <tbody>
                {receipt.items.map((item, index) => {
                  const suggestion = suggestionByItem(item);
                  const note = suggestion ? getSuggestionNote(suggestion.source) : null;
                  const categoryName = categoryNameForItem(item);
  
                  return (
                    <tr key={`${item.id}-${index}`}>
                      <td>{item.name}</td>
                      <td>{item.quantity}</td>
                      <td>{currencyFormatter.format(item.unitPrice)}</td>
                      <td>{currencyFormatter.format(item.totalPrice)}</td>
                      {categorizeModeActive ? (
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
                      ) : (
                        <td>
                          <span
                            className={
                              categoryName
                                ? 'item-category-name'
                                : 'item-category-name item-category-name--empty'
                            }
                          >
                            {categoryName ?? '—'}
                          </span>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
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