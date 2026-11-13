import { keywordListKey } from '../hooks/useCommodityCategoryKeywords';
import { KeywordChipList } from './KeywordChipList';
import type { CategoryKeywords, KeywordListType } from '../types/commodityCategoryKeyword';

interface CategoryKeywordCardProps {
  category: CategoryKeywords;
  /** Ключи затронутых запросами полей/чипов (см. useCommodityCategoryKeywords). */
  busyKeys: ReadonlySet<string>;
  /** Inline-ошибки списков по ключу пары «категория + список». */
  listErrors: Record<string, string>;
  onAdd: (categoryId: number, listType: KeywordListType, word: string) => Promise<boolean>;
  onRemove: (id: string, listKey: string) => Promise<boolean>;
}

/**
 * Карточка категории (ADR-035 п. 8): название и два списка чипов — «Повышают» (boost)
 * и «Исключают» (exclude). Счётчики слов видны без раскрытия (UC-1).
 */
export function CategoryKeywordCard({
  category,
  busyKeys,
  listErrors,
  onAdd,
  onRemove,
}: CategoryKeywordCardProps) {
  const boostKey = keywordListKey(category.categoryId, 'boost');
  const excludeKey = keywordListKey(category.categoryId, 'exclude');

  return (
    <article className="keyword-card">
      <h3 className="keyword-card-title">{category.categoryName}</h3>

      <KeywordChipList
        listType="boost"
        title="Повышают"
        listKey={boostKey}
        words={category.boost}
        busyKeys={busyKeys}
        error={listErrors[boostKey] ?? null}
        onAdd={(word) => onAdd(category.categoryId, 'boost', word)}
        onRemove={(id) => onRemove(id, boostKey)}
      />

      <KeywordChipList
        listType="exclude"
        title="Исключают"
        listKey={excludeKey}
        words={category.exclude}
        busyKeys={busyKeys}
        error={listErrors[excludeKey] ?? null}
        onAdd={(word) => onAdd(category.categoryId, 'exclude', word)}
        onRemove={(id) => onRemove(id, excludeKey)}
      />
    </article>
  );
}
