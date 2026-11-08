/**
 * Контракты админского раздела «Журнал категоризации» (ADR-033 п. 7/п. 8, FR-6/FR-7).
 * Строка выдачи — уникальное сочетание «название + источник + предложено + принято»
 * (дедупликация выполняется на сервере, FR-7); «кто принял» и decidedAt — от последнего
 * события сочетания внутри отфильтрованного периода. Счётчиков и агрегатов в контракте нет.
 */

/**
 * Допустимые wire-имена источника предложения в журнале — ровно четыре (FR-1/FR-2,
 * серверный `CategorizationDecisionSources`): `existing` и `undefined` предложениями
 * не являются и в журнал не пишутся (FR-5b).
 */
export type CategorizationDecisionSource = 'ai' | 'precategorized' | 'cache' | 'merchant-default';

export interface CategorizationDecisionItem {
  /** Исходное название товара, как в чеке — входит в ключ сочетания («Хлеб»/«ХЛЕБ» — разные строки). */
  commodityName: string;
  /** Нормализованное название (`CommodityNameNormalizer.NormalizeName`). */
  normalizedName: string;
  /**
   * Wire-имя источника предложения; намеренно шире union (как в suggest-контрактах) —
   * устойчивость к будущим аддитивным значениям сервера.
   */
  suggestionSource: CategorizationDecisionSource | string;
  suggestedCategoryId: number;
  /** Имя из справочника (безопасное чтение): известный код — отображаемое имя, удалённый — «—». */
  suggestedCategoryName: string;
  /** Итог; 0 = «Не указана» (сброс категории после предложения — решение с итогом Undefined). */
  finalCategoryId: number;
  /** Имя итоговой категории из справочника («Не указана» для кода 0, «—» для удалённого кода). */
  finalCategoryName: string;
  acceptedById: string;
  /** null — пользователь удалён или без имени: в UI пусто, id и admin-флаг при этом всегда есть (F1). */
  acceptedByName: string | null;
  /** Снимок `users.is_admin` на момент решения (заполнен всегда). */
  acceptedByAdmin: boolean;
  /**
   * Момент последнего события сочетания — ISO 8601 **со смещением** (`+00:00`),
   * а не строго `Z` (проверено в T-08): парсить `new Date(...)`, не ждать `Z`.
   */
  decidedAt: string;
}

/**
 * Фильтры списка журнала (ADR-033 п. 7). Пагинация (`limit`/`offset`) передаётся
 * отдельно в api-функции. Пустые значения не отправляются: присутствующий, но пустой
 * `source`/`from`/`to` сервер считает невалидным (`400`), поэтому «не выбрано» — это
 * отсутствие параметра, а не пустая строка.
 */
export interface CategorizationDecisionsFilter {
  /** `true` — только расхождения (`suggested != final`); `false` — серверный дефолт, не отправляется. */
  onlyMismatch?: boolean;
  /** Wire-имя источника; `undefined` — без фильтра. */
  source?: CategorizationDecisionSource;
  /** Код предложенной категории (> 0 — «Не указана» для предложения недопустима). */
  suggestedCategoryId?: number;
  /** Код итоговой категории; 0 = «Не указана» допустим (в отличие от предложения). */
  finalCategoryId?: number;
  /** Подстрока по исходному ИЛИ нормализованному названию (серверный ILIKE). */
  search?: string;
  /** Начало периода: `yyyy-MM-dd` (как `<input type="date">`); конвертация в момент — в api. */
  from?: string;
  /** Конец периода: `yyyy-MM-dd`; выбранный день включается (api отдаёт серверу `< следующий день`). */
  to?: string;
}

export interface PaginatedCategorizationDecisions {
  items: CategorizationDecisionItem[];
  /** `X-Total-Count` — число уникальных сочетаний под фильтром (FR-7), не число сырых событий. */
  totalItems: number;
  pageSize: number;
  currentPage: number;
}
