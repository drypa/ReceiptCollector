interface PaginationProps {
  currentPage: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onPrevious: () => void;
  onNext: () => void;
}

export function Pagination({ currentPage, totalPages, onPageChange, onPrevious, onNext }: PaginationProps) {
  if (totalPages <= 1) {
    return null;
  }

  const visiblePages = createVisiblePages(currentPage, totalPages);

  return (
    <nav className="pagination" aria-label="Пейджинг чеков">
      <button type="button" onClick={onPrevious} disabled={currentPage === 1}>
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
            >
              {page}
            </button>
          </li>
        ))}
      </ul>
      <button type="button" onClick={onNext} disabled={currentPage === totalPages}>
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
