import { usePageSize } from '../contexts/PageSizeContext';
import { useAdmin } from '../hooks/useAdmin';
import { useMerchants } from '../hooks/useMerchants';
import { Pagination } from './Pagination';
import { MerchantTable } from './MerchantTable';

export function MerchantsPage() {
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

  return <MerchantsView isAdmin={isAdmin} />;
}

interface MerchantsViewProps {
  isAdmin: boolean;
}

function MerchantsView({ isAdmin }: MerchantsViewProps) {
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();

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
  } = useMerchants({ pageSize });

  return (
    <div className="layout">
      <header>
        <div>
          <h1>Магазины</h1>
          <p>
            Найдено магазинов: <strong>{totalItems}</strong>
          </p>
        </div>
        <div className="controls">
          <div className="page-size-selector">
            <label htmlFor="merchant-page-size-select">Строк на странице: </label>
            <select
              id="merchant-page-size-select"
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

      {isLoading && (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем магазины...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить магазины: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      )}

      {!isLoading && !error && (
        <MerchantTable merchants={data} isAdmin={isAdmin} onRefresh={refresh} />
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
