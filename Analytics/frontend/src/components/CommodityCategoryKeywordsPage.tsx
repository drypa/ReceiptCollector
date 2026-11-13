import { useMemo } from 'react';
import { useAdmin } from '../hooks/useAdmin';
import { useCommodityCategoryKeywords } from '../hooks/useCommodityCategoryKeywords';
import { CategoryKeywordCard } from './CategoryKeywordCard';
import type { CategoryKeywords } from '../types/commodityCategoryKeyword';
import './CommodityCategoryKeywordsPage.css';

/**
 * Порядок секций по группам (ADR-035 п. 8); категории с пустой группой — плоским
 * списком в конце. Значения — константы `CommodityCategoryHelper.GetGroup`.
 */
const GROUP_ORDER = ['Продукты', 'Транспорт', 'Для дома', 'Прочее'];

/**
 * Админский раздел «Категории товаров» (FR-9/FR-10, UC-1…UC-3, ADR-035 п. 8).
 * Гейт по useAdmin повторяет CategorizationDecisionsPage: данные запрашиваются только
 * после подтверждения прав (вью с хуком монтируется лишь админу — при `!isAdmin` ни одного
 * запроса к `/api/commodity-category-keywords` не уходит). Скрытие раздела — UX, не защита:
 * сервер на прямой запрос и сам ответил бы 403 (ADR-035 п. 9).
 */
export function CommodityCategoryKeywordsPage() {
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

  return <CommodityCategoryKeywordsView />;
}

function CommodityCategoryKeywordsView() {
  const {
    data,
    isLoading,
    error,
    listErrors,
    busyKeys,
    reload,
    add,
    delete: removeKeyword,
  } = useCommodityCategoryKeywords();

  const sections = useMemo(() => buildSections(data), [data]);

  // Полноэкранный спиннер — только когда показывать ещё нечего: рефетч после мутации
  // сохраняет отрисованные данные (H1 — страница не «мигает» в пустоту).
  const initialLoading = isLoading && data.length === 0;

  return (
    <div className="layout">
      <header>
        <div>
          <h1>Категории товаров</h1>
          <p>
            Слова сопоставляются с названием товара как подстрока без учёта регистра:
            «соль» сработает и в «фасоль». Исключения сильнее повышений: подсказки-исключения
            попадают в запрос к ИИ первыми, а повышения отбрасываются при переполнении
            промта первыми.
          </p>
        </div>
        <div className="controls">
          <button type="button" onClick={reload} disabled={isLoading}>
            Обновить
          </button>
        </div>
      </header>

      {initialLoading && (
        <div className="state state-loading">
          <span className="spinner" aria-hidden="true" /> Загружаем списки слов...
        </div>
      )}

      {/* Общий баннер: сбой загрузки или сетевая ошибка/5xx мутации (ADR-035 п. 8);
          адресные 4xx показываются inline под затронутым списком. */}
      {error && !initialLoading && (
        <div className="state state-error" role="alert">
          <p>{error}</p>
          <button type="button" onClick={reload}>
            Повторить
          </button>
        </div>
      )}

      {!isLoading && !error && sections.length === 0 && (
        <div className="empty-state">
          <p>Категории не найдены.</p>
        </div>
      )}

      {!initialLoading && !error && (
        <div className="keywords-sections">
          {sections.map((section) => (
            <section key={section.group || 'ungrouped'} className="keywords-section">
              {section.group !== '' && (
                <h2 className="keywords-section-title">{section.group}</h2>
              )}
              <div className="keywords-cards">
                {section.items.map((category) => (
                  <CategoryKeywordCard
                    key={category.categoryId}
                    category={category}
                    busyKeys={busyKeys}
                    listErrors={listErrors}
                    onAdd={add}
                    onRemove={removeKeyword}
                  />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * Секции по группам в порядке «Продукты», «Транспорт», «Для дома», «Прочее», затем
 * категории с пустой группой (плоский список, ADR-035 п. 8). Внутри секции сохраняется
 * порядок выдачи сервера (порядок объявления реестра категорий).
 */
function buildSections(
  data: CategoryKeywords[],
): Array<{ group: string; items: CategoryKeywords[] }> {
  const sections: Array<{ group: string; items: CategoryKeywords[] }> = [];

  for (const group of GROUP_ORDER) {
    const items = data.filter((category) => category.group === group);
    if (items.length > 0) {
      sections.push({ group, items });
    }
  }

  // Защитный случай: неизвестная непустая группа (реестр групп может вырасти) —
  // после известных, в порядке первого появления; пустая группа — всегда последней.
  for (const category of data) {
    const known = category.group === '' || GROUP_ORDER.includes(category.group);
    if (!known && !sections.some((section) => section.group === category.group)) {
      sections.push({
        group: category.group,
        items: data.filter((item) => item.group === category.group),
      });
    }
  }

  const ungrouped = data.filter((category) => category.group === '');
  if (ungrouped.length > 0) {
    sections.push({ group: '', items: ungrouped });
  }

  return sections;
}
