import { getSuggestionNote } from '../utils/categorizationSource';
import type { CategorizationDecisionItem } from '../types/categorizationDecision';

interface CategorizationDecisionsTableProps {
  items: CategorizationDecisionItem[];
}

// Тот же формат даты, что в PrecategorizedCommodityTable (ru-RU, коротко).
const dateFormatter = new Intl.DateTimeFormat('ru-RU', {
  dateStyle: 'short',
  timeStyle: 'short',
});

/**
 * Таблица журнала «предложено → принято» (колонки — FR-6): исходное название,
 * нормализованное, предложено (категория + подпись источника из utils/categorizationSource),
 * принято, кто принял (имя или пусто + бейдж «админ»), дата решения.
 * Строки — уже дедуплицированы сервером (FR-7); событий в UI нет, только просмотр.
 */
export function CategorizationDecisionsTable({ items }: CategorizationDecisionsTableProps) {
  if (items.length === 0) {
    return (
      <div className="empty-state">
        <p>Нет событий по выбранным фильтрам.</p>
      </div>
    );
  }

  return (
    <div className="table-wrapper">
      <table>
        <thead>
          <tr>
            <th>Исходное название</th>
            <th>Нормализованное название</th>
            <th>Предложено</th>
            <th>Принято</th>
            <th>Кто принял</th>
            <th>Дата решения</th>
          </tr>
        </thead>
        <tbody>
          {items.map((row) => {
            // Подпись источника — общий модуль страниц категоризации; неизвестный источник
            // даёт null (fallback utils/categorizationSource) — строка остаётся с категорией.
            const note = getSuggestionNote(row.suggestionSource);
            return (
              <tr key={combinationKey(row)}>
                <td>{row.commodityName}</td>
                <td>{row.normalizedName}</td>
                <td>
                  <span className="item-category-name">{row.suggestedCategoryName}</span>
                  {note && (
                    <div className={`category-suggestion category-suggestion-${row.suggestionSource}`}>
                      {note}
                    </div>
                  )}
                </td>
                <td>
                  {/* Имя итога — из справочника на сервере: «Не указана» для кода 0,
                      «—» для удалённого из справочника кода (безопасное чтение ADR-033 п. 1). */}
                  <span
                    className={
                      row.finalCategoryId === 0
                        ? 'item-category-name item-category-name--empty'
                        : 'item-category-name'
                    }
                  >
                    {row.finalCategoryName}
                  </span>
                </td>
                <td>
                  <span className="accepted-by">
                    {/* Имя может быть недоступно (пользователь удалён/без имени) — ячейка пустая;
                        бейдж «админ» — снимок флага на момент решения, показываем всегда когда он есть (F1). */}
                    {row.acceptedByName}
                    {row.acceptedByAdmin && <span className="admin-badge">админ</span>}
                  </span>
                </td>
                <td>{formatDate(row.decidedAt)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/**
 * React key: уникальное сочетание «название + источник + предложено + принято» (FR-7) —
 * ровно то, чем сервер схлопывает события; id/decidedAt в контракте строки нет.
 * Разделитель заведомо не встречается в названиях товаров.
 */
function combinationKey(row: CategorizationDecisionItem): string {
  return [row.commodityName, row.suggestionSource, row.suggestedCategoryId, row.finalCategoryId].join('\u0000');
}

function formatDate(value: string): string {
  // decidedAt приходит как ISO 8601 со смещением (`+00:00`), а не строго `Z` (сводка T-08):
  // Date корректно разбирает оба варианта; непарсивое значение — «—», а не «Invalid Date».
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : dateFormatter.format(date);
}
