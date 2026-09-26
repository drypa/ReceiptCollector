import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchReceipts, isNotFound } from '../api/receipts';
import type { ReceiptSummary } from '../types/receipt';

interface UseReceiptsOptions {
  pageSize?: number;
  /** Задан — список чеков магазина, не задан — все чеки пользователя. */
  merchantId?: string;
}

export function useReceipts({ pageSize = 10, merchantId }: UseReceiptsOptions = {}) {
  const [receipts, setReceipts] = useState<ReceiptSummary[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const abortRef = useRef<AbortController | null>(null);

  const loadPage = useCallback(
    (page: number) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.trunc(page)) : 1;
      const offset = (normalizedPage - 1) * pageSize;

      setIsLoading(true);
      setError(null);
      setNotFound(false);

      fetchReceipts({ limit: pageSize, offset, signal: controller.signal, merchantId })
        .then(({ receipts: pageReceipts, totalItems: total, currentPage: responsePage, pageSize: responsePageSize }) => {
          const effectivePageSize = responsePageSize > 0 ? responsePageSize : pageSize;
          const effectivePage = responsePage > 0 ? responsePage : normalizedPage;
          const effectiveTotalItems = total >= 0 ? total : offset + pageReceipts.length;
          const computedTotalPages = Math.max(1, Math.ceil(effectiveTotalItems / effectivePageSize));
          const finalPage = Math.min(Math.max(1, effectivePage), computedTotalPages);

          setReceipts(pageReceipts);
          setTotalItems(effectiveTotalItems);
          setCurrentPage(finalPage);
        })
        .catch((fetchError) => {
          if (fetchError instanceof DOMException && fetchError.name === 'AbortError') {
            return;
          }
          if (isNotFound(fetchError)) {
            setNotFound(true);
            setReceipts([]);
            setTotalItems(0);
            return;
          }
          setError(fetchError instanceof Error ? fetchError.message : 'Неизвестная ошибка');
        })
        .finally(() => {
          if (abortRef.current === controller) {
            setIsLoading(false);
          }
        });
    },
    [pageSize, merchantId],
  );

  // When pageSize changes, reset to first page
  useEffect(() => {
    setCurrentPage(1);
    loadPage(1);
  }, [pageSize, loadPage]);

  useEffect(() => {
    loadPage(1);

    return () => {
      abortRef.current?.abort();
    };
  }, [loadPage]);

  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  const goToPage = useCallback(
    (page: number) => {
      const targetPage = Math.min(Math.max(1, Math.trunc(page)), totalPages);
      if (targetPage === currentPage && !isLoading) {
        return;
      }

      loadPage(targetPage);
    },
    [currentPage, isLoading, loadPage, totalPages],
  );

  const nextPage = useCallback(() => {
    goToPage(currentPage + 1);
  }, [currentPage, goToPage]);

  const previousPage = useCallback(() => {
    goToPage(currentPage - 1);
  }, [currentPage, goToPage]);

  const refresh = useCallback(() => {
    loadPage(currentPage);
  }, [currentPage, loadPage]);

  return {
    data: receipts,
    isLoading,
    error,
    /** Ресурс отсутствует (404): страница решает, какой экран «Не найдено» показать. */
    notFound,
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
