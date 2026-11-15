import { useCallback, useEffect, useRef, useState } from 'react';
import { confirmPrecategorizedBatch, declinePrecategorizedBatch } from '../api/precategorizedCommodities';
import type {
  PrecategorizedBulkAction,
  PrecategorizedBulkItemOutcome,
  PrecategorizedBulkSkipReason,
  PrecategorizedCommodityItem,
} from '../types/precategorizedCommodity';

/**
 * Итог завершённой массовой операции (ADR-036 §5 «Итоговая сводка»): только счётчики
 * и сгруппированные причины пропусков — без списка id и без какого-либо прогресса
 * (решение заказчика 5). `applied`/`updatedCommodities` заполняются для подтверждения,
 * `declined` — для отклонения (асимметрия контрактов §1.1/§1.2 намеренная).
 */
export interface BulkOperationSummary {
  action: PrecategorizedBulkAction;
  applied?: number;
  declined?: number;
  skipped: number;
  updatedCommodities?: number;
  reasons: Partial<Record<PrecategorizedBulkSkipReason, number>>;
}

interface UsePrecategorizedBulkOperationsOptions {
  /**
   * Вызывается в `finally` после любой завершённой операции — успех это или ошибка
   * (ADR-036 §5): страница делает `refresh()` текущей таблицы, чтобы показать
   * актуальное состояние очереди.
   */
  onCompleted: () => void;
}

/**
 * Строка построчного отчёта обеих массовых операций в объёме, нужном для группировки
 * причин: `outcome` + `reason`. Оба wire-типа (`…ConfirmItem`/`…DeclineItem`)
 * структурно подходят.
 */
interface BulkReportItem {
  outcome: PrecategorizedBulkItemOutcome;
  reason?: PrecategorizedBulkSkipReason | null;
}

/**
 * Резервный текст сбоя на случай исключения без сообщения (неожиданный тип ошибки).
 * Штатный текст «Операция не выполнена, изменения не применены.» формирует API-клиент
 * для 5xx/сети (ADR-036 §1.5) — серверный английский текст в состояние не попадает.
 */
const BULK_OPERATION_FALLBACK_ERROR = 'Операция не выполнена, изменения не применены.';

/**
 * Агрегация причин пропусков построчного отчёта в счётчики (ADR-036 §5): ключи
 * `already-applied`/`not-found` появляются только при фактических пропусках с этой
 * причиной; строки без `reason` (контрактом не предусмотрены) в причины не попадают.
 */
function groupSkipReasons(results: BulkReportItem[]): Partial<Record<PrecategorizedBulkSkipReason, number>> {
  const reasons: Partial<Record<PrecategorizedBulkSkipReason, number>> = {};

  for (const item of results) {
    if (item.outcome !== 'skipped' || !item.reason) {
      continue;
    }
    reasons[item.reason] = (reasons[item.reason] ?? 0) + 1;
  }

  return reasons;
}

/**
 * Состояние массовой операции страницы «Предкатегоризация» (ADR-036 §5):
 * снимок `items` в момент подтверждения диалога, РОВНО ОДИН запрос на операцию,
 * busy-флаг без чисел, итоговая сводка с причинами пропусков, ошибка и
 * автообновление списка через `onCompleted()` в `finally`.
 * <p>
 * Клиентская логика минимальна по решению заказчика 4–5 и ADR-036 A3: никаких
 * порций, счётчиков прогресса, повторных запросов и отмены — вся страница
 * обрабатывается на сервере одной транзакцией. Хук не показывает тосты и не
 * управляет диалогом — это презентация компонента (задача 07).
 */
export function usePrecategorizedBulkOperations({ onCompleted }: UsePrecategorizedBulkOperationsOptions) {
  const [isRunning, setIsRunning] = useState(false);
  const [summary, setSummary] = useState<BulkOperationSummary | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Синхронный guard против двойного нажатия: setState асинхронен, и без ref второй
  // клик в том же тике ушёл бы вторым запросом (edge case «одновременное нажатие
  // двух кнопок» — вторая операция невозможна, пока идёт первая).
  const runningRef = useRef(false);
  // Колбэк страницы может меняться между рендерами — дёргаем свежий, не пересобирая run().
  const onCompletedRef = useRef(onCompleted);

  useEffect(() => {
    onCompletedRef.current = onCompleted;
  }, [onCompleted]);

  /**
   * Запуск массовой операции: `items` — строки текущей страницы, переданные
   * в момент подтверждения диалога; id снимаются сразу (`items.map(i => i.id)`)
   * и дальше не меняются, список во время операции не перезапрашивается (§5).
   * Повторный вызов во время работы — no-op.
   */
  const run = useCallback(async (action: PrecategorizedBulkAction, items: PrecategorizedCommodityItem[]) => {
    if (runningRef.current) {
      return;
    }

    const ids = items.map((item) => item.id);
    // Флаг ставится сразу после снимка и до первого await: в однопоточном JS второй
    // вызов не может вклиниться между проверкой и установкой, а гипотетический
    // синхронный бросок map не оставляет «залипший» runningRef = true.
    runningRef.current = true;

    setIsRunning(true);
    setSummary(null);
    setError(null);

    try {
      // Ровно один запрос на операцию (A3): весь снимок id уходит целиком,
      // сервер применяет его одной транзакцией и отдаёт итоговый отчёт.
      if (action === 'confirm') {
        const result = await confirmPrecategorizedBatch(ids);
        setSummary({
          action,
          applied: result.applied,
          skipped: result.skipped,
          updatedCommodities: result.updatedCommodities,
          reasons: groupSkipReasons(result.results),
        });
      } else {
        const result = await declinePrecategorizedBatch(ids);
        setSummary({
          action,
          declined: result.declined,
          skipped: result.skipped,
          reasons: groupSkipReasons(result.results),
        });
      }
    } catch (runError) {
      // Технический сбой (5xx/сеть): по решению 6 не применено ничего, текст уже
      // локализован API-клиентом («изменения не применены»), серверный не подставляем.
      setError(
        runError instanceof Error && runError.message
          ? runError.message
          : BULK_OPERATION_FALLBACK_ERROR,
      );
    } finally {
      runningRef.current = false;
      setIsRunning(false);
      // Автообновление списка и после успеха, и после ошибки (§5): админ видит
      // актуальную очередь, повторный запуск безопасен.
      onCompletedRef.current();
    }
  }, []);

  /**
   * Очистка сводки и ошибки (без отмены работающей операции — её всё равно
   * отменить нельзя, а запрос один). Вызывается страницей при смене страницы/поиска,
   * чтобы итог прошлой операции не относился к новому набору строк (задача 08).
   */
  const reset = useCallback(() => {
    setSummary(null);
    setError(null);
  }, []);

  return { isRunning, summary, error, run, reset };
}
