/**
 * Контракты админского раздела «Категории товаров» (ADR-035 п. 7/п. 8, FR-3/FR-4/FR-9).
 * `GET` возвращает полный снимок категорий (кроме `Undefined`) с обоими списками слов;
 * каждое слово несёт `id` строки — `DELETE` адресен только по нему (решение G1:
 * «id уже есть в выдаче GET и однозначен»).
 */

/** Wire-имя списка слов — закрытое множество (серверный `KeywordListTypeNames`, решение A1). */
export type KeywordListType = 'boost' | 'exclude';

/**
 * Слово списка в снимке: `id` — идентификатор строки для адресного `DELETE /{id}`,
 * `word` — как введено (после trim, регистр и внутренние символы сохранены, FR-5).
 */
export interface CategoryKeywordItem {
  id: string;
  word: string;
}

/** Строка категории со словами обоих списков — элемент `GET`-ответа (ADR-035 п. 7). */
export interface CategoryKeywords {
  /** Числовой код `CommodityCategory` (без FK, ADR-005). */
  categoryId: number;
  /** Имя элемента enum (например, `DriedFruits`) — стабильный ключ для UI. */
  categoryKey: string;
  /** Отображаемое имя из `CommodityCategoryHelper.GetDisplayName`. */
  categoryName: string;
  /** Группа из `CommodityCategoryHelper.GetGroup`; пустая строка — вне групп (плоский список). */
  group: string;
  boost: CategoryKeywordItem[];
  exclude: CategoryKeywordItem[];
}

/**
 * Тело `POST /api/commodity-category-keywords` (ADR-035 п. 7): адресная операция
 * «одно слово в один список одной категории».
 */
export interface AddKeywordRequest {
  categoryId: number;
  listType: KeywordListType;
  word: string;
}

/** Ответ `200` на `POST` — созданный элемент (слово после trim в исходном регистре). */
export interface AddedKeyword {
  id: string;
  categoryId: number;
  listType: KeywordListType;
  word: string;
}

/**
 * Лимит слов на список (FR-5, серверная константа `MaxWordsPerList`): UI блокирует
 * ввод на лимите, чтобы `409` не был сюрпризом (ADR-035 п. 8).
 */
export const MAX_WORDS_PER_LIST = 200;
