import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchReceipts } from '../api/receipts';
import type { ReceiptSummary } from '../types/receipt';

interface UseReceiptsByMerchantOptions {
  pageSize?: number;
}

export function useReceiptsByMerchant(merchantId: string | null, { pageSize = 10 }: UseReceiptsByMerchantOptions = {}) {
  const [receipts, setReceipts] = useState<ReceiptSummary[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const loadPage = useCallback(
    (page: number) => {
      if (!merchantId) {
        setReceipts([]);
        setTotalItems(0);
        setCurrentPage(1);
        return;
      }

      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.trunc(page)) : 1;
      const offset = (normalizedPage - 1) * pageSize;

      setIsLoading(true);
      setError(null);

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

  useEffect(() => {
    loadPage(1);
  }, [loadPage]);

  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  const goToPage = useCallback(
    (page: number) => {
      if (!merchantId) return;
      
      const targetPage = Math.min(Math.max(1, Math.trunc(page)), totalPages);
      if (targetPage === currentPage && !isLoading) {
        return;
      }

      loadPage(targetPage);
    },
    [currentPage, isLoading, loadPage, totalPages, merchantId],
  );

  const nextPage = useCallback(() => {
    if (!merchantId) return;
    goToPage(currentPage + 1);
  }, [currentPage, goToPage, merchantId]);

  const previousPage = useCallback(() => {
    if (!merchantId) return;
    goToPage(currentPage - 1);
  }, [currentPage, goToPage, merchantId]);

  const refresh = useCallback(() => {
    if (!merchantId) return;
    loadPage(currentPage);
  }, [currentPage, loadPage, merchantId]);

  return {
    data: receipts,
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