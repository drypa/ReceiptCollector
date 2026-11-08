import type {
  CategorizationDecisionsFilter,
  CategorizationDecisionItem,
  PaginatedCategorizationDecisions,
} from '../types/categorizationDecision';

interface FetchCategorizationDecisionsOptions extends CategorizationDecisionsFilter {
  limit: number;
  offset: number;
  signal?: AbortSignal;
}

/**
 * Страница уникальных сочетаний журнала (ADR-033 п. 7, FR-6/FR-7):
 * `GET /api/categorization-decisions`, общее количество — из `X-Total-Count`
 * (число уникальных сочетаний под фильтром, а не число строк выдачи).
 */
export async function fetchCategorizationDecisions({
  limit,
  offset,
  onlyMismatch,
  source,
  suggestedCategoryId,
  finalCategoryId,
  search,
  from,
  to,
  signal,
}: FetchCategorizationDecisionsOptions): Promise<PaginatedCategorizationDecisions> {
  const searchParams = new URLSearchParams({
    limit: limit.toString(),
    offset: offset.toString(),
  });

  // Отправляем только содержательные значения (прецедент precategorizedCommodities.ts):
  // присутствующий, но пустой source/from/to сервер считает невалидным (400, проверено
  // тестами T-08); onlyMismatch=false — серверный дефолт, тоже не отправляем.
  if (onlyMismatch) {
    searchParams.set('onlyMismatch', 'true');
  }
  if (source) {
    searchParams.set('source', source);
  }
  if (suggestedCategoryId != null) {
    searchParams.set('suggestedCategory', suggestedCategoryId.toString());
  }
  // Для итоговой категории код 0 («Не указана») допустим (ADR-033 п. 7), поэтому проверка
  // «не выбрано» — `!= null`, а не проверка истинности (0 иначе молча терялся бы).
  if (finalCategoryId != null) {
    searchParams.set('finalCategory', finalCategoryId.toString());
  }
  if (search) {
    searchParams.set('search', search);
  }
  const fromIso = toIsoBoundary(from, false);
  if (fromIso) {
    searchParams.set('from', fromIso);
  }
  const toIso = toIsoBoundary(to, true);
  if (toIso) {
    searchParams.set('to', toIso);
  }

  const response = await fetch(`/api/categorization-decisions?${searchParams.toString()}`, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    throw new Error(await readError(response, 'Не удалось загрузить журнал категоризации'));
  }

  const items = (await response.json()) as CategorizationDecisionItem[];
  const totalHeader = response.headers.get('X-Total-Count');
  const parsedTotal = totalHeader ? Number.parseInt(totalHeader, 10) : Number.NaN;
  // Fallback без заголовка — offset + размер страницы (прецедент precategorizedCommodities.ts).
  const totalItems = Number.isFinite(parsedTotal) ? parsedTotal : offset + items.length;

  return {
    items,
    totalItems,
    pageSize: limit,
    currentPage: Math.max(1, Math.floor(offset / limit) + 1),
  };
}

/**
 * Конвертация целого дня из `<input type="date">` (`yyyy-MM-dd`) в момент ISO 8601.
 * Сервер сравнивает моменты: `created_at >= from`, `created_at < to` (ADR-033 п. 7),
 * а пользователь выбирает целые дни в таймзоне браузера:
 * - `from` — начало выбранного дня;
 * - `to` — начало СЛЕДУЮЩЕГО дня: из-за строгого `<` иначе выбранный последний день
 *   целиком выпадал бы из периода.
 * Форма `yyyy-MM-ddT00:00:00` без смещения парсится как локальное время браузера
 * (ECMAScript), наружу отдаётся UTC (`Z`) — момент не зависит от таймзоны сервера.
 * Непарсивое значение — как не заданное (просить 400 у сервера не за что).
 */
function toIsoBoundary(value: string | undefined, isRangeEnd: boolean): string | undefined {
  if (!value) {
    return undefined;
  }

  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) {
    return undefined;
  }

  if (isRangeEnd) {
    date.setDate(date.getDate() + 1);
  }

  return date.toISOString();
}

async function readError(response: Response, fallback: string): Promise<string> {
  // Ошибки API — текстовые (ADR-033 п. 7, без ProblemDetails). 401/403 поясняем
  // понятнее, чем сырой текст сервера (прецедент precategorizedCommodities.ts).
  if (response.status === 403) {
    return 'Недостаточно прав для просмотра журнала.';
  }

  if (response.status === 401) {
    return 'Сессия истекла. Войдите снова.';
  }

  const text = (await response.text().catch(() => '')).trim();
  return text && !text.startsWith('<') ? text : fallback;
}
