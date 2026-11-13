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
 *
 * Нормализация опустевшей страницы (ADR-036 §5, по образцу useCommodities и ADR-028 §6):
 * если запрошенная страница оказалась за последней доступной — например, массовая
 * операция подтвердила или удалила её последние строки, — страница один раз
 * перезапрашивается на последнюю доступную. Пустая первая страница (очередь пуста или
 * поиск ничего не нашёл) показывается штатно: пустое состояние, не ошибка, без догрузки.
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
  // Признак того, что смену currentPage инициировала нормализация опустевшей страницы:
  // страницу уже дозапрашивает load напрямую, и эффект-триггер не должен ни прерывать
  // этот запрос, ни дублировать его (см. loadPage и эффект загрузки ниже).
  const normalizationRetryRef = useRef(false);

  // Поиск с debounce: сброс на первую страницу при изменении запроса.
  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      setSearch(searchInput.trim());
      setCurrentPage(1);
    }, SEARCH_DEBOUNCE_MS);

    return () => window.clearTimeout(timeoutId);
  }, [searchInput]);

  // Именованное функциональное выражение — как в useCommodities (ADR-028 §6):
  // повторный запрос выполняет сам load, рекурсия ограничена одним шагом.
  const loadPage = useCallback(function load(
    page: number,
    term: string,
    allowNormalization = true,
  ): void {
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
        // Ответ устаревшего запроса (отменён или вытеснен новым) игнорируется целиком:
        // иначе цепочка из двух запросов затрёт свежие данные (ADR-028 §6).
        if (abortRef.current !== controller) {
          return;
        }

        const effectivePageSize = responsePageSize > 0 ? responsePageSize : pageSize;
        const effectivePage = responsePage > 0 ? responsePage : normalizedPage;
        const effectiveTotalItems = total >= 0 ? total : offset + pageItems.length;
        const computedTotalPages = Math.max(1, Math.ceil(effectiveTotalItems / effectivePageSize));

        // Инвариант (ADR-036 §5, по образцу ADR-028 §6): страница под запросом непустая
        // и не больше последней. Опустевшая последняя страница (массовая операция забрала
        // её последние строки) — догружаем последнюю доступную; не более одной повторной
        // попытки на цепочку (allowNormalization=false во втором вызове) — защита от
        // цикла. При effectiveTotalItems === 0 (очередь пуста/поиск не нашёл) условие
        // неверно: пустая страница показывается штатно, это не ошибка.
        if (
          pageItems.length === 0 &&
          effectiveTotalItems > 0 &&
          normalizedPage > computedTotalPages &&
          allowNormalization
        ) {
          // currentPage меняется сразу, повторный запрос выполняет сам load. Эффект-
          // триггер пересоздастся на эту смену: его cleanup не прерывает догрузку, а сам
          // эффект не дублирует запрос (normalizationRetryRef).
          normalizationRetryRef.current = true;
          setCurrentPage(computedTotalPages);
          load(computedTotalPages, term, false);
          return;
        }

        setItems(pageItems);
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
        // isLoading не мигает при цепочке из двух запросов: пока активен новый
        // controller, статус оставляет в покое (его finally закроет цепочку).
        if (abortRef.current === controller) {
          setIsLoading(false);
        }
      });
    },
    [pageSize],
  );

  useEffect(() => {
    // Смену currentPage инициировала нормализация опустевшей страницы: страницу уже
    // дозапрашивает load, дублирующий запрос из эффекта не отправляем.
    // Cleanup здесь намеренно нет: прерывать летящий запрос-догрузку нельзя, а
    // последующие загрузки прерывают предыдущий запрос сами (abort в начале loadPage),
    // размонтирование же покрыто отдельным эффектом ниже. Не «чинить» return cleanup'ом.
    if (normalizationRetryRef.current) {
      normalizationRetryRef.current = false;
      return;
    }

    loadPage(currentPage, search);

    return () => {
      // При нормализации эффект пересоздаётся из-за смены currentPage, но активный
      // запрос-догрузку прерывать нельзя.
      if (!normalizationRetryRef.current) {
        abortRef.current?.abort();
      }
    };
  }, [currentPage, search, reloadToken, loadPage]);

  // Отмена активного запроса при размонтировании — в том числе когда оно пришлось на
  // повторный запрос нормализации (его эффект cleanup не поставил).
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
