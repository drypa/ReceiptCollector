import { useEffect } from 'react';
import { usePageSize } from '../contexts/PageSizeContext';
import { useAdmin } from '../hooks/useAdmin';
import { usePrecategorizedBulkOperations } from '../hooks/usePrecategorizedBulkOperations';
import { usePrecategorizedCommodities } from '../hooks/usePrecategorizedCommodities';
import { Pagination } from './Pagination';
import { PrecategorizedBulkActions } from './PrecategorizedBulkActions';
import { PrecategorizedCommodityTable } from './PrecategorizedCommodityTable';

/**
 * Подсказка на всех контролах, заблокированных на время массовой операции
 * (ADR-036 §5): сводка должна относиться к тому набору строк, который на экране.
 */
const BULK_LOCKED_TITLE = 'Дождитесь завершения массовой операции';

/**
 * Админский раздел «Предкатегоризация» (FR-7, ADR-027 п.7).
 * Гейт по useAdmin повторяет MerchantsPage: для не-админа данные не запрашиваются.
 */
export function PrecategorizedCommoditiesPage() {
  const { isAdmin, loading: adminLoading } = useAdmin();

  if (adminLoading) {
    return (
      <div className="layout">
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Проверяем права доступа...
        </div>
      </div>
    );
  }

  if (!isAdmin) {
    return (
      <div className="layout">
        <div className="empty-state">
          <p>Доступ запрещён.</p>
        </div>
      </div>
    );
  }

  return <PrecategorizedCommoditiesView />;
}

function PrecategorizedCommoditiesView() {
  const { pageSize, setPageSize, pageSizeOptions } = usePageSize();
  const {
    data,
    isLoading,
    error,
    currentPage,
    totalPages,
    totalItems,
    search,
    setSearch,
    goToPage,
    nextPage,
    previousPage,
    refresh,
  } = usePrecategorizedCommodities({ pageSize });

  // Массовые операции страницы (ADR-036 §5): хук владеет снимком строк, одним
  // запросом и автообновлением списка (onCompleted: refresh — дублирующих
  // вызовов не добавляем). Компонент кнопок подключается к bulk.run напрямую:
  // снимок id берётся в момент подтверждения диалога (сводка задачи 07).
  const bulk = usePrecategorizedBulkOperations({ onCompleted: refresh });
  const { reset: resetBulk } = bulk;

  // Сводка относится к набору строк на экране: при смене страницы, поиска или
  // размера страницы итог прошлой операции больше не актуален (ADR-036 §5).
  // На первом рендере reset() — no-op (summary/error и так пусты).
  useEffect(() => {
    resetBulk();
  }, [currentPage, search, pageSize, resetBulk]);

  // На время массовой операции блокируются все контролы списка (ADR-036 §5,
  // паттерн блокировок ADR-028 C5): админ не может увести со страницы строк,
  // к которым относится итог операции.
  const controlsLocked = bulk.isRunning;

  return (
    <div className="layout">
      <header>
        <div>
          <h1>Предкатегоризация</h1>
          <p>
            Предложений в очереди: <strong>{totalItems}</strong>
          </p>
        </div>
        <div className="controls">
          <div className="page-size-selector">
            <label htmlFor="precategorized-page-size-select">Строк на странице: </label>
            <select
              id="precategorized-page-size-select"
              value={pageSize}
              onChange={(e) => setPageSize(Number(e.target.value))}
              disabled={isLoading || controlsLocked}
              title={controlsLocked ? BULK_LOCKED_TITLE : undefined}
            >
              {pageSizeOptions.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </div>
          <button
            type="button"
            onClick={refresh}
            disabled={isLoading || controlsLocked}
            title={controlsLocked ? BULK_LOCKED_TITLE : undefined}
          >
            Обновить
          </button>
        </div>
        {/* Кнопки массовых операций — в шапке рядом с «Обновить», не в таблице
            (ADR-036 §5); для не-админа не рендерятся — гейт выше. */}
        <PrecategorizedBulkActions
          items={data}
          isRunning={bulk.isRunning}
          summary={bulk.summary}
          error={bulk.error}
          isLoading={isLoading}
          loadError={Boolean(error)}
          onRun={bulk.run}
        />
      </header>

      <div className="controls">
        <label htmlFor="precategorized-search">Поиск: </label>
        <input
          id="precategorized-search"
          type="search"
          value={search}
          placeholder="Наименование или магазин"
          onChange={(event) => setSearch(event.target.value)}
          disabled={controlsLocked}
          title={controlsLocked ? BULK_LOCKED_TITLE : undefined}
        />
      </div>

      {isLoading && (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем предложения...
        </div>
      )}

      {error && !isLoading && (
        <div className="state state-error" role="alert">
          <p>Не удалось загрузить предложения: {error}</p>
          <button type="button" onClick={refresh}>
            Попробовать снова
          </button>
        </div>
      )}

      {!isLoading && !error && (
        <PrecategorizedCommodityTable items={data} onRefresh={refresh} disabled={controlsLocked} />
      )}

      {!isLoading && !error && (
        <Pagination
          currentPage={currentPage}
          totalPages={totalPages}
          onPageChange={goToPage}
          onNext={nextPage}
          onPrevious={previousPage}
          disabled={controlsLocked}
          disabledTitle={BULK_LOCKED_TITLE}
        />
      )}
    </div>
  );
}
