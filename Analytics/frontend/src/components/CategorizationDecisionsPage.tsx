import { useEffect, useMemo, useState } from 'react';
import { fetchCategories } from '../api/commodities';
import { useAdmin } from '../hooks/useAdmin';
import { useCategorizationDecisions } from '../hooks/useCategorizationDecisions';
import { usePageSize } from '../contexts/PageSizeContext';
import { CategorizationDecisionsTable } from './CategorizationDecisionsTable';
import { Pagination } from './Pagination';
import { renderCategorySelectOptions } from '../utils/categoryOptions';
import type { Category } from '../types/commodity';
import type { CategorizationDecisionSource } from '../types/categorizationDecision';

/** Фильтр источника: ровно четыре имени журнала (FR-1/FR-2), порядок — как в подсказке `400` сервера. */
const SOURCE_FILTER_LABELS: Array<{ value: CategorizationDecisionSource; label: string }> = [
  { value: 'ai', label: 'ИИ' },
  { value: 'precategorized', label: 'Очередь предкатегорий' },
  { value: 'cache', label: 'Кэш прошлых чеков' },
  { value: 'merchant-default', label: 'Дефолт магазина' },
];

/**
 * Админский раздел «Журнал категоризации» (FR-6/FR-7, ADR-033 п. 8).
 * Гейт по useAdmin повторяет PrecategorizedCommoditiesPage: данные запрашиваются только
 * после подтверждения прав (вью с хуком монтируется лишь админу — при `!isAdmin` ни одного
 * запроса к `/api/categorization-decisions` не уходит). Скрытие раздела — UX, не защита:
 * сервер на прямой запрос и сам ответил бы 403 (ADR-033 п. 9).
 */
export function CategorizationDecisionsPage() {
  const { isAdmin, loading: adminLoading } = useAdmin();

  if (adminLoading) {
    return (
      <div className="layout">
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Проверяем права доступа...
        </div>
      </div>
    );
  }

  if (!isAdmin) {
    return (
      <div className="layout">
        <div className="empty-state">
          <p>Доступ запрещён.</p>
        </div>
      </div>
    );
  }

  return <CategorizationDecisionsView />;
}

function CategorizationDecisionsView() {
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();

  // Состояние фильтров — здесь, хук получает примитивы (прецедент categoryFilter в
  // CommoditiesPage): изменение ЛЮБОГО фильтра пересоздаёт loadPage в хуке, и список
  // перезагружается с первой страницы (гарантия сброса — в useCategorizationDecisions).
  const [onlyMismatch, setOnlyMismatch] = useState(false);
  const [source, setSource] = useState<'' | CategorizationDecisionSource>('');
  const [suggestedCategoryId, setSuggestedCategoryId] = useState<number | null>(null);
  const [finalCategoryId, setFinalCategoryId] = useState<number | null>(null);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  const {
    data,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalItems,
    search,
    setSearch,
    goToPage,
    nextPage,
    previousPage,
    refresh,
  } = useCategorizationDecisions({
    pageSize,
    onlyMismatch,
    source: source || undefined,
    suggestedCategoryId: suggestedCategoryId ?? undefined,
    finalCategoryId: finalCategoryId ?? undefined,
    from: from || undefined,
    to: to || undefined,
  });

  // Справочник категорий фильтров (тот же fetch, что в PrecategorizedCommodityTable).
  // Сбой не роняет страницу: селекторы остаются пустыми, журнал — работает.
  const [categories, setCategories] = useState<Category[]>([]);
  useEffect(() => {
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
  }, []);

  // «Предложено»: без Undefined — предложения «Не указана» не бывает, сервер отклоняет
  // suggestedCategory=0 (ADR-033 п. 7). «Принято» — полный справочник: Undefined (id 0)
  // приходит из справочника с именем «Не указана» и допустим как итог (сброс после предложения).
  const suggestedCategoryOptions = useMemo(
    () => categories.filter((category) => category.key !== 'Undefined'),
    [categories],
  );

  return (
    <div className="layout">
      <header>
        <div>
          <h1>Журнал категоризации</h1>
          <p>
            Уникальных сочетаний: <strong>{totalItems}</strong>
          </p>
        </div>
        <div className="controls">
          <div className="page-size-selector">
            <label htmlFor="decisions-page-size-select">Строк на странице: </label>
            <select
              id="decisions-page-size-select"
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
              disabled={isLoading}
            >
              {pageSizeOptions.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </div>
          <button type="button" onClick={refresh} disabled={isLoading}>
            Обновить
          </button>
        </div>
      </header>

      <div className="decisions-filters">
        <div className="page-size-selector">
          <label htmlFor="decisions-search">Поиск: </label>
          <input
            id="decisions-search"
            type="search"
            value={search}
            placeholder="Название товара"
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>
        <div className="page-size-selector">
          <input
            id="decisions-only-mismatch"
            type="checkbox"
            checked={onlyMismatch}
            onChange={(event) => setOnlyMismatch(event.target.checked)}
            disabled={isLoading}
          />
          <label htmlFor="decisions-only-mismatch">Только расхождения</label>
        </div>
        <div className="page-size-selector">
          <label htmlFor="decisions-source-select">Источник: </label>
          <select
            id="decisions-source-select"
            value={source}
            onChange={(event) => setSource(event.target.value as '' | CategorizationDecisionSource)}
            disabled={isLoading}
          >
            <option value="">Любой</option>
            {SOURCE_FILTER_LABELS.map((item) => (
              <option key={item.value} value={item.value}>
                {item.label}
              </option>
            ))}
          </select>
        </div>
        <div className="page-size-selector">
          <label htmlFor="decisions-suggested-select">Предложено: </label>
          <select
            id="decisions-suggested-select"
            value={suggestedCategoryId === null ? '' : String(suggestedCategoryId)}
            onChange={(event) => setSuggestedCategoryId(parseCategoryId(event.target.value))}
            disabled={isLoading}
          >
            <option value="">Любая</option>
            {renderCategorySelectOptions(suggestedCategoryOptions)}
          </select>
        </div>
        <div className="page-size-selector">
          <label htmlFor="decisions-final-select">Принято: </label>
          <select
            id="decisions-final-select"
            value={finalCategoryId === null ? '' : String(finalCategoryId)}
            onChange={(event) => setFinalCategoryId(parseCategoryId(event.target.value))}
            disabled={isLoading}
          >
            <option value="">Любая</option>
            {renderCategorySelectOptions(categories)}
          </select>
        </div>
        <div className="page-size-selector">
          <label htmlFor="decisions-from">Период с: </label>
          <input
            id="decisions-from"
            type="date"
            value={from}
            max={to || undefined}
            onChange={(event) => setFrom(event.target.value)}
            disabled={isLoading}
          />
        </div>
        <div className="page-size-selector">
          <label htmlFor="decisions-to">по: </label>
          <input
            id="decisions-to"
            type="date"
            value={to}
            min={from || undefined}
            onChange={(event) => setTo(event.target.value)}
            disabled={isLoading}
          />
        </div>
      </div>

      {isLoading && (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем журнал...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить журнал: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      )}

      {!isLoading && !error && <CategorizationDecisionsTable items={data} />}

      {!isLoading && !error && (
        <Pagination
          currentPage={currentPage}
          totalPages={totalPages}
          onPageChange={goToPage}
          onNext={nextPage}
          onPrevious={previousPage}
        />
      )}
    </div>
  );
}

/**
 * Значение <select> категории: '' — «не выбрано» (параметр не отправляется серверу),
 * иначе числовой код из справочника (для «Принято» допустим и 0 — «Не указана»).
 */
function parseCategoryId(value: string): number | null {
  if (value === '') {
    return null;
  }

  const code = Number(value);
  return Number.isFinite(code) ? code : null;
}
