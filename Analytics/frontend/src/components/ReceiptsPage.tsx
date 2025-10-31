import { useReceipts } from '../hooks/useReceipts';
import { Pagination } from './Pagination';
import { ReceiptTable } from './ReceiptTable';

const DEFAULT_PAGE_SIZE = 10;

export function ReceiptsPage() {
  const {
    data,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalItems,
    refresh,
    goToPage,
    nextPage,
    previousPage,
  } = useReceipts({ pageSize: DEFAULT_PAGE_SIZE });

  return (
    <main className="layout">
      <header>
        <div>
          <h1>Мои чеки</h1>
          <p>
            Найдено чеков: <strong>{totalItems}</strong>
          </p>
        </div>
        <button type="button" onClick={refresh} disabled={isLoading}>
          Обновить
        </button>
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

      {!isLoading && !error && <ReceiptTable receipts={data} />}

      {!isLoading && !error && (
        <Pagination
          currentPage={currentPage}
          totalPages={totalPages}
          onPageChange={goToPage}
          onNext={nextPage}
          onPrevious={previousPage}
        />
      )}
    </main>
  );
}
