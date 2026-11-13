import { useCallback, useState } from 'react';
import { keywordChipKey } from '../hooks/useCommodityCategoryKeywords';
import { MAX_WORDS_PER_LIST } from '../types/commodityCategoryKeyword';
import type { CategoryKeywordItem, KeywordListType } from '../types/commodityCategoryKeyword';

interface KeywordChipListProps {
  /** Тип списка — определяет тон чипов («Повышают»/«Исключают»). */
  listType: KeywordListType;
  /** Заголовок списка: «Повышают» или «Исключают». */
  title: string;
  /** Ключ пары «категория + список» — блокировка добавления и inline-ошибка привязаны к нему. */
  listKey: string;
  words: CategoryKeywordItem[];
  /** Ключи затронутых запросами полей/чипов (см. useCommodityCategoryKeywords). */
  busyKeys: ReadonlySet<string>;
  /** Inline-ошибка под списком (текст сервера для 400/409, пояснения для 401/403). */
  error: string | null;
  onAdd: (word: string) => Promise<boolean>;
  onRemove: (id: string) => void;
}

/**
 * Список чипов одного слова с полем добавления (ADR-035 п. 8): счётчик `N / 200`,
 * плейсхолдер «Список пуст», чип с `×`, поле ввода + кнопка «Добавить». На лимите
 * поле блокируется с подсказкой — чтобы `409` не был сюрпризом. Клиентская валидация
 * минимальна (пустое поле, maxLength): «слово из 1 символа» доходит до сервера и
 * показывается inline его текстом `400` (сервер — единственная авторитетная проверка).
 */
export function KeywordChipList({
  listType,
  title,
  listKey,
  words,
  busyKeys,
  error,
  onAdd,
  onRemove,
}: KeywordChipListProps) {
  const [draft, setDraft] = useState('');

  const trimmedWord = draft.trim();
  const limitReached = words.length >= MAX_WORDS_PER_LIST;
  const adding = busyKeys.has(listKey);
  const addDisabled = adding || limitReached || trimmedWord.length === 0;

  const submit = useCallback(async () => {
    if (addDisabled) {
      return;
    }

    // Поле очищается только после успеха: при 400/409 текст остаётся — его правит
    // пользователь, а не переписывает серверный отказ.
    if (await onAdd(trimmedWord)) {
      setDraft('');
    }
  }, [addDisabled, onAdd, trimmedWord]);

  return (
    <div className="keyword-list">
      <div className="keyword-list-header">
        <span className="keyword-list-title">{title}</span>
        <span
          className={
            limitReached
              ? 'keyword-list-counter keyword-list-counter--limit'
              : 'keyword-list-counter'
          }
        >
          {words.length} / {MAX_WORDS_PER_LIST}
        </span>
      </div>

      <div className="keyword-chips">
        {words.length === 0 && <span className="keyword-chips-empty">Список пуст</span>}
        {words.map((item) => (
          <span key={item.id} className={`keyword-chip keyword-chip--${listType}`}>
            {item.word}
            <button
              type="button"
              className="keyword-chip-remove"
              aria-label={`Удалить слово «${item.word}» из списка «${title}»`}
              disabled={busyKeys.has(keywordChipKey(item.id))}
              onClick={() => onRemove(item.id)}
            >
              ×
            </button>
          </span>
        ))}
      </div>

      <form
        className="keyword-add-form"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        <input
          className="keyword-add-input"
          type="text"
          value={draft}
          maxLength={64}
          placeholder="Новое слово"
          aria-label={`Новое слово в список «${title}»`}
          disabled={adding || limitReached}
          onChange={(event) => setDraft(event.target.value)}
        />
        <button type="submit" className="keyword-add-button" disabled={addDisabled}>
          Добавить
        </button>
      </form>

      {limitReached && (
        <p className="keyword-list-hint">
          Достигнут лимит: не более {MAX_WORDS_PER_LIST} слов в списке.
        </p>
      )}
      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
