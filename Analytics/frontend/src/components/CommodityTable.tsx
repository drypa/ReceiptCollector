import { useCallback, useState } from 'react';
import { updateCommodityCategory } from '../api/commodities';
import type { CommodityItem, Category } from '../types/commodity';
import type { DraftEntry } from '../hooks/useCommodityPageCategorization';
import { renderCategorySelectOptions } from '../utils/categoryOptions';
import { getDraftNote } from '../utils/categorizationSource';

interface CommodityTableProps {
  commodities: CommodityItem[];
  isAdmin: boolean;
  onReceiptClick: (receiptId: string) => void;
  onRefresh: () => void;
  /**
   * Справочник категорий поднят на уровень страницы (ADR-028 п. 9, T-12):
   * один fetch на страницу, используется и таблицей, и хуком режима проверки.
   */
  categories: Category[];
  /** Режим проверки страницы (phase 'review' | 'saving'): селект + подпись источника (C2/C8). */
  reviewMode?: boolean;
  /** Черновики строк по commodityId (значение — ключ enum, пустая строка = «не трогаем»). */
  drafts?: Record<string, DraftEntry>;
  onDraftChange?: (commodityId: string, value: string) => void;
  /** Точечный повтор строки — тот же suggest-эндпоинт с одним id (C2, ADR-028 п. 1.3). */
  onRetryRow?: (commodityId: string) => void;
  /** Повтор доступен только после завершения основного цикла порций (T-13). */
  retriesEnabled?: boolean;
  /** phase 'saving': черновик не редактируется, кнопки повтора недоступны (ADR-028 п. 5). */
  draftsLocked?: boolean;
  /** C5: в режиме проверки ссылка-дата (уход в чек) заблокирована с подсказкой. */
  controlsLocked?: boolean;
  /** Подсказка на заблокированных контролах (C5). */
  lockedTitle?: string;
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
 * Нужен ли строке точечный повтор (T-13): «не определена»/ошибка — сразу;
 * строка без результата (id не вернулся сервером) — после завершения цикла порций.
 */
function isRetryable(entry: DraftEntry | undefined, retriesEnabled: boolean): boolean {
  // Правила повтора (T-13): только строки без значения и без ручной правки —
  // повтор по определению не может затерять пользовательский ввод.
  if (!entry || entry.touched || entry.value !== '') {
    return false;
  }
  if (entry.source === 'undefined' || entry.error) {
    return true;
  }
  return !entry.source && retriesEnabled;
}

export function CommodityTable({
  commodities,
  isAdmin,
  onReceiptClick,
  onRefresh,
  categories,
  reviewMode = false,
  drafts,
  onDraftChange,
  onRetryRow,
  retriesEnabled = false,
  draftsLocked = false,
  controlsLocked = false,
  lockedTitle,
}: CommodityTableProps) {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [savingId, setSavingId] = useState<string | null>(null);

  // Как selectableCategories в «Деталях чека»: Undefined не выбирается руками.
  const selectableCategories = categories.filter((category) => category.key !== 'Undefined');

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
          {commodities.map((commodity) => {
            const draft = drafts?.[commodity.id];
            const note = draft ? getDraftNote(draft) : null;
            // Класс подписи: ручная правка визуально отделена от источника предложения;
            // иначе — источник (undefined, пока предложения нет).
            const noteSource = draft?.touched ? 'touched' : draft?.source ?? 'undefined';

            return (
              <tr key={commodity.id}>
                <td>{commodity.merchantName}</td>
                <td>
                  <button
                    type="button"
                    onClick={() => onReceiptClick(commodity.receiptId)}
                    className="date-link"
                    disabled={controlsLocked}
                    title={controlsLocked ? lockedTitle : undefined}
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
                    {reviewMode ? (
                      <div className="category-review">
                        <select
                          value={draft?.value ?? ''}
                          onChange={(e) => onDraftChange?.(commodity.id, e.target.value)}
                          disabled={draftsLocked}
                          className="item-category-select"
                          title={draft?.error ?? undefined}
                        >
                          <option value="">—</option>
                          {renderCategorySelectOptions(selectableCategories, (c) => c.key)}
                        </select>
                        {note && (
                          <div
                            className={`category-suggestion category-suggestion-${noteSource}`}
                            title={draft?.error ?? undefined}
                          >
                            {note}
                          </div>
                        )}
                        {isRetryable(draft, retriesEnabled) && (
                          <button
                            type="button"
                            className="retry-row-btn"
                            onClick={() => onRetryRow?.(commodity.id)}
                            disabled={draftsLocked}
                            title="Запросить предложение для этой строки ещё раз"
                          >
                            Повторить
                          </button>
                        )}
                      </div>
                    ) : savingId === commodity.id ? (
                      <span>Сохранение...</span>
                    ) : editingId === commodity.id ? (
                      <select
                        defaultValue={commodity.categoryId != null ? String(commodity.categoryId) : ''}
                        onChange={(e) => handleCategoryChange(commodity.id, e.target.value)}
                        onBlur={() => setEditingId(null)}
                        autoFocus
                      >
                        <option value="">—</option>
                        {renderCategorySelectOptions(categories)}
                      </select>
                    ) : (
                      <div className="category-display">
                        <span>{commodity.categoryName ?? '—'}</span>
                        <button
                          type="button"
                          className="edit-category-btn"
                          onClick={() => setEditingId(commodity.id)}
                          disabled={controlsLocked}
                          title={controlsLocked ? lockedTitle : 'Редактировать категорию'}
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
