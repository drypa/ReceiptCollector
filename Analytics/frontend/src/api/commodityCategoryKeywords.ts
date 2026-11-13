import type {
  AddKeywordRequest,
  AddedKeyword,
  CategoryKeywords,
} from '../types/commodityCategoryKeyword';

const ENDPOINT = '/api/commodity-category-keywords';

/**
 * Ошибка API слов категоризации с HTTP-статусом: страница различает «адресные»
 * клиентские ошибки (4xx — показываем inline под затронутым списком) и общие
 * (сеть/5xx — баннер с кнопкой «Повторить») — ADR-035 п. 8.
 */
export class CommodityCategoryKeywordApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'CommodityCategoryKeywordApiError';
    this.status = status;
  }
}

/**
 * Полный снимок категорий со словами (`GET /api/commodity-category-keywords`, ADR-035 п. 7):
 * все категории кроме `Undefined`, включая пустые списки; `X-Total-Count` и пагинация не
 * вводятся (объём ограничен).
 */
export async function fetchCategoryKeywords(signal?: AbortSignal): Promise<CategoryKeywords[]> {
  const response = await fetch(ENDPOINT, {
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    throw new CommodityCategoryKeywordApiError(
      await readError(response, 'Не удалось загрузить списки слов'),
      response.status,
    );
  }

  return (await response.json()) as CategoryKeywords[];
}

/**
 * Добавление одного слова (`POST`, ADR-035 п. 7): `200` с созданным элементом
 * (слово после trim в исходном регистре); `400`/`409` — текстовое тело сервера.
 */
export async function addKeyword(
  request: AddKeywordRequest,
  signal?: AbortSignal,
): Promise<AddedKeyword> {
  const response = await fetch(ENDPOINT, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    credentials: 'include',
    body: JSON.stringify(request),
    signal,
  });

  if (!response.ok) {
    throw new CommodityCategoryKeywordApiError(
      await readError(response, 'Не удалось добавить слово'),
      response.status,
    );
  }

  return (await response.json()) as AddedKeyword;
}

/**
 * Удаление слова по `id` (`DELETE /{id:guid}`, ADR-035 п. 7): `204` — удалено,
 * `404 word not found.` — строки нет (в т.ч. удалена в другой вкладке).
 */
export async function deleteKeyword(id: string, signal?: AbortSignal): Promise<void> {
  const response = await fetch(`${ENDPOINT}/${encodeURIComponent(id)}`, {
    method: 'DELETE',
    credentials: 'include',
    signal,
  });

  if (!response.ok) {
    throw new CommodityCategoryKeywordApiError(
      await readError(response, 'Не удалось удалить слово'),
      response.status,
    );
  }
}

async function readError(response: Response, fallback: string): Promise<string> {
  // Ошибки API — текстовые (ADR-035 п. 7, без ProblemDetails): 400/409/404 показываем
  // текстом сервера как есть. 401/403 поясняем понятнее сырого текста (прецедент
  // precategorizedCommodities.ts / categorizationDecisions.ts).
  if (response.status === 403) {
    return 'Недостаточно прав для выполнения действия.';
  }

  if (response.status === 401) {
    return 'Сессия истекла. Войдите снова.';
  }

  const text = (await response.text().catch(() => '')).trim();
  return text && !text.startsWith('<') ? text : fallback;
}
