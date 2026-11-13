/**
 * Контракт админского раздела «Предкатегоризация» (ADR-027 п.7, FR-7).
 * Имена категории/статуса приходят с сервера (wire-имена proposed/applied).
 */
export interface PrecategorizedCommodityItem {
  id: string;
  name: string;
  merchantId: string;
  merchantName: string | null;
  proposedCategoryId: number;
  proposedCategoryName: string;
  status: string;
  createdAt: string;
}

export interface PaginatedPrecategorizedCommodities {
  items: PrecategorizedCommodityItem[];
  totalItems: number;
  pageSize: number;
  currentPage: number;
}

/**
 * Действие массовой операции страницы «Предкатегоризация» (ADR-036 §5):
 * подтверждение или отклонение всех строк текущей видимой страницы.
 */
export type PrecategorizedBulkAction = 'confirm' | 'decline';

/**
 * Исход строки в массовом отчёте (ADR-036 §1.1/§1.2). Wire-имена — lowercase
 * (стиль статусов/источников проекта): `applied` и `declined` — решение по строке
 * состоялось, `skipped` — строка пропущена с причиной `reason`.
 */
export type PrecategorizedBulkItemOutcome = 'applied' | 'declined' | 'skipped';

/**
 * Причина пропуска строки (ADR-036 §1.1/§1.2): встречается только у `skipped`.
 * `already-applied` — строку уже подтвердили в другом окне, `not-found` — строку
 * уже удалили или она исчезла между отрисовкой страницы и запуском операции.
 */
export type PrecategorizedBulkSkipReason = 'already-applied' | 'not-found';

/**
 * Строка построчного отчёта массового подтверждения (ADR-036 §1.1).
 * `categoryId`/`categoryName` — фактически применённая категория из снимка строки (D1),
 * `updatedCommodities` — число обновлённых товаров (может быть 0: решение всё равно
 * состоялось, D10 ADR-027). У `skipped`-строк эти поля не заполняются сервером
 * (`null` или отсутствуют) — клиент ориентируется на `outcome`.
 */
export interface PrecategorizedBulkConfirmItem {
  id: string;
  outcome: PrecategorizedBulkItemOutcome;
  reason?: PrecategorizedBulkSkipReason | null;
  categoryId?: number | null;
  categoryName?: string | null;
  updatedCommodities?: number | null;
}

/**
 * Ответ `POST /api/precategorized-commodities/batch/confirm` (ADR-036 §1.1):
 * агрегат и построчный отчёт в порядке запроса. Инвариант ответа:
 * `applied + skipped = requested`; `updatedCommodities` в агрегате — сумма
 * по `applied`-строкам.
 */
export interface PrecategorizedBulkConfirmResult {
  requested: number;
  applied: number;
  skipped: number;
  updatedCommodities: number;
  results: PrecategorizedBulkConfirmItem[];
}

/**
 * Строка построчного отчёта массового отклонения (ADR-036 §1.2).
 * Поля `updatedCommodities` нет намеренно: отклонение не меняет товары (асимметрия
 * подтверждена ADR). У `skipped` заполняется только `reason`.
 */
export interface PrecategorizedBulkDeclineItem {
  id: string;
  outcome: PrecategorizedBulkItemOutcome;
  reason?: PrecategorizedBulkSkipReason | null;
}

/**
 * Ответ `POST /api/precategorized-commodities/batch/decline` (ADR-036 §1.2):
 * агрегат и построчный отчёт в порядке запроса. Инвариант ответа:
 * `declined + skipped = requested`.
 */
export interface PrecategorizedBulkDeclineResult {
  requested: number;
  declined: number;
  skipped: number;
  results: PrecategorizedBulkDeclineItem[];
}
