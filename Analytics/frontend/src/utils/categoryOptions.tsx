import type { ReactNode } from 'react';
import type { Category } from '../types/commodity';

/**
 * Группировка категорий по полю group для <optgroup> (решение D1 ADR 010).
 * Категории с пустой/отсутствующей группой (старые категории 0–17 и Other=255)
 * выводятся плоским списком — прямые дети <select>. Порядок групп — по порядку
 * первого появления в массиве.
 */
export function groupCategories(categories: Category[]): Array<{ label: string; items: Category[] }> {
  const grouped = new Map<string, Category[]>();

  for (const cat of categories) {
    const group = cat.group ?? '';
    const bucket = grouped.get(group) ?? [];
    bucket.push(cat);
    grouped.set(group, bucket);
  }

  return Array.from(grouped.entries()).map(([label, items]) => ({ label, items }));
}

/**
 * Рендер опций категорий с группировкой по group.
 *
 * @param getValue  функция значения <option>: по умолчанию String(cat.id).
 *                  Для категоризации в чеке передаётся кастом, возвращающий
 *                  cat.key (имя enum, контракт PUT /api/receipts/{id}/categories).
 */
export function renderCategorySelectOptions(
  categories: Category[],
  getValue: (category: Category) => string = (category) => String(category.id),
): ReactNode[] {
  return groupCategories(categories).map(({ label, items }) => {
    const options = items.map((cat) => (
      <option key={cat.id} value={getValue(cat)}>
        {cat.name}
      </option>
    ));

    if (label === '') {
      return options;
    }

    return (
      <optgroup key={label} label={label}>
        {options}
      </optgroup>
    );
  });
}