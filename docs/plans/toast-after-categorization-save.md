# План: Тост вместо модального окна после сохранения категоризации (Analytics UI)

**Связанный ADR:** [ADR-020](../adr/020-toast-after-categorization-save.md)
**Файл задачи:** [toast-after-categorization-save.md](../tasks/toast-after-categorization-save.md)

## Область изменений

Frontend Analytics (`Analytics/frontend/`), только точка сохранения категоризации:
`ReceiptDetails.tsx` → `saveSelectedCategories()`. Backend, .NET API, остальные компоненты,
другие модальные окна — **без изменений**. Внешних зависимостей не добавляется.

## Что меняется

### Изменение 1: Новый модуль `src/components/Toasts.tsx`

Самодостаточный тост-модуль (provider + hook + контейнер):

- `ToastProvider` — React-контекст; состояние `Array<{ id, type: 'success' | 'error', message }>`;
  методы `success(message)` / `error(message)`; уникальный `id` (crypto.randomUUID, fallback —
  счётчик); авто-удаление через `setTimeout(..., 5000)`; ручное `dismiss(id)` очищает таймер;
  общий `Map<id, timeoutId>` с cleanup на unmount провайдера.
- `useToast()` — хук, возвращает `{ success, error }`.
- `ToastContainer` — рендер стека: `role="status"` для успеха, `role="alert"` для ошибки;
  кнопка закрытия «×» с `aria-label="Закрыть уведомление"`; классы `.toast`,
  `.toast--success`, `.toast--error`.

### Изменение 2: `src/App.tsx` — подключить провайдер

Оборачиваем дерево `<ToastProvider>` (внутри `BrowserRouter`, вокруг `<PageSizeProvider>`).
Маршруты и остальные провайдеры не меняются.

### Изменение 3: `src/components/ReceiptDetails.tsx` — `saveSelectedCategories()`

Было:

```tsx
const result = await saveReceiptCategories(receipt.id, items);
showDialog('Готово', `Сохранено категорий: ${result.updated}.`, () => {
  setSuggestions(null);
  setSelectedCategories({});
  onReceiptRefresh?.();
});
// catch: showDialog('Ошибка', ...)
```

Стало:

```tsx
const result = await saveReceiptCategories(receipt.id, items);
setSuggestions(null);
setSelectedCategories({});
onReceiptRefresh?.();
toast.success(`Сохранено категорий: ${result.updated}`);
// catch:
toast.error(error instanceof Error ? error.message : 'Не удалось сохранить категории товаров');
```

Важные нюансы:

- Обратные вызовы подтверждающей кнопки выполняются **немедленно** при успехе, до показа тоста
  (решение C1 ADR-020) — иначе режим категоризации «застрянет».
- Текст без завершающей точки (мини-формат тоста); при `updated = 0` текст корректен.
- После ошибки пользователь остаётся в режиме категоризации с прежними выборами — так нужно,
  можно сразу повторить (решение D1).
- `showDialog`/`CustomDialog` для других сценариев страницы (`runCategorization`,
  `saveMerchantName`, «ID магазина отсутствует») — **не трогаем**.
- Добавить `const { ... } = useToast();` (вызов в `ReceiptDetails`).

### Изменение 4: `src/App.css` — стили тостов

- `.toast-container` — `position: fixed; top: 1rem; right: 1rem;` колонка с `gap`,
  `z-index: 1100` (выше `.dialog-overlay` = 1000);
- `.toast` — белый фон, радиус, тень, левая акцентная планка (`border-left: 4px solid currentColor`),
  иконка ✓/✕ (inline-SVG или псевдоэлемент), анимация появления (fade + сдвиг вниз);
- `.toast--success` — зелёный акцент (`#16a34a`);
- `.toast--error` — красный акцент (`#dc2626`, как в `.state-error`);
- `.toast-close` — кнопка «×».

## Что НЕ меняется

- `package.json` / lock-файл (зависимости не добавляются), конфигурация Vite/TS/ESLint.
- Backend (Go), .NET API (`saveReceiptCategories` контракт не меняется), nginx, бот.
- `CustomDialog.tsx`, `CommoditiesPage`, `CommodityTable`, `MerchantsPage`, `MerchantTable`,
  `ReceiptTable`, `Pagination`, `Layout`, `Sidebar`, `ReceiptsPage`.
- Все модальные окна Analytics, кроме удаляемого «Готово» в сценарии сохранения категоризации.

## Порядок выполнения

1. Создать `components/Toasts.tsx` (провайдер, хук, контейнер, таймеры).
2. Подключить `<ToastProvider>` в `App.tsx`.
3. Обновить `saveSelectedCategories` в `ReceiptDetails.tsx` (успех → выход из режима +
   `onRefresh()` + `toast.success`; ошибка → `toast.error`).
4. Добавить стили в `App.css`.
5. Прогнать `npm run build` и `npm run lint` (регрессионный барьер — frontend-тестов в проекте нет).
6. Ручная приёмка по чек-листу задачи (ниже).

## Проверка критериев приёмки

- [ ] Модальное окно «Готово» после сохранения категоризации больше не появляется.
- [ ] Успех → тост «Сохранено категорий: N» (N — фактическое число; проверить N > 0 и N = 0).
- [ ] Тост успеха имеет зелёный акцент (планка + иконка).
- [ ] Ошибка сохранения (в т.ч. остановка backend / сеть) → красный тост, отличимый без чтения
      текста; модальное окно ошибки не возвращается.
- [ ] Тост авто-скрывается через ~5 секунд.
- [ ] Крестик закрывает тост раньше.
- [ ] Быстрые повторные «Сохранить» — тосты накапливаются без наложений (каждый со своим
      таймером).
- [ ] После успешного сохранения режим категоризации закрывается и список товаров
      обновляется сразу (без ожидания нажатия кнопки).
- [ ] Прочие диалоги `ReceiptDetails` (ошибка категоризации, ошибка сохранения имени магазина)
      не изменились.

## Риски и митигация

| Риск | Вероятность | Митигация |
|------|-------------|-----------|
| Таймеры не очищаются (утечка / setState после unmount) | Низкая | `Map<id, timeoutId>` + cleanup на unmount |
| «Застрявший» режим категоризации после успеха | Низкая | Немедленный `setSuggestions(null)` + `setSelectedCategories({})` до показа тоста |
| Регрессия других диалогов | Низкая | Меняется только `saveSelectedCategories`; ручная приёмка по чек-листу |
| Дубликаты `id` при быстрых кликах | Очень низкая | `crypto.randomUUID()` + fallback-счётчик |