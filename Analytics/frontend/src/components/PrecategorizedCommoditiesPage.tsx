import { usePageSize } from '../contexts/PageSizeContext';
import { useAdmin } from '../hooks/useAdmin';
import { usePrecategorizedCommodities } from '../hooks/usePrecategorizedCommodities';
import { Pagination } from './Pagination';
import { PrecategorizedCommodityTable } from './PrecategorizedCommodityTable';

/**
 * Админский раздел «Предкатегоризация» (FR-7, ADR-027 п.7).
 * Гейт по useAdmin повторяет MerchantsPage: для не-админа данные не запрашиваются.
 */
export function PrecategorizedCommoditiesPage() {
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

  return <PrecategorizedCommoditiesView />;
}

function PrecategorizedCommoditiesView() {
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();
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
  } = usePrecategorizedCommodities({ pageSize });

  return (
    <div className="layout">
      <header>
        <div>
          <h1>Предкатегоризация</h1>
          <p>
            Предложений в очереди: <strong>{totalItems}</strong>
          </p>
        </div>
        <div className="controls">
          <div className="page-size-selector">
            <label htmlFor="precategorized-page-size-select">Строк на странице: </label>
            <select
              id="precategorized-page-size-select"
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

      <div className="controls">
        <label htmlFor="precategorized-search">Поиск: </label>
        <input
          id="precategorized-search"
          type="search"
          value={search}
          placeholder="Наименование или магазин"
          onChange={(event) => setSearch(event.target.value)}
        />
      </div>

      {isLoading && (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем предложения...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить предложения: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      )}

      {!isLoading && !error && (
        <PrecategorizedCommodityTable items={data} onRefresh={refresh} />
      )}

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
