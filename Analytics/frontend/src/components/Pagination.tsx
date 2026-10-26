interface PaginationProps {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPrevious: () => void;
  onNext: () => void;
  /**
   * Полная блокировка пагинации (режим проверки на «Товарах», C5/ADR-028 п. 5).
   * Аддитивный проп: без него поведение идентично прежнему (страницы «Чеки», «Магазины»).
   */
  disabled?: boolean;
  /** Подсказка на кнопках, когда блокировка активна (title). */
  disabledTitle?: string;
}

export function Pagination({
  currentPage,
  totalPages,
  onPageChange,
  onPrevious,
  onNext,
  disabled = false,
  disabledTitle,
}: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const visiblePages = createVisiblePages(currentPage, totalPages);
  const previousDisabled = disabled || currentPage === 1;
  const nextDisabled = disabled || currentPage === totalPages;

  return (
    <nav className="pagination" aria-label="Пейджинг чеков">
      <button
        type="button"
        onClick={onPrevious}
        disabled={previousDisabled}
        aria-disabled={previousDisabled || undefined}
        title={disabled ? disabledTitle : undefined}
      >
        Назад
      </button>
      <ul>
        {visiblePages.map((page) => (
          <li key={page}>
            <button
              type="button"
              className={page === currentPage ? 'active' : ''}
              aria-current={page === currentPage ? 'page' : undefined}
              onClick={() => onPageChange(page)}
              disabled={disabled}
              aria-disabled={disabled || undefined}
              title={disabled ? disabledTitle : undefined}
            >
              {page}
            </button>
          </li>
        ))}
      </ul>
      <button
        type="button"
        onClick={onNext}
        disabled={nextDisabled}
        aria-disabled={nextDisabled || undefined}
        title={disabled ? disabledTitle : undefined}
      >
        Вперёд
      </button>
    </nav>
  );
}

function createVisiblePages(currentPage: number, totalPages: number) {
  const delta = 2;
  const pages: number[] = [];
  const start = Math.max(1, currentPage - delta);
  const end = Math.min(totalPages, currentPage + delta);

  for (let page = start; page <= end; page += 1) {
    pages.push(page);
  }

  if (!pages.includes(1)) {
    pages.unshift(1);
  }

  if (!pages.includes(totalPages)) {
    pages.push(totalPages);
  }

  return Array.from(new Set(pages)).sort((a, b) => a - b);
}
