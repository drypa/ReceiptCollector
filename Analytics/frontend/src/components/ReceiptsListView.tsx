import type { ReactNode } from 'react';
import { usePageSize } from '../contexts/PageSizeContext';
import type { useReceipts } from '../hooks/useReceipts';
import { Pagination } from './Pagination';
import { ReceiptTable } from './ReceiptTable';

type ReceiptsListState = ReturnType<typeof useReceipts>;

interface ReceiptsListViewProps {
  title: string;
  list: ReceiptsListState;
  onViewMerchantReceipts: (merchantId: string) => void;
  onReceiptClick: (receiptId: string) => void;
  /** Дополнительное действие в шапке (кнопка «Все чеки»). */
  headerAction?: ReactNode;
}

export function ReceiptsListView({
  title,
  list,
  onViewMerchantReceipts,
  onReceiptClick,
  headerAction,
}: ReceiptsListViewProps) {
  const { setPageSize, pageSizeOptions } = usePageSize();
  const { data, isLoading, error, currentPage, totalPages, totalItems, pageSize, refresh, goToPage, nextPage, previousPage } = list;

  return (
    <div className="layout">
      <header>
        <div>
          <h1>{title}</h1>
          <p>
            Найдено чеков: <strong>{totalItems}</strong>
            {headerAction}
          </p>
        </div>
        <div className="controls">
          <div className="page-size-selector">
            <label htmlFor="page-size-select">Строк на странице: </label>
            <select
              id="page-size-select"
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
          <span className="spinner" aria-hidden="true" /> Загружаем чеки...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить чеки: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      )}

      {!isLoading && !error && (
        <ReceiptTable
          receipts={data}
          onViewMerchantReceipts={onViewMerchantReceipts}
          onReceiptClick={onReceiptClick}
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
