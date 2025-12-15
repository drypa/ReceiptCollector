import { useState, useEffect } from 'react';
import { useReceipts } from '../hooks/useReceipts';
import { useReceiptsByMerchant } from '../hooks/useReceiptsByMerchant';
import { Pagination } from './Pagination';
import { ReceiptTable } from './ReceiptTable';

const DEFAULT_PAGE_SIZE = 10;

export function ReceiptsPage() {
  const [selectedMerchantId, setSelectedMerchantId] = useState<string | null>(null);
  
  // Use the appropriate hook based on whether we're filtering by merchant
  const {
    data: allReceiptsData,
    isLoading: allReceiptsLoading,
    error: allReceiptsError,
    currentPage: allReceiptsCurrentPage,
    totalPages: allReceiptsTotalPages,
    totalItems: allReceiptsTotalItems,
    refresh: allReceiptsRefresh,
    goToPage: allReceiptsGoToPage,
    nextPage: allReceiptsNextPage,
    previousPage: allReceiptsPreviousPage
  } = useReceipts({ pageSize: DEFAULT_PAGE_SIZE });
  
  const {
    data: merchantReceiptsData,
    isLoading: merchantReceiptsLoading,
    error: merchantReceiptsError,
    currentPage: merchantReceiptsCurrentPage,
    totalPages: merchantReceiptsTotalPages,
    totalItems: merchantReceiptsTotalItems,
    refresh: merchantReceiptsRefresh,
    goToPage: merchantReceiptsGoToPage,
    nextPage: merchantReceiptsNextPage,
    previousPage: merchantReceiptsPreviousPage
  } = useReceiptsByMerchant(selectedMerchantId, { pageSize: DEFAULT_PAGE_SIZE });

  // Select the appropriate data and methods based on selectedMerchantId
  const data = selectedMerchantId ? merchantReceiptsData : allReceiptsData;
  const isLoading = selectedMerchantId ? merchantReceiptsLoading : allReceiptsLoading;
  const error = selectedMerchantId ? merchantReceiptsError : allReceiptsError;
  const currentPage = selectedMerchantId ? merchantReceiptsCurrentPage : allReceiptsCurrentPage;
  const totalPages = selectedMerchantId ? merchantReceiptsTotalPages : allReceiptsTotalPages;
  const totalItems = selectedMerchantId ? merchantReceiptsTotalItems : allReceiptsTotalItems;
  const refresh = selectedMerchantId ? merchantReceiptsRefresh : allReceiptsRefresh;
  const goToPage = selectedMerchantId ? merchantReceiptsGoToPage : allReceiptsGoToPage;
  const nextPage = selectedMerchantId ? merchantReceiptsNextPage : allReceiptsNextPage;
  const previousPage = selectedMerchantId ? merchantReceiptsPreviousPage : allReceiptsPreviousPage;

  const handleBackToAllReceipts = () => {
    setSelectedMerchantId(null);
  };

  const handleViewMerchantReceipts = (merchantId: string) => {
    setSelectedMerchantId(merchantId);
  };

  return (
    <main className="layout">
      <header>
        <div>
          <h1>{selectedMerchantId ? 'Чеки по магазину' : 'Мои чеки'}</h1>
          <p>
            Найдено чеков: <strong>{totalItems}</strong>
            {selectedMerchantId && (
              <button
                type="button"
                onClick={handleBackToAllReceipts}
                style={{ marginLeft: '1rem' }}
                className="secondary"
              >
                Все чеки
              </button>
            )}
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

      {!isLoading && !error && <ReceiptTable receipts={data} onViewMerchantReceipts={handleViewMerchantReceipts} />}

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
