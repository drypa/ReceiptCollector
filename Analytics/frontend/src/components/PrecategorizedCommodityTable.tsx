import { useCallback, useEffect, useState } from 'react';
import {
  confirmPrecategorized,
  declinePrecategorized,
  updatePrecategorizedCategory,
} from '../api/precategorizedCommodities';
import { fetchCategories } from '../api/commodities';
import type { Category } from '../types/commodity';
import type { PrecategorizedCommodityItem } from '../types/precategorizedCommodity';
import { renderCategorySelectOptions } from '../utils/categoryOptions';
import { CustomDialog } from './CustomDialog';
import { useToast } from './Toasts';

interface PrecategorizedCommodityTableProps {
  items: PrecategorizedCommodityItem[];
  onRefresh: () => void;
}

const dateFormatter = new Intl.DateTimeFormat('ru-RU', {
  dateStyle: 'short',
  timeStyle: 'short',
});

export function PrecategorizedCommodityTable({ items, onRefresh }: PrecategorizedCommodityTableProps) {
  const toast = useToast();
  const [categories, setCategories] = useState<Category[]>([]);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [declineTarget, setDeclineTarget] = useState<PrecategorizedCommodityItem | null>(null);

  useEffect(() => {
    fetchCategories()
      .then((all) => setCategories(all.filter((category) => category.key !== 'Undefined')))
      .catch((error) => console.error('Ошибка загрузки категорий товаров:', error));
  }, []);

  const handleCategoryChange = useCallback(
    async (item: PrecategorizedCommodityItem, categoryId: string) => {
      const value = Number(categoryId);
      if (!Number.isFinite(value)) {
        return;
      }

      setSavingId(item.id);
      try {
        await updatePrecategorizedCategory(item.id, value);
        toast.success('Категория предложения обновлена');
        onRefresh();
      } catch (error) {
        toast.error(error instanceof Error ? error.message : 'Не удалось изменить категорию');
      } finally {
        setSavingId(null);
      }
    },
    [onRefresh, toast],
  );

  const handleConfirm = useCallback(
    async (item: PrecategorizedCommodityItem) => {
      setSavingId(item.id);
      try {
        const result = await confirmPrecategorized(item.id);
        toast.success(`Подтверждено. Обновлено товаров: ${result.updatedCommodities}`);
        onRefresh();
      } catch (error) {
        toast.error(error instanceof Error ? error.message : 'Не удалось подтвердить предложение');
      } finally {
        setSavingId(null);
      }
    },
    [onRefresh, toast],
  );

  const handleDeclineConfirmed = useCallback(async () => {
    if (!declineTarget) {
      return;
    }

    const target = declineTarget;
    setDeclineTarget(null);
    setSavingId(target.id);
    try {
      await declinePrecategorized(target.id);
      toast.success('Предложение отклонено');
      onRefresh();
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'Не удалось отклонить предложение');
    } finally {
      setSavingId(null);
    }
  }, [declineTarget, onRefresh, toast]);

  if (items.length === 0) {
    return (
      <div className="empty-state">
        <p>Нет предложений.</p>
      </div>
    );
  }

  return (
    <>
      <div className="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Наименование</th>
              <th>Магазин</th>
              <th>Предложенная категория</th>
              <th>Создано</th>
              <th>Действия</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => {
              const isSaving = savingId === item.id;
              return (
                <tr key={item.id}>
                  <td>{item.name}</td>
                  <td>{item.merchantName ?? '—'}</td>
                  <td>
                    <select
                      value={String(item.proposedCategoryId)}
                      onChange={(event) => handleCategoryChange(item, event.target.value)}
                      disabled={isSaving}
                      aria-label={`Категория для «${item.name}»`}
                    >
                      {renderCategorySelectOptions(categories)}
                    </select>
                  </td>
                  <td>{formatDate(item.createdAt)}</td>
                  <td>
                    <div className="category-display">
                      <button
                        type="button"
                        onClick={() => handleConfirm(item)}
                        disabled={isSaving}
                        aria-label={`Подтвердить категорию для «${item.name}»`}
                      >
                        Подтвердить
                      </button>
                      <button
                        type="button"
                        className="secondary"
                        onClick={() => setDeclineTarget(item)}
                        disabled={isSaving}
                        aria-label={`Отклонить предложение для «${item.name}»`}
                      >
                        Отклонить
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <CustomDialog
        isOpen={declineTarget !== null}
        title="Отклонить предложение?"
        message={
          declineTarget
            ? `Предложение для «${declineTarget.name}» будет удалено. Категории товаров не изменятся.`
            : ''
        }
        confirmText="Отклонить"
        onConfirm={handleDeclineConfirmed}
        onClose={() => setDeclineTarget(null)}
      />
    </>
  );
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter.format(date);
}
