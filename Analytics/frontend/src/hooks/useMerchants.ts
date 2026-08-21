import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchMerchants } from '../api/merchants';
import type { MerchantDto } from '../api/merchants';

interface UseMerchantsOptions {
  pageSize?: number;
}

export function useMerchants({ pageSize = 10 }: UseMerchantsOptions = {}) {
  const [merchants, setMerchants] = useState<MerchantDto[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
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

      fetchMerchants({ limit: pageSize, offset, signal: controller.signal })
        .then(({ merchants: pageMerchants, totalItems: total, currentPage: responsePage, pageSize: responsePageSize }) => {
          const effectivePageSize = responsePageSize > 0 ? responsePageSize : pageSize;
          const effectivePage = responsePage > 0 ? responsePage : normalizedPage;
          const effectiveTotalItems = total >= 0 ? total : offset + pageMerchants.length;
          const computedTotalPages = Math.max(1, Math.ceil(effectiveTotalItems / effectivePageSize));
          const finalPage = Math.min(Math.max(1, effectivePage), computedTotalPages);

          setMerchants(pageMerchants);
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
    [pageSize],
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
    data: merchants,
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
