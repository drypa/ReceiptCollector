import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePageSize } from '../contexts/PageSizeContext';
import { useCommodities } from '../hooks/useCommodities';
import { useCommodityPageCategorization } from '../hooks/useCommodityPageCategorization';
import { useAdmin } from '../hooks/useAdmin';
import { fetchCategories } from '../api/commodities';
import { useToast } from './Toasts';
import { Pagination } from './Pagination';
import { CommodityTable } from './CommodityTable';
import type { Category, CommodityCategoryFilter } from '../types/commodity';

const CATEGORY_FILTER_LABELS: Array<{ value: CommodityCategoryFilter; label: string }> = [
  { value: 'any', label: 'Все товары' },
  { value: 'uncategorized', label: 'Без категории' },
  { value: 'undefined', label: 'Категория «Не указана»' },
];

/** Подсказка на всех заблокированных в режиме проверки контролах (C5). */
const CONTROLS_LOCKED_TITLE = 'Сначала сохраните или отмените изменения';

export function CommoditiesPage() {
  const navigate = useNavigate();
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();
  const { isAdmin } = useAdmin();
  const toast = useToast();
  const [categoryFilter, setCategoryFilter] = useState<CommodityCategoryFilter>('any');

  const {
    data,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalItems,
    goToPage,
    nextPage,
    previousPage,
    refresh,
  } = useCommodities({ pageSize, categoryFilter });

  // Справочник категорий поднят на уровень страницы (ADR-028 п. 9): один fetch при isAdmin,
  // используется и таблицей (колонка «Категория»), и режимом проверки.
  const [categories, setCategories] = useState<Category[]>([]);
  useEffect(() => {
    if (!isAdmin) {
      return;
    }
    let active = true;
    fetchCategories()
      .then((loaded) => {
        if (active) {
          setCategories(loaded);
        }
      })
      .catch((err) => console.error('Ошибка загрузки категорий:', err));
    return () => {
      active = false;
    };
  }, [isAdmin]);

  // После успеха: перезагрузка текущей страницы (под фильтром «Без категории» она сама
  // наполняется следующими — C6) и тост с серверовым числом реально изменённых строк (C9).
  const handleSaved = useCallback(
    (updated: number) => {
      refresh();
      toast.success(`Сохранено категорий: ${updated}`);
    },
    [refresh, toast],
  );

  const {
    phase,
    snapshot,
    draft,
    progress,
    aiWarning,
    start,
    setValue,
    retryRow,
    save,
    cancel,
  } = useCommodityPageCategorization({ categories, onSaved: handleSaved });

  // N в кнопке — строки без содержательной категории: categoryId == null или 0 (Undefined),
  // как в existing-шаге алгоритма и подтверждении ADR-027 (D11).
  const candidateCount = useMemo(
    () => data.filter((row) => row.categoryId == null || row.categoryId === 0).length,
    [data],
  );

  const inReview = phase !== 'idle';
  const controlsLocked = inReview;
  // В режиме проверки рендерится снапшот страницы; список не перезапрашивается (ADR-028 п. 7).
  const rows = inReview && snapshot ? snapshot : data;
  const chunksFinished = progress.total > 0 && progress.processed >= progress.total;

  // T-13 (рекомендация): уход из вкладки браузера в режиме проверки теряет черновик.
  useEffect(() => {
    if (phase !== 'review') {
      return;
    }
    const warnOnLeave = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', warnOnLeave);
    return () => window.removeEventListener('beforeunload', warnOnLeave);
  }, [phase]);

  const handleReceiptClick = (receiptId: string) => {
    navigate(`/?receiptId=${receiptId}`);
  };

  return (
    <div className="layout">
      <header>
        <div>
          <h1>Товары</h1>
          <p>
            Найдено товаров: <strong>{totalItems}</strong>
          </p>
        </div>
        <div className="controls">
          <div className="page-size-selector">
            <label htmlFor="commodity-page-size-select">Строк на странице: </label>
            <select
              id="commodity-page-size-select"
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
              disabled={isLoading || controlsLocked}
              title={controlsLocked ? CONTROLS_LOCKED_TITLE : undefined}
            >
              {pageSizeOptions.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </div>
          <div className="page-size-selector">
            <label htmlFor="commodity-category-filter-select">Категория: </label>
            <select
              id="commodity-category-filter-select"
              value={categoryFilter}
              onChange={(e) => setCategoryFilter(e.target.value as CommodityCategoryFilter)}
              disabled={isLoading || controlsLocked}
              title={controlsLocked ? CONTROLS_LOCKED_TITLE : undefined}
            >
              {CATEGORY_FILTER_LABELS.map((item) => (
                <option key={item.value} value={item.value}>
                  {item.label}
                </option>
              ))}
            </select>
          </div>
          <button
            type="button"
            onClick={refresh}
            disabled={isLoading || controlsLocked}
            title={controlsLocked ? CONTROLS_LOCKED_TITLE : undefined}
          >
            Обновить
          </button>
          {/* Кнопка — только администраторам (C1); скрытие в UI не заменяет серверную проверку.
              Неактивна при N = 0 и пока не готовы данные (снапшот нельзя взять с устаревающего
              списка; справочник нужен для предзаполнения «уже присвоена»). */}
          {isAdmin && !inReview && (
            <button
              type="button"
              onClick={() => start(data)}
              disabled={candidateCount === 0 || isLoading || categories.length === 0}
              title={
                candidateCount === 0
                  ? 'На этой странице нет товаров без категории'
                  : isLoading || categories.length === 0
                    ? 'Данные ещё не загружены'
                    : undefined
              }
            >
              Категоризировать страницу ({candidateCount})
            </button>
          )}
          {inReview && (
            <>
              {/* «Сохранить» доступна и во время обработки порций (C3): сохраняется видимое. */}
              <button type="button" onClick={() => void save()} disabled={phase === 'saving'}>
                Сохранить категории
              </button>
              <button type="button" onClick={cancel} disabled={phase === 'saving'}>
                Отмена
              </button>
            </>
          )}
        </div>
      </header>

      {phase === 'review' && progress.total > 0 && !chunksFinished && (
        <div className="categorization-progress" role="status">
          Определяем категории… {progress.processed} из {progress.total}
        </div>
      )}

      {/* Постоянное предупреждение об ИИ — состояние, а не событие (ADR-028 п. 4, C7). */}
      {inReview && aiWarning && (
        <div className="categorization-ai-warning" role="status">
          ИИ недоступен: категории определяются только из кэша прошлых чеков и очереди
          предкатегорий, остальные строки помечены «не определена».
        </div>
      )}

      {/* В фазах review/saving таблица рендерит снапшот и не подменяется блоками загрузки. */}
      {inReview ? (
        <CommodityTable
          commodities={rows}
          isAdmin={isAdmin}
          onReceiptClick={handleReceiptClick}
          onRefresh={refresh}
          categories={categories}
          reviewMode
          drafts={draft}
          onDraftChange={setValue}
          onRetryRow={(commodityId) => void retryRow(commodityId)}
          retriesEnabled={phase === 'review' && chunksFinished}
          draftsLocked={phase === 'saving'}
          controlsLocked={controlsLocked}
          lockedTitle={CONTROLS_LOCKED_TITLE}
        />
      ) : isLoading ? (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем товары...
        </div>
      ) : error ? (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить товары: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      ) : (
        <CommodityTable
          commodities={data}
          isAdmin={isAdmin}
          onReceiptClick={handleReceiptClick}
          onRefresh={refresh}
          categories={categories}
        />
      )}

      {(inReview || (!isLoading && !error)) && (
        <Pagination
          currentPage={currentPage}
          totalPages={totalPages}
          onPageChange={goToPage}
          onNext={nextPage}
          onPrevious={previousPage}
          disabled={controlsLocked}
          disabledTitle={CONTROLS_LOCKED_TITLE}
        />
      )}
    </div>
  );
}
