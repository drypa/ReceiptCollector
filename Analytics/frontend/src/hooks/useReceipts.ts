import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { fetchReceipts } from '../api/receipts';
import type { ReceiptSummary } from '../types/receipt';

interface UseReceiptsOptions {
  pageSize?: number;
}

export function useReceipts({ pageSize = 10 }: UseReceiptsOptions = {}) {
  const [receipts, setReceipts] = useState<ReceiptSummary[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const load = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setIsLoading(true);
    setError(null);

    fetchReceipts(controller.signal)
      .then((data) => {
        setReceipts(data);
        setCurrentPage(1);
      })
      .catch((fetchError) => {
        if (fetchError instanceof DOMException && fetchError.name === 'AbortError') {
          return;
        }
        setError(fetchError instanceof Error ? fetchError.message : 'Неизвестная ошибка');
      })
      .finally(() => {
        if (abortRef.current === controller) {
          setIsLoading(false);
        }
      });
  }, []);

  useEffect(() => {
    load();

    return () => {
      abortRef.current?.abort();
    };
  }, [load]);

  const totalItems = receipts.length;
  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  const pageReceipts = useMemo(() => {
    const startIndex = (currentPage - 1) * pageSize;
    return receipts.slice(startIndex, startIndex + pageSize);
  }, [currentPage, receipts, pageSize]);

  const goToPage = useCallback(
    (page: number) => {
      setCurrentPage((prev) => {
        if (page < 1) {
          return 1;
        }

        if (page > totalPages) {
          return totalPages;
        }

        if (page === prev) {
          return prev;
        }

        return page;
      });
    },
    [totalPages],
  );

  const nextPage = useCallback(() => {
    goToPage(currentPage + 1);
  }, [currentPage, goToPage]);

  const previousPage = useCallback(() => {
    goToPage(currentPage - 1);
  }, [currentPage, goToPage]);

  const refresh = useCallback(() => {
    load();
  }, [load]);

  return {
    data: pageReceipts,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalItems,
    pageSize,
    goToPage,
    nextPage,
    previousPage,
    refresh,
  };
}
