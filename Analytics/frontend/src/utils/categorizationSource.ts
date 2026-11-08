import type { CategorizationSource } from '../types/commodity';

/**
 * Подпись-источник предложения категории (ADR-028 п. 9, C8): общий модуль для страниц
 * «Детали чека» (поведение не меняется — перенос дословно из ReceiptDetails.tsx) и
 * «Товары» (подпись у каждой строки, включая повторяющиеся названия).
 * Аддитивный источник `'merchant-default'` — категория по умолчанию магазина (ADR-032, п. 7);
 * существующие подписи и fallback для неизвестного source прежние.
 * <p>
 * Здесь же — общее правило снимка предложения для журнала решений (ADR-033 п. 3/4, T-09/T-10),
 * чтобы набор допустимых источников и условия снимка не разъезжались между страницами.
 */
export function getSuggestionNote(source: CategorizationSource | string): string | null {
  switch (source) {
    case 'cache':
      return 'по прошлым чекам';
    case 'ai':
      return 'предложено ИИ';
    case 'existing':
      return 'уже присвоена';
    case 'precategorized':
      return 'из очереди предкатегорий';
    case 'merchant-default':
      return 'по умолчанию магазина';
    case 'undefined':
      return 'не определена';
    default:
      return null;
  }
}

/**
 * Подпись строки черновика страницы «Товары»: ручная правка перекрывает источник
 * (поздняя порция не должна «молча стирать» введённое — ADR-028 п. 2).
 */
export function getDraftNote(entry: { source?: string; touched: boolean }): string | null {
  if (entry.touched) {
    return 'изменено вручную';
  }
  return entry.source ? getSuggestionNote(entry.source) : null;
}

/**
 * Источники, которые журнал решений считает предложением категории (ADR-033 п. 3,
 * «Явно зафиксированные семантики»): `existing` (категория уже была у позиции) и
 * `undefined` (предложения нет) снимка не дают — по ним события не пишутся (FR-1/FR-5b).
 * Набор — ровно четыре wire-имени, как у серверного `CategorizationDecisionSources`;
 * проверяется только {@link buildSuggestionSnapshot} (модульно-приватный: снаружи нужен
 * не перечень, а правило снимка).
 */
const SUGGESTION_SNAPSHOT_SOURCES: ReadonlySet<string> = new Set([
  'ai',
  'precategorized',
  'cache',
  'merchant-default',
]);

/**
 * Снимок предложения в сохраняемом элементе (ADR-033 п. 3/4, решение A1):
 * `suggestedCategory` — ключ категории (имя enum, как `category` в save-контрактах),
 * `suggestionSource` — wire-имя источника.
 */
export interface SuggestionSnapshot {
  suggestedCategory: string;
  suggestionSource: string;
}

/**
 * Правило снимка — единое для страницы чека и страницы «Товары» (ADR-033 п. 3/4):
 * снимок есть **только** при валидном предложении, то есть когда категория определена
 * (`categoryId != null` и маппится в ключ справочника, `Undefined` — не категория)
 * и источник входит в набор допустимых (см. {@link SUGGESTION_SNAPSHOT_SOURCES}).
 * Иначе `null` — поля снимка в payload не попадают (FR-5b, NFR-2).
 * <p>
 * Проверки самодостаточны: четвёрка источников и отсечение `Undefined` выполняются здесь,
 * поэтому будущий вызывающий с полным справочником категорий не может получить
 * `suggestedCategory: 'Undefined'` (сервер такой снимок отбросил бы с Warning).
 *
 * @param suggestion       предложение по строке (`{ categoryId, source }`), может отсутствовать;
 * @param categoryKeyById  справочник `id -> key` (обычно без `Undefined`, но это не обязательно).
 */
export function buildSuggestionSnapshot(
  suggestion: { categoryId: number | null; source: string } | null | undefined,
  categoryKeyById: ReadonlyMap<number, string>,
): SuggestionSnapshot | null {
  if (
    !suggestion ||
    suggestion.categoryId == null ||
    !SUGGESTION_SNAPSHOT_SOURCES.has(suggestion.source)
  ) {
    return null;
  }

  const suggestedCategory = categoryKeyById.get(suggestion.categoryId);
  // `Undefined` («Не указана») — не категория: suggestedCategory не может быть 0/Undefined.
  if (!suggestedCategory || suggestedCategory === 'Undefined') {
    return null;
  }

  return { suggestedCategory, suggestionSource: suggestion.source };
}
