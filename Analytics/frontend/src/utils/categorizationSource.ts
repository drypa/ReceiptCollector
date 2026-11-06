import type { CategorizationSource } from '../types/commodity';

/**
 * Подпись-источник предложения категории (ADR-028 п. 9, C8): общий модуль для страниц
 * «Детали чека» (поведение не меняется — перенос дословно из ReceiptDetails.tsx) и
 * «Товары» (подпись у каждой строки, включая повторяющиеся названия).
 * Аддитивный источник `'merchant-default'` — категория по умолчанию магазина (ADR-032, п. 7);
 * существующие подписи и fallback для неизвестного source прежние.
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
