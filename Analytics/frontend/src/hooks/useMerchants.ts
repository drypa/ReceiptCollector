import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchMerchants } from '../api/merchants';
import type { MerchantDto } from '../api/merchants';

interface UseMerchantsOptions {
  pageSize?: number;
}

const SEARCH_DEBOUNCE_MS = 300;

export function useMerchants({ pageSize = 10 }: UseMerchantsOptions = {}) {
  const [merchants, setMerchants] = useState<MerchantDto[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [reloadToken, setReloadToken] = useState(0);
  const abortRef = useRef<AbortController | null>(null);

  // Поиск с debounce: сброс на первую страницу при изменении запроса (как в precategorization).
  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setCurrentPage(1);
    }, SEARCH_DEBOUNCE_MS);

    return () => window.clearTimeout(timeoutId);
  }, [searchInput]);

  // При смене размера страницы — первая страница; загрузка выполнится эффектом ниже.
  useEffect(() => {
    setCurrentPage(1);
  }, [pageSize]);

  const loadPage = useCallback(
    (page: number, term: string) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.trunc(page)) : 1;
      const offset = (normalizedPage - 1) * pageSize;

      setIsLoading(true);
      setError(null);

      fetchMerchants({ limit: pageSize, offset, search: term || undefined, signal: controller.signal })
        .then(({ merchants: pageMerchants, totalItems: total, currentPage: responsePage, pageSize: responsePageSize }) => {
          if (abortRef.current !== controller) {
            return;
          }

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
          if (abortRef.current !== controller) {
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

  useEffect(() => {
    loadPage(currentPage, search);

    return () => {
      abortRef.current?.abort();
    };
  }, [currentPage, search, reloadToken, loadPage]);

  // Отмена активного запроса при размонтировании.
  useEffect(() => {
    return () => {
      abortRef.current?.abort();
    };
  }, []);

  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  const goToPage = useCallback(
    (page: number) => {
      const targetPage = Math.min(Math.max(1, Math.trunc(page)), totalPages);
      if (targetPage === currentPage && !isLoading) {
        return;
      }

      setCurrentPage(targetPage);
    },
    [currentPage, isLoading, totalPages],
  );

  const nextPage = useCallback(() => {
    goToPage(currentPage + 1);
  }, [currentPage, goToPage]);

  const previousPage = useCallback(() => {
    goToPage(currentPage - 1);
  }, [currentPage, goToPage]);

  const refresh = useCallback(() => {
    setReloadToken((token) => token + 1);
  }, []);

  return {
    data: merchants,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalItems,
    pageSize,
    search: searchInput,
    setSearch: setSearchInput,
    goToPage,
    nextPage,
    previousPage,
    refresh,
  };
}
