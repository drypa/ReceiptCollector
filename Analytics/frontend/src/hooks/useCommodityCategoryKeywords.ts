import { useCallback, useEffect, useRef, useState } from 'react';
import {
  addKeyword,
  CommodityCategoryKeywordApiError,
  deleteKeyword,
  fetchCategoryKeywords,
} from '../api/commodityCategoryKeywords';
import type { CategoryKeywords, KeywordListType } from '../types/commodityCategoryKeyword';

/** Ключ пары «категория + список»: блокировка поля ввода и inline-ошибка привязаны к нему. */
export function keywordListKey(categoryId: number, listType: KeywordListType): string {
  return `${categoryId}:${listType}`;
}

/** Ключ конкретного чипа: на время удаления блокируется только этот чип, не весь список. */
export function keywordChipKey(id: string): string {
  return `chip:${id}`;
}

/**
 * Загрузка снимка слов и мутации для раздела «Категории товаров» (ADR-035 п. 8,
 * решение H1): после успешного `POST`/`DELETE` выполняется рефетч `GET` с сохранением
 * уже отрисованных данных — `data` не очищается перед запросом, поэтому страница
 * не «мигает» в пустоту. На время запроса блокируется только затронутое поле/чип
 * (`busyKeys`), остальные списки остаются редактируемыми.
 * Стиль — прецедент usePrecategorizedCommodities: `AbortController` на `GET`,
 * `error`/`isLoading`/`reload`; ошибки мутаций классифицирует `reportMutationError`.
 */
export function useCommodityCategoryKeywords() {
  const [data, setData] = useState<CategoryKeywords[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [listErrors, setListErrors] = useState<Record<string, string>>({});
  const [busyKeys, setBusyKeys] = useState<ReadonlySet<string>>(new Set<string>());
  const abortRef = useRef<AbortController | null>(null);

  const markBusy = useCallback((key: string, busy: boolean) => {
    setBusyKeys((keys) => {
      if (keys.has(key) === busy) {
        return keys;
      }

      const next = new Set(keys);
      if (busy) {
        next.add(key);
      } else {
        next.delete(key);
      }

      return next;
    });
  }, []);

  const setListError = useCallback((listKey: string, message: string | null) => {
    setListErrors((errors) => {
      if (message !== null) {
        return { ...errors, [listKey]: message };
      }

      if (!(listKey in errors)) {
        return errors;
      }

      const next = { ...errors };
      delete next[listKey];
      return next;
    });
  }, []);

  // Единственный GET: отменяет предыдущий, пишет только «свой» ответ (прецедент
  // useCategorizationDecisions). data перед запросом НЕ сбрасывается — рефетч после
  // мутации атомарно подменяет снимок (H1).
  const load = useCallback(async () => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setIsLoading(true);
    setError(null);

    try {
      const categories = await fetchCategoryKeywords(controller.signal);
      if (abortRef.current !== controller) {
        return;
      }

      setData(categories);
    } catch (loadError) {
      if (loadError instanceof DOMException && loadError.name === 'AbortError') {
        return;
      }

      if (abortRef.current !== controller) {
        return;
      }

      setError(loadError instanceof Error ? loadError.message : 'Неизвестная ошибка');
    } finally {
      if (abortRef.current === controller) {
        setIsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void load();

    return () => {
      abortRef.current?.abort();
    };
  }, [load]);

  // «Адресные» клиентские ошибки (4xx) относятся к затронутому списку и показываются
  // под ним inline: 400/404/409 — текстом сервера, 401/403 — пояснениями из api-модуля
  // (ADR-035 п. 8). Сеть и 5xx — общий баннер с кнопкой «Повторить».
  const reportMutationError = useCallback(
    (mutationError: unknown, listKey: string) => {
      const message =
        mutationError instanceof Error ? mutationError.message : 'Неизвестная ошибка';

      if (
        mutationError instanceof CommodityCategoryKeywordApiError &&
        mutationError.status >= 400 &&
        mutationError.status < 500
      ) {
        setListError(listKey, message);
        return;
      }

      setError(message);
    },
    [setListError],
  );

  /** Добавление слова: `true` — успех (данные уже рефетчнуты), `false` — ошибка уже разложена по состояниям. */
  const add = useCallback(
    async (categoryId: number, listType: KeywordListType, word: string): Promise<boolean> => {
      const listKey = keywordListKey(categoryId, listType);
      markBusy(listKey, true);
      setListError(listKey, null);

      try {
        await addKeyword({ categoryId, listType, word });
        await load();
        return true;
      } catch (mutationError) {
        reportMutationError(mutationError, listKey);
        return false;
      } finally {
        markBusy(listKey, false);
      }
    },
    [load, markBusy, reportMutationError, setListError],
  );

  /**
   * Удаление слова по `id`: блокируется чип (`keywordChipKey`), а inline-ошибка
   * привязывается к списку-владельцу (`listKey` — его знает карточка категории).
   */
  const remove = useCallback(
    async (id: string, listKey: string): Promise<boolean> => {
      const chipKey = keywordChipKey(id);
      markBusy(chipKey, true);
      setListError(listKey, null);

      try {
        await deleteKeyword(id);
        await load();
        return true;
      } catch (mutationError) {
        reportMutationError(mutationError, listKey);
        return false;
      } finally {
        markBusy(chipKey, false);
      }
    },
    [load, markBusy, reportMutationError, setListError],
  );

  return {
    data,
    isLoading,
    error,
    listErrors,
    busyKeys,
    reload: load,
    add,
    delete: remove,
  };
}
