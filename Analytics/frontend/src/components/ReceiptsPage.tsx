import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useReceipts } from '../hooks/useReceipts';
import { useReceiptsByMerchant } from '../hooks/useReceiptsByMerchant';
import { usePageSize } from '../contexts/PageSizeContext';
import { Pagination } from './Pagination';
import { ReceiptTable } from './ReceiptTable';
import { ReceiptDetails } from './ReceiptDetails';
import { fetchReceiptDetails } from '../api/receipts';
import type { ReceiptDetails as ReceiptDetailsType } from '../types/receipt';

export function ReceiptsPage() {
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();
  const [searchParams, setSearchParams] = useSearchParams();
  const receiptIdFromUrl = searchParams.get('receiptId');
  const [selectedMerchantId, setSelectedMerchantId] = useState<string | null>(null);
  const [selectedReceiptId, setSelectedReceiptId] = useState<string | null>(null);
  const [receiptDetails, setReceiptDetails] = useState<ReceiptDetailsType | null>(null);
  const [loadingReceiptDetails, setLoadingReceiptDetails] = useState(false);
  
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
  } = useReceipts({ pageSize });
  
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
  } = useReceiptsByMerchant(selectedMerchantId, { pageSize });

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

  const handleReceiptClick = async (receiptId: string) => {
    setSelectedReceiptId(receiptId);
    setLoadingReceiptDetails(true);
    
    try {
      const details = await fetchReceiptDetails(receiptId);
      setReceiptDetails(details);
    } catch (error) {
      console.error('Failed to load receipt details:', error);
      setReceiptDetails(null);
    } finally {
      setLoadingReceiptDetails(false);
    }
  };

  const handleBackToList = () => {
    setSelectedReceiptId(null);
    setReceiptDetails(null);
  };

  useEffect(() => {
    if (receiptIdFromUrl) {
      handleReceiptClick(receiptIdFromUrl);
      setSearchParams({}, { replace: true });
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [receiptIdFromUrl]);

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

      {!isLoading && !error && !selectedReceiptId && (
        <ReceiptTable
          receipts={data}
          onViewMerchantReceipts={handleViewMerchantReceipts}
          onReceiptClick={handleReceiptClick}
        />
      )}

      {selectedReceiptId && (
        <>
          {loadingReceiptDetails ? (
            <div className="state state-loading">
              <span className="spinner" aria-hidden="true" /> Загружаем детали чека...
            </div>
          ) : (
            <ReceiptDetails receipt={receiptDetails} onBack={handleBackToList} />
          )}
        </>
      )}

      {!isLoading && !error && !selectedReceiptId && (
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
