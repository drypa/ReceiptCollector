import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchPrecategorizedCommodities } from '../api/precategorizedCommodities';
import type { PrecategorizedCommodityItem } from '../types/precategorizedCommodity';

interface UsePrecategorizedCommoditiesOptions {
  pageSize?: number;
}

const SEARCH_DEBOUNCE_MS = 300;

/**
 * Постраничная загрузка очереди предложений с поиском (FR-7.4).
 * Паттерн повторяет useMerchants: AbortController на запрос, refresh текущей страницы.
 */
export function usePrecategorizedCommodities({
  pageSize = 10,
}: UsePrecategorizedCommoditiesOptions = {}) {
  const [items, setItems] = useState<PrecategorizedCommodityItem[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [reloadToken, setReloadToken] = useState(0);
  const abortRef = useRef<AbortController | null>(null);

  // Поиск с debounce: сброс на первую страницу при изменении запроса.
  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setCurrentPage(1);
    }, SEARCH_DEBOUNCE_MS);

    return () => window.clearTimeout(timeoutId);
  }, [searchInput]);

  const loadPage = useCallback(
    (page: number, term: string) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.trunc(page)) : 1;
      const offset = (normalizedPage - 1) * pageSize;

      setIsLoading(true);
      setError(null);

      fetchPrecategorizedCommodities({
        limit: pageSize,
        offset,
        search: term || undefined,
        signal: controller.signal,
      })
        .then(({ items: pageItems, totalItems: total, currentPage: responsePage, pageSize: responsePageSize }) => {
          const effectivePageSize = responsePageSize > 0 ? responsePageSize : pageSize;
          const effectivePage = responsePage > 0 ? responsePage : normalizedPage;
          const effectiveTotalItems = total >= 0 ? total : offset + pageItems.length;
          const computedTotalPages = Math.max(1, Math.ceil(effectiveTotalItems / effectivePageSize));

          setItems(pageItems);
          setTotalItems(effectiveTotalItems);
          setCurrentPage(Math.min(Math.max(1, effectivePage), computedTotalPages));
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

  useEffect(() => {
    loadPage(currentPage, search);

    return () => {
      abortRef.current?.abort();
    };
  }, [currentPage, search, reloadToken, loadPage]);

  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  const goToPage = useCallback(
    (page: number) => {
      const targetPage = Math.min(Math.max(1, Math.trunc(page)), totalPages);
      if (targetPage === currentPage && !isLoading) {
        return;
      }

      // Загрузку выполняет эффект ниже — не дублируем запрос.
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
    data: items,
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
