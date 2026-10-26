import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchCommodities } from '../api/commodities';
import type { CommodityItem } from '../types/commodity';
import type { CommodityCategoryFilter } from '../types/commodity';

interface UseCommoditiesOptions {
  pageSize?: number;
  categoryFilter?: CommodityCategoryFilter;
}

export function useCommodities({ pageSize = 10, categoryFilter = 'any' }: UseCommoditiesOptions = {}) {
  const [commodities, setCommodities] = useState<CommodityItem[]>([]);
  const [currentPage, setCurrentPage] = useState(1);
  const [totalItems, setTotalItems] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  // Именованное функциональное выражение: позволяет рекурсивно догрузить страницу
  // ровно один раз (нормализация пустой страницы, C6) без ref-костылей.
  const loadPage = useCallback(function load(
    page: number,
    allowNormalization = true,
  ): void {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.trunc(page)) : 1;
    const offset = (normalizedPage - 1) * pageSize;

    setIsLoading(true);
    setError(null);

    fetchCommodities({ limit: pageSize, offset, signal: controller.signal, categoryFilter })
      .then(({ commodities: pageCommodities, totalItems: total, currentPage: responsePage, pageSize: responsePageSize }) => {
        // Ответ устаревшего запроса (отменён или вытеснен новым) игнорируется целиком.
        if (abortRef.current !== controller) {
          return;
        }

        const effectivePageSize = responsePageSize > 0 ? responsePageSize : pageSize;
        const effectivePage = responsePage > 0 ? responsePage : normalizedPage;
        const effectiveTotalItems = total >= 0 ? total : offset + pageCommodities.length;
        const computedTotalPages = Math.max(1, Math.ceil(effectiveTotalItems / effectivePageSize));

        // Инвариант (ADR-028 п. 6, C6): текущая страница непустая и не больше последней.
        // Пустая страница за пределами последней (например, после сохранения категорий
        // под фильтром «Без категории») — догружаем последнюю доступную. Не более одной
        // повторной попытки на цепочку (allowNormalization=false во втором вызове) —
        // защита от цикла. При totalItems === 0 computedTotalPages === 1: догрузки нет,
        // пустой список показывается штатно («Товары не найдены»), это не ошибка.
        if (
          pageCommodities.length === 0 &&
          effectiveTotalItems > 0 &&
          normalizedPage > computedTotalPages &&
          allowNormalization
        ) {
          load(computedTotalPages, false);
          return;
        }

        setCommodities(pageCommodities);
        setTotalItems(effectiveTotalItems);
        setCurrentPage(Math.min(Math.max(1, effectivePage), computedTotalPages));
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
        // isLoading не «залипает» при цепочке из двух запросов: пока активен новый
        // controller, статус оставляет в покое (его finally закроет цепочку).
        if (abortRef.current === controller) {
          setIsLoading(false);
        }
      });
    },
    [pageSize, categoryFilter],
  );

  // Единственный триггер загрузки: монтирование + смена размера страницы + смена фильтра
  // (loadPage пересоздаётся при обоих). Прежние два useEffect давали ДВА первичных
  // запроса на монтировании (исправлено). Сброс на страницу 1 при смене pageSize
  // сохранён; при смене фильтра запрос и раньше шёл на страницу 1.
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

  // Перезагрузка текущей (уже нормализованной) страницы; при опустевшей после
  // сохранения странице сработает нормализация из loadPage (догрузка последней доступной).
  const refresh = useCallback(() => {
    loadPage(currentPage);
  }, [currentPage, loadPage]);

  return {
    data: commodities,
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
