import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchCategorizationDecisions } from '../api/categorizationDecisions';
import type {
  CategorizationDecisionItem,
  CategorizationDecisionSource,
} from '../types/categorizationDecision';

interface UseCategorizationDecisionsOptions {
  pageSize?: number;
  /** Только расхождения (`suggested != final`) — главный фильтр страницы (FR-6). */
  onlyMismatch?: boolean;
  /** Wire-имя источника; `undefined` — без фильтра. */
  source?: CategorizationDecisionSource;
  /** Код предложенной категории (> 0); `undefined` — без фильтра. */
  suggestedCategoryId?: number;
  /** Код итоговой категории (0 = «Не указана» допустим); `undefined` — без фильтра. */
  finalCategoryId?: number;
  /** Начало периода `yyyy-MM-dd`; `undefined` — без границы. */
  from?: string;
  /** Конец периода `yyyy-MM-dd` (день включается, см. api.toIsoBoundary). */
  to?: string;
}

const SEARCH_DEBOUNCE_MS = 300;

/**
 * Постраничная загрузка журнала уникальных сочетаний с фильтрами (FR-6/FR-7).
 * Паттерн — гибрид двух прецедентов: `usePrecategorizedCommodities` (AbortController
 * на запрос, debounce поиска, `refresh`) и `useCommodities` (фильтры примитивами из
 * страницы; ЕДИНСТВЕННЫЙ триггер загрузки — смена `loadPage`, которая пересоздаётся
 * при изменении любого фильтра/поиска/размера страницы).
 * <p>
 * Сброс на первую страницу при изменении ЛЮБОГО фильтра обеспечивается триггером
 * ниже (FR-6): фильтр изменился → новая `loadPage` → `setCurrentPage(1)` + загрузка
 * страницы 1; двойных запросов на монтировании нет (исправление C6 из ADR-028).
 */
export function useCategorizationDecisions({
  pageSize = 10,
  onlyMismatch = false,
  source,
  suggestedCategoryId,
  finalCategoryId,
  from,
  to,
}: UseCategorizationDecisionsOptions = {}) {
  const [items, setItems] = useState<CategorizationDecisionItem[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const abortRef = useRef<AbortController | null>(null);

  // Поиск с debounce: в запрос уходит trimmed-значение (прецедент usePrecategorizedCommodities).
  useEffect(() => {
    const timeoutId = window.setTimeout(() => setSearch(searchInput.trim()), SEARCH_DEBOUNCE_MS);

    return () => window.clearTimeout(timeoutId);
  }, [searchInput]);

  const loadPage = useCallback(
    (page: number) => {
      abortRef.current?.abort();
      const controller = new AbortController();
      abortRef.current = controller;

      const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.trunc(page)) : 1;
      const offset = (normalizedPage - 1) * pageSize;

      setIsLoading(true);
      setError(null);

      fetchCategorizationDecisions({
        limit: pageSize,
        offset,
        onlyMismatch,
        source,
        suggestedCategoryId,
        finalCategoryId,
        search: search || undefined,
        from,
        to,
        signal: controller.signal,
      })
        .then(({ items: pageItems, totalItems: total, currentPage: responsePage, pageSize: responsePageSize }) => {
          // Ответ устаревшего запроса (отменён или вытеснен новым) игнорируется целиком
          // (прецедент useCommodities): фильтр/поиск сменились — пишем только «свой» ответ.
          if (abortRef.current !== controller) {
            return;
          }

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
    [pageSize, onlyMismatch, source, suggestedCategoryId, finalCategoryId, from, to, search],
  );

  // Единственный триггер загрузки: монтирование + смена любого фильтра/поиска/размера
  // страницы (loadPage пересоздаётся при каждом). Страница всегда начинается с первой (FR-6).
  useEffect(() => {
    setCurrentPage(1);
    loadPage(1);
  }, [loadPage]);

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

  // Перезагрузка текущей страницы с теми же фильтрами (прецедент usePrecategorizedCommodities).
  const refresh = useCallback(() => {
    loadPage(currentPage);
  }, [currentPage, loadPage]);

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
