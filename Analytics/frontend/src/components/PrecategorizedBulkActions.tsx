import { useCallback, useEffect, useRef, useState } from 'react';
import type {
  PrecategorizedBulkAction,
  PrecategorizedBulkSkipReason,
  PrecategorizedCommodityItem,
} from '../types/precategorizedCommodity';
import type { BulkOperationSummary } from '../hooks/usePrecategorizedBulkOperations';
import { CustomDialog } from './CustomDialog';
import { useToast } from './Toasts';

/**
 * Пропсы массовых действий страницы «Предкатегоризация». Компонент **не знает о
 * запросах** (ADR-036 §5, инвариант 10 плана): всё состояние выполнения приходит
 * из хука `usePrecategorizedBulkOperations` (задача 06), который страница
 * подключает через `onRun` (задача 08).
 */
interface PrecategorizedBulkActionsProps {
  /** Строки текущей видимой страницы — область действия обеих операций (решение заказчика 2). */
  items: PrecategorizedCommodityItem[];
  /** Идёт массовая операция (флаг хука): обе кнопки блокируются одновременно. */
  isRunning: boolean;
  /** Итог завершённой операции; `null`, пока операция не завершена или после `reset()`. */
  summary: BulkOperationSummary | null;
  /** Текст сбоя завершённой операции (локализован API-клиентом, ADR-036 §1.5). */
  error: string | null;
  /** Страница загружает список — массовые операции недоступны. */
  isLoading?: boolean;
  /** Список не загрузился — массовые операции недоступны. */
  loadError?: boolean;
  /** Дополнительная внешняя блокировка (задача 08), поверх собственных условий. */
  disabled?: boolean;
  /**
   * Запуск массовой операции — подключается к `run()` хука задачи 06
   * (снимок id берётся в момент вызова, ровно один запрос на операцию).
   * Возврат может быть promise'ом: вызов оборачивается так, чтобы необработанного
   * rejection не было даже если `onCompleted`/`refresh` страницы бросит
   * (замечание ревью к задаче 06).
   */
  onRun: (action: PrecategorizedBulkAction, items: PrecategorizedCommodityItem[]) => void | Promise<unknown>;
}

/**
 * Человекочитаемые причины пропусков (ADR-036 §5): wire-причина → текст сводки.
 * Порядок перебора фиксирован — сводка стабильна между ре-рендерами.
 */
const SKIP_REASON_TITLES: Record<PrecategorizedBulkSkipReason, string> = {
  'already-applied': 'уже обработаны в другом окне',
  'not-found': 'больше не существуют',
};
const SKIP_REASON_ORDER: PrecategorizedBulkSkipReason[] = ['already-applied', 'not-found'];

/**
 * Дословные тексты диалога подтверждения (ADR-036 §5): `N` = `items.length`
 * (строки текущей страницы, не `X-Total-Count`).
 */
function dialogMessage(action: PrecategorizedBulkAction, count: number): string {
  return action === 'confirm'
    ? `Подтвердить все ${count} предложений на этой странице? Категории будут применены ко всем товарам с этими названиями у всех пользователей.`
    : `Отклонить все ${count} предложений на этой странице? Предложения будут удалены безвозвратно. Категории товаров не изменятся.`;
}

/**
 * Главная строка сводки (ADR-036 §5): «Подтверждено: N, пропущено: M»
 * (+ «обновлено товаров: K» для подтверждения) / «Отклонено: N, пропущено: M».
 */
function summaryLine(summary: BulkOperationSummary): string {
  return summary.action === 'confirm'
    ? `Подтверждено: ${summary.applied ?? 0}, пропущено: ${summary.skipped}, обновлено товаров: ${summary.updatedCommodities ?? 0}`
    : `Отклонено: ${summary.declined ?? 0}, пропущено: ${summary.skipped}`;
}

/**
 * Агрегированные причины пропусков (без списка id, ADR-036 §5); пустая строка,
 * если пропусков нет.
 */
function skipReasonsLine(summary: BulkOperationSummary): string {
  const parts = SKIP_REASON_ORDER
    .map((reason) => ({ reason, count: summary.reasons[reason] ?? 0 }))
    .filter(({ count }) => count > 0)
    .map(({ reason, count }) => `${SKIP_REASON_TITLES[reason]} — ${count}`);

  return parts.length > 0 ? `Причины пропусков: ${parts.join(', ')}` : '';
}

/**
 * Кнопки «Подтвердить все» / «Отклонить все» с диалогом подтверждения, busy-строкой
 * без прогресса и итоговой сводкой (UX-контракт ADR-036 §5).
 * <p>
 * Компонент презентационный: один запрос на операцию, снимок страницы и
 * автообновление списка живёт в хуке задачи 06; здесь — только диалог
 * (`CustomDialog`, без busy-режима — YAGNI), busy-строка `role="status"` без
 * чисел, сводка inline + дублирование тостом по конвенции ADR-020 (тост ровно
 * один раз на завершённую операцию, без повторов на ре-рендерах).
 */
export function PrecategorizedBulkActions({
  items,
  isRunning,
  summary,
  error,
  isLoading = false,
  loadError = false,
  disabled = false,
  onRun,
}: PrecategorizedBulkActionsProps) {
  const toast = useToast();
  // Один экземпляр диалога с состоянием { action } (ADR-036 §5); null — диалог закрыт.
  const [pendingAction, setPendingAction] = useState<PrecategorizedBulkAction | null>(null);
  // Действие последней запущенной операции — нужно только для текста busy-строки
  // («Подтверждаем…» / «Отклоняем…»); во время операции второй запуск невозможен.
  const [lastAction, setLastAction] = useState<PrecategorizedBulkAction | null>(null);
  // Тост привязан к переходу running → idle (а не к наличию summary/error):
  // no-op запуск (guard хука от двойного нажатия) перехода не создаёт — тоста
  // нет; ре-рендеры со свежей сводкой перехода не содержат — тост не повторяется.
  const wasRunningRef = useRef(false);

  const buttonsDisabled = disabled || isLoading || loadError || isRunning || items.length === 0;

  // Причина недоступности кнопок (ADR-036 §5): приоритет — от «операция
  // выполняется» к «страница пуста», чтобы самое важное сообщение было первым.
  const buttonsTitle = isRunning
    ? 'Дождитесь завершения массовой операции'
    : isLoading
      ? 'Список предложений загружается'
      : loadError
        ? 'Не удалось загрузить предложения'
        : items.length === 0
          ? 'На этой странице нет предложений'
          : disabled
            ? 'Массовые операции временно недоступны'
            : undefined;

  const openDialog = useCallback((action: PrecategorizedBulkAction) => {
    setPendingAction(action);
  }, []);

  const closeDialog = useCallback(() => {
    // «Отмена»/закрытие — никаких запросов, ничего не меняется (критерий приёмки).
    setPendingAction(null);
  }, []);

  const handleDialogConfirm = useCallback(() => {
    if (!pendingAction) {
      return;
    }

    const action = pendingAction;
    // Диалог закрывается сразу: busy-состояние живёт в хуке (у CustomDialog
    // busy-режима нет — YAGNI, ADR-036 §5).
    setPendingAction(null);
    setLastAction(action);

    // Снимок строки страницы передаётся в хук (id снимаются там же, в момент
    // вызова, — ровно один запрос на операцию). onRun подключён к run() хука
    // 06: если onCompleted (refresh страницы) бросит, хук уже сбросил isRunning
    // в finally, но rejected promise до React должен быть погашен здесь
    // (замечание ревью к задаче 06) — состояние хука от этого не страдает.
    // Сбой автообновления списка администратору не виден, поэтому гасим его не
    // молча, а логом (конвенция кодовой базы: CategorizationDecisionsPage,
    // MerchantTable).
    Promise.resolve(onRun(action, items)).catch((err) =>
      console.error('Ошибка автообновления списка после массовой операции:', err),
    );
  }, [items, onRun, pendingAction]);

  // Тост по завершении операции: success — со сводкой, error — с текстом сбоя
  // (конвенция ADR-020). Привязан к переходу running → idle: хук выставляет
  // summary/error в том же батче, что и гасит isRunning, поэтому на переходе
  // они уже актуальны; no-op запуск и ре-рендеры тост не повторяют.
  useEffect(() => {
    const wasRunning = wasRunningRef.current;
    wasRunningRef.current = isRunning;

    if (!wasRunning || isRunning) {
      return;
    }

    if (summary) {
      const reasons = skipReasonsLine(summary);
      toast.success(reasons ? `${summaryLine(summary)}. ${reasons}` : summaryLine(summary));
    } else if (error) {
      toast.error(error);
    }
  }, [isRunning, summary, error, toast]);

  // Причины пропусков считаем один раз на рендер (используются в сводке).
  const summaryReasons = summary ? skipReasonsLine(summary) : '';

  return (
    <div className="bulk-actions">
      <div className="controls">
        <button type="button" onClick={() => openDialog('confirm')} disabled={buttonsDisabled} title={buttonsTitle}>
          Подтвердить все
        </button>
        <button
          type="button"
          className="secondary"
          onClick={() => openDialog('decline')}
          disabled={buttonsDisabled}
          title={buttonsTitle}
        >
          Отклонить все
        </button>
      </div>

      {/* Busy-строка без чисел, процентов и «X из N» (решение заказчика 5).
          lastAction === null при isRunning практически недостижим (isRunning
          поднимается только запуском из этого компонента, который фиксирует
          действие); безопасный фолбэк — текст подтверждения. */}
      {isRunning && (
        <div className="bulk-busy" role="status" aria-live="polite">
          <span className="spinner" aria-hidden="true" />
          {lastAction === 'decline' ? 'Отклоняем предложения…' : 'Подтверждаем предложения…'}
        </div>
      )}

      {/* Итоговая сводка: inline, не исчезает сама; сохраняется до следующего
          действия или смены страницы (reset() из задачи 08). */}
      {summary && (
        <div className="bulk-summary" role="status" aria-live="polite">
          <span>{summaryLine(summary)}</span>
          {summaryReasons && <span className="bulk-summary-reasons">{summaryReasons}</span>}
        </div>
      )}

      {/* Технический сбой: по решению 6 не применено ничего; текст уже
          локализован («Операция не выполнена, изменения не применены.»). */}
      {error && (
        <div className="state state-error" role="alert">
          <p>{error}</p>
        </div>
      )}

      <CustomDialog
        isOpen={pendingAction !== null}
        title={pendingAction === 'decline' ? 'Отклонить все предложения?' : 'Подтвердить все предложения?'}
        message={pendingAction ? dialogMessage(pendingAction, items.length) : ''}
        confirmText={pendingAction === 'decline' ? 'Отклонить' : 'Подтвердить'}
        onConfirm={handleDialogConfirm}
        onClose={closeDialog}
      />
    </div>
  );
}
