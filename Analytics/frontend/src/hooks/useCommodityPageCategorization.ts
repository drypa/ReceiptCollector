import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiRequestError, saveCommodityCategories, suggestCommodityCategories } from '../api/commodities';
import type { CommodityCategorizationItem, Category, CommodityItem } from '../types/commodity';
import { useToast } from '../components/Toasts';

/**
 * Максимум уникальных названий в одной порции (ADR-028 п. 2). В запрос порции уходят
 * commodityId **всех** строк её названий — строк в порции может быть больше пяти.
 */
export const PAGE_SUGGEST_CHUNK_SIZE = 5;

/** Фазы машины состояний страницы (ADR-028 п. 5). */
export type CategorizationPhase = 'idle' | 'review' | 'saving';

/**
 * Черновик строки: `value` — **ключ** категории (имя enum, контракт PUT принимает имя),
 * пустая строка = «не трогаем» (C4). `initialValue` — значение на момент старта режима;
 * `touched` — строку правил человек, пришедшие порции её не перезаписывают (ADR-028 п. 2).
 */
export interface DraftEntry {
  value: string;
  initialValue: string;
  source?: string;
  error?: string;
  touched: boolean;
}

export interface CategorizationProgress {
  processed: number;
  total: number;
}

interface UseCommodityPageCategorizationOptions {
  /** Справочник категорий страницы (поднят на уровень CommoditiesPage, грузится один раз). */
  categories: Category[];
  /** Вызывается после успешного PUT: страница делает refresh() текущей страницы и тост. */
  onSaved: (updated: number) => void;
}

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError';
}

/** Русские тексты ошибок сохранения по статусу (ADR-028 п. 5; сообщения формируются на клиенте). */
function saveErrorMessage(error: unknown): string {
  if (error instanceof ApiRequestError) {
    if (error.status === 409) {
      return 'Часть строк больше недоступна, обновите страницу';
    }
    if (error.status === 403) {
      return 'Недостаточно прав';
    }
    if (error.status === 401) {
      return 'Требуется повторный вход';
    }
    if (error.message) {
      return error.message;
    }
  }
  return error instanceof Error && error.message ? error.message : 'Не удалось сохранить категории';
}

/**
 * Группировка кандидатов по **точному** названию и нарезка порций по
 * {@link PAGE_SUGGEST_CHUNK_SIZE} названий. Нормализация названий на клиенте
 * намеренно НЕ дублируется (единственный источник правила — серверный
 * CommodityNameNormalizer, ADR-028 п. 2).
 */
function buildChunks(candidates: CommodityItem[]): string[][] {
  const idsByName = new Map<string, string[]>();
  for (const row of candidates) {
    const ids = idsByName.get(row.name) ?? [];
    ids.push(row.id);
    idsByName.set(row.name, ids);
  }

  const groups = Array.from(idsByName.values());
  const chunks: string[][] = [];
  for (let index = 0; index < groups.length; index += PAGE_SUGGEST_CHUNK_SIZE) {
    chunks.push(groups.slice(index, index + PAGE_SUGGEST_CHUNK_SIZE).flat());
  }
  return chunks;
}

/**
 * Машина состояний режима проверки страницы «Товары» (ADR-028 п. 2/5, C2/C3/C4/C8/C9):
 * снапшот страницы, черновик, последовательные порции suggest, прогресс, сохранение
 * в любой момент, отмена без следов.
 * <p>
 * Ничего не пишет самостоятельно: единственная запись — PUT в {@link save}.
 * Монотонный `generation`-счётчик отбрасывает ответы, пришедшие после cancel()/save()
 * или размонтирования (React StrictMode, гонки).
 */
export function useCommodityPageCategorization({
  categories,
  onSaved,
}: UseCommodityPageCategorizationOptions) {
  const toast = useToast();

  const [phase, setPhase] = useState<CategorizationPhase>('idle');
  const [snapshot, setSnapshot] = useState<CommodityItem[] | null>(null);
  const [draft, setDraft] = useState<Record<string, DraftEntry>>({});
  const [progress, setProgress] = useState<CategorizationProgress>({ processed: 0, total: 0 });
  const [aiWarning, setAiWarning] = useState(false);

  const generationRef = useRef(0);
  const inflightRef = useRef<Set<AbortController>>(new Set());
  const phaseRef = useRef(phase);
  const draftRef = useRef(draft);
  const categoryKeyByIdRef = useRef<Map<number, string>>(new Map());
  const onSavedRef = useRef(onSaved);

  useEffect(() => {
    phaseRef.current = phase;
  }, [phase]);

  useEffect(() => {
    draftRef.current = draft;
  }, [draft]);

  useEffect(() => {
    onSavedRef.current = onSaved;
  }, [onSaved]);

  // Актуальная карта categoryId → key: справочник может догнать уже запущенный режим
  // (загрузка категорий асинхронна), значения применённых предложений маппятся через неё.
  useEffect(() => {
    categoryKeyByIdRef.current = new Map(categories.map((category) => [category.id, category.key]));
  }, [categories]);

  // Отменяем всё при размонтировании: ответы не должны попасть в снятый компонент.
  useEffect(() => {
    // Локальная копия ссылки на Set: правило react-hooks требует не читать ref в cleanup.
    const inflight = inflightRef.current;
    return () => {
      generationRef.current += 1;
      for (const controller of inflight) {
        controller.abort();
      }
      inflight.clear();
    };
  }, []);

  /** Инвалидирует текущий цикл: новые ответы игнорируются, все запросы прерваны. */
  const invalidate = useCallback((): number => {
    const generation = (generationRef.current += 1);
    for (const controller of inflightRef.current) {
      controller.abort();
    }
    inflightRef.current.clear();
    return generation;
  }, []);

  const resetState = useCallback(() => {
    setPhase('idle');
    setSnapshot(null);
    setDraft({});
    setProgress({ processed: 0, total: 0 });
    setAiWarning(false);
  }, []);

  /**
   * Применение ответа порции по commodityId (порядок не гарантирован):
   * touched-строки НЕ перезаписываются — поздняя порция не стирает введённое вручную.
   */
  const applySuggestionItems = useCallback((items: CommodityCategorizationItem[]) => {
    setDraft((prev) => {
      let changed = false;
      const next = { ...prev };
      for (const item of items) {
        const entry = next[item.commodityId];
        if (!entry || entry.touched) {
          continue;
        }
        next[item.commodityId] = {
          ...entry,
          value: item.categoryId != null ? (categoryKeyByIdRef.current.get(item.categoryId) ?? '') : '',
          source: item.source,
          error: item.error ?? undefined,
        };
        changed = true;
      }
      return changed ? next : prev;
    });
  }, []);

  /** Сбой порции: её строки (кроме touched) становятся «не определена» с текстом ошибки. */
  const markChunkFailed = useCallback((commodityIds: string[], message: string) => {
    setDraft((prev) => {
      let changed = false;
      const next = { ...prev };
      for (const id of commodityIds) {
        const entry = next[id];
        if (!entry || entry.touched) {
          continue;
        }
        next[id] = { ...entry, source: 'undefined', error: message };
        changed = true;
      }
      return changed ? next : prev;
    });
  }, []);

  /** Последовательный цикл порций: строго один in-flight запрос на страницу (ADR-028 п. 2). */
  const runChunks = useCallback(
    async (chunks: string[][], generation: number) => {
      for (const chunk of chunks) {
        if (generationRef.current !== generation) {
          return;
        }

        const controller = new AbortController();
        inflightRef.current.add(controller);
        try {
          const response = await suggestCommodityCategories(chunk, controller.signal);
          if (generationRef.current !== generation) {
            return;
          }
          if (!response.aiAvailable) {
            // Постоянное предупреждение (состояние, а не событие — ADR-028 п. 4).
            setAiWarning(true);
          }
          applySuggestionItems(response.items);
          setProgress((prev) => ({ ...prev, processed: prev.processed + chunk.length }));
        } catch (error) {
          if (generationRef.current !== generation || isAbortError(error)) {
            return; // отмена (cancel/save/unmount) — частичные действия не применяются
          }
          // Сбой порции не ломает страницу и остальные порции (деградация, ADR-028 п. 4).
          const message =
            error instanceof Error && error.message
              ? error.message
              : 'Не удалось определить категории этой порции.';
          markChunkFailed(chunk, message);
          setProgress((prev) => ({ ...prev, processed: prev.processed + chunk.length }));
        } finally {
          inflightRef.current.delete(controller);
        }
      }
    },
    [applySuggestionItems, markChunkFailed],
  );

  /**
   * Запуск режима проверки: фиксируется снапшот строок; во время проверки список
   * не перезапрашивается (стабильность порядка, ADR-028 п. 7).
   */
  const start = useCallback(
    (rows: CommodityItem[]) => {
      const generation = invalidate();
      categoryKeyByIdRef.current = new Map(categories.map((category) => [category.id, category.key]));

      const initial: Record<string, DraftEntry> = {};
      const candidates: CommodityItem[] = [];
      for (const row of rows) {
        const hasMeaningfulCategory = row.categoryId != null && row.categoryId !== 0;
        if (hasMeaningfulCategory) {
          // Уже категоризированная строка: значение предзаполнено, «уже присвоена»,
          // в порции не участвует (0/Undefined трактуется как «категории нет», ADR-028).
          const key = categoryKeyByIdRef.current.get(row.categoryId as number) ?? '';
          initial[row.id] = { value: key, initialValue: key, source: 'existing', touched: false };
        } else {
          initial[row.id] = { value: '', initialValue: '', touched: false };
          candidates.push(row);
        }
      }

      setSnapshot(rows);
      setDraft(initial);
      setProgress({ processed: 0, total: candidates.length });
      setAiWarning(false);
      setPhase('review');

      const chunks = buildChunks(candidates);
      if (chunks.length > 0) {
        void runChunks(chunks, generation);
      }
    },
    [categories, invalidate, runChunks],
  );

  /** Ручная правка: значение остаётся пользовательским, подпись — «изменено вручную». */
  const setValue = useCallback((commodityId: string, value: string) => {
    setDraft((prev) => {
      const entry = prev[commodityId];
      if (!entry || (entry.value === value && entry.touched)) {
        return prev;
      }
      return { ...prev, [commodityId]: { ...entry, value, touched: true } };
    });
  }, []);

  /**
   * Точечный повтор для одной строки (C2, ADR-028 п. 1.3): тот же suggest-эндпоинт
   * с одним commodityId, без перезапуска страницы и без автоматического сохранения.
   * <p>
   * Правило прогресса (фиксируем, T-13): X из Y отражает только исходный цикл порций;
   * повтор не изменяет processed/total (иначе счётчик «рос бы обратно»).
   */
  const retryRow = useCallback(
    async (commodityId: string) => {
      if (phaseRef.current !== 'review') {
        return;
      }

      const entry = draftRef.current[commodityId];
      if (!entry) {
        return;
      }
      // Повтор не затирает ручную правку со значением.
      if (entry.touched && entry.value !== '') {
        return;
      }

      const generation = generationRef.current;
      const controller = new AbortController();
      inflightRef.current.add(controller);
      try {
        const response = await suggestCommodityCategories([commodityId], controller.signal);
        if (generationRef.current !== generation) {
          return;
        }
        if (!response.aiAvailable) {
          setAiWarning(true);
        }
        applySuggestionItems(response.items);
        // Строка не получила записи (например, id исчез из выборки) — помечаем «не определена».
        if (!response.items.some((item) => item.commodityId === commodityId)) {
          markChunkFailed([commodityId], 'Строка больше недоступна для категоризации.');
        }
      } catch (error) {
        if (generationRef.current !== generation || isAbortError(error)) {
          return;
        }
        // Ошибка повтора — только в title строки; остальные строки и сохранение не блокируются.
        markChunkFailed(
          [commodityId],
          error instanceof Error && error.message ? error.message : 'Не удалось повторить категоризацию строки.',
        );
      } finally {
        inflightRef.current.delete(controller);
      }
    },
    [applySuggestionItems, markChunkFailed],
  );

  /**
   * Сохранение в любой момент (C2/C3): in-flight порция прерывается, сохраняется то,
   * что видит администратор. Отправляются только changed (value !== initialValue и
   * value !== '' — пустое «не трогаем» не уходит в PUT, C4).
   */
  const save = useCallback(async () => {
    if (phaseRef.current !== 'review') {
      return;
    }

    const changed = Object.entries(draftRef.current)
      .filter(([, entry]) => entry.value !== entry.initialValue && entry.value !== '')
      .map(([commodityId, entry]) => ({ commodityId, category: entry.value }));

    if (changed.length === 0) {
      // ADR-020: N = 0 — тост всё равно показывается; запрос не отправляется.
      invalidate();
      resetState();
      toast.success('Сохранено категорий: 0');
      return;
    }

    const epoch = invalidate();
    setPhase('saving');

    const controller = new AbortController();
    inflightRef.current.add(controller);
    try {
      const result = await saveCommodityCategories(changed, controller.signal);
      if (generationRef.current !== epoch) {
        return; // страницу закрыли/перезапустили, пока шло сохранение
      }
      resetState();
      onSavedRef.current(result.updated);
    } catch (error) {
      if (generationRef.current !== epoch || isAbortError(error)) {
        return;
      }
      // Черновик сохраняется (ADR-028 п. 5, «Ошибки» задачи): можно повторить сохранение.
      setPhase('review');
      toast.error(saveErrorMessage(error));
    } finally {
      inflightRef.current.delete(controller);
    }
  }, [invalidate, resetState, toast]);

  /** Отмена: черновик выбрасывается, в БД ничего не пишется, блокировки снимаются. */
  const cancel = useCallback(() => {
    invalidate();
    resetState();
  }, [invalidate, resetState]);

  const changedCount = useMemo(
    () => Object.values(draft).filter((entry) => entry.value !== entry.initialValue && entry.value !== '').length,
    [draft],
  );

  return {
    phase,
    snapshot,
    draft,
    progress,
    aiWarning,
    changedCount,
    isDirty: changedCount > 0,
    start,
    setValue,
    retryRow,
    save,
    cancel,
  };
}
