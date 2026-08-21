import { useNavigate } from 'react-router-dom';
import { usePageSize } from '../contexts/PageSizeContext';
import { useCommodities } from '../hooks/useCommodities';
import { useAdmin } from '../hooks/useAdmin';
import { Pagination } from './Pagination';
import { CommodityTable } from './CommodityTable';

export function CommoditiesPage() {
  const navigate = useNavigate();
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();
  const { isAdmin } = useAdmin();

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
  } = useCommodities({ pageSize });

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
          <span className="spinner" aria-hidden="true" /> Загружаем товары...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить товары: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      )}

      {!isLoading && !error && (
        <CommodityTable
          commodities={data}
          isAdmin={isAdmin}
          onReceiptClick={handleReceiptClick}
          onRefresh={refresh}
        />
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
