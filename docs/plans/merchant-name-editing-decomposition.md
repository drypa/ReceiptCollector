# Декомпозиция: Редактирование имени магазина в списке магазинов (/merchants)

> Источники: [задача](../tasks/merchant-name-editing.md), [ADR 011](../adr/011-merchant-name-editing.md),
> [план реализации](merchant-name-editing.md).
> Решение PM от 07.08.2026: **Шаг 6 (frontend-тесты) исключён** из объёма.
> Объём: **только Analytics (.NET API + React frontend)**. PostgreSQL-миграций, Go-backend,
> Telegram-бота и nginx **не касаемся**.

## Сводка

| # | Подзадача | Приоритет | Оценка |
|---|-----------|-----------|--------|
| T1 | Domain: усилить `Merchant.UpdateName` (trim + лимит 256) | P0 | 0.25 дн |
| T2 | API: перенести эндпоинт `UpdateMerchantName` в `/api/merchants` + валидация | P0 | 0.5 дн |
| T3 | API-тесты: переключение существующих + 5 новых кейсов валидации | P0 | 0.5 дн |
| T4 | Frontend: API-слой `updateMerchantName` в `api/merchants.ts` + фикс `credentials` | P0 | 0.25 дн |
| T5 | Frontend: inline-редактирование имени в `MerchantTable.tsx` | P0 | 1 дн |
| T6 | Сборка, lint, полный прогон тестов + ручной чек-лист приёмки | P0 | 0.5 дн |

**Итого (последовательно): ~3 дня.**

## Порядок выполнения и параллельность

```
Трек A (backend)   T1 → T2 → T3 ───────────────┐
                                               ├─→ T6 (приёмка)
Трек B (frontend)  T4 → T5 ────────────────────┘
```

- **T4 можно начинать сразу, параллельно с треком A**: контракт `PUT /api/merchants/{merchantId:guid}/name`
  зафиксирован в ADR 011 (решение A1) и не зависит от кода backend.
- **T2 и T3 выполнять одним непрерывным изменением** (одна ветка/коммит): после удаления
  `UpdateMerchantName`/`UpdateMerchantNameRequest` из `ReceiptEndpoints.cs` тестовый проект
  **не компилируется**, пока тесты не переведены на `MerchantEndpoints.UpdateMerchantName` (см. T3).
- **T5 зависит от T4** (импортирует `updateMerchantName` из `api/merchants.ts`).
- **T6 — финальная, после обоих треков.**

---

## T1. Domain: усилить `Merchant.UpdateName` (trim + лимит 256)

**Файл:** `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/Merchant.cs`

**Действия** (ADR 011, решение B1, раздел «Детали решения» / план п. 1.4):

1. В `UpdateName(string name)` в начале метода выполнить `name = name.Trim();`.
2. Оставить существующую проверку `string.IsNullOrWhiteSpace(name)` → `ArgumentException`
   (порядок: **trim → проверка пустоты → проверка длины**).
3. Добавить после проверки пустоты: `if (name.Length > 256) throw new ArgumentException(
   "Merchant name must be at most 256 characters.", nameof(name));`
   (лимит 256 — из `MerchantConfiguration.HasMaxLength(256)`, единый источник на клиенте и сервере).
4. **Конструктор НЕ трогать** — по ADR: «защита ингресста данных nalog.ru от новых отказов;
   лимит длины конструктора не добавляется».

**Критерий готовности:**
- `cd Analytics && dotnet build` — без ошибок.
- Поведение (trim и 400-лимит) дополнительно покрывается тестами T3
  (`UpdateMerchantName_TrimsWhitespace`, `UpdateMerchantName_WithTooLongName_ReturnsBadRequest`).

**Оценка:** 0.25 дня.

---

## T2. API: перенести эндпоинт `UpdateMerchantName` в `/api/merchants` + валидация

**Файлы:**
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs`
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptEndpoints.cs`

**Действия** (ADR 011, решение A1 / план п. 1.1–1.3):

1. В `MapMerchantEndpoints` (группа `/api/merchants`) добавить
   `group.MapPut("/{merchantId:guid}/name", UpdateMerchantName);`
   **после** `group.MapPut("/{merchantId:guid}/category", UpdateCategory);`
   (статический `/categories` не конфликтует — констрейнт `:guid`).
2. Перенести из `ReceiptEndpoints.cs` в `MerchantEndpoints.cs`:
   - метод `public static async Task<IResult> UpdateMerchantName(...)` **без изменения сигнатуры**
     и логики проверок (401 Unauthorized → 403 Forbid → 404 Not Found);
   - рекорд `public sealed record UpdateMerchantNameRequest(string Name);`
     (объявить в файле, как `UpdateMerchantCategoryRequest`).
   - Все нужные `using` в `MerchantEndpoints.cs` уже есть (`Api.Modules.Users` — UserContext,
     `Domain.Modules.Users` — IUserRepository, `Domain.Modules.Merchants` — IMerchantRepository/Merchant,
     `Microsoft.AspNetCore.Mvc` — `[FromBody]`).
3. Добавить валидацию **после** проверки существования магазина (порядок обработки:
   авторизация → права админа → существование → валидация имени → обновление):
   ```csharp
   var normalized = request.Name?.Trim();
   if (string.IsNullOrWhiteSpace(normalized))
   {
       return Results.BadRequest("Merchant name is required.");
   }
   if (normalized.Length > 256)
   {
       return Results.BadRequest("Merchant name must be at most 256 characters.");
   }
   merchant.UpdateName(normalized);
   await merchantRepository.AddAsync(merchant, cancellationToken);
   return Results.Ok("Merchant name updated successfully.");
   ```
4. Из `ReceiptEndpoints.cs` удалить: маршрут `group.MapPut("/merchants/{merchantId:guid}/name", ...)`
   (и комментарий над ним), метод `UpdateMerchantName`, рекорд `UpdateMerchantNameRequest`.
5. Убрать ставшие ненужными `using` в `ReceiptEndpoints.cs` (после удаления они используются только
   в `UpdateMerchantName`): `ReceiptCollector.Analytics.Api.Modules.Users`,
   `ReceiptCollector.Analytics.Domain.Modules.Merchants`,
   `ReceiptCollector.Analytics.Domain.Modules.Users`.
   Оставить: `System.Globalization`, `Microsoft.AspNetCore.Mvc`,
   `ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts`.
6. `Program.cs` **не меняется** — `MapMerchantEndpoints()` уже зарегистрирован.

**Критерий готовности:**
- `cd Analytics && dotnet build` — без ошибок (тестовый проект в этот момент может не компилироваться —
  ссылки обновляются в T3 тем же изменением).
- В `ReceiptEndpoints.cs` нет упоминаний `UpdateMerchantName`/`UpdateMerchantNameRequest`.
- Swagger (локально `dotnet run` в Api): `PUT /api/merchants/{merchantId}/name` виден под тегом
  **«Merchants»**; в теге «Receipts» этого маршрута нет.

**Оценка:** 0.5 дня.

---

## T3. API-тесты: переключение существующих + новые кейсы валидации

**Файл:** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantEndpointsTests.cs`

**Действия** (ADR 011, решение B1 / план п. 1.5, 4.1–4.2):

1. Три существующих теста `UpdateMerchantName_WithAdminUser_ShouldUpdateSuccessfully`,
   `UpdateMerchantName_WithNonAdminUser_ShouldReturnForbidden`,
   `UpdateMerchantName_WithNonExistentMerchant_ShouldReturnNotFound`:
   - заменить `ReceiptEndpoints.UpdateMerchantName(` → `MerchantEndpoints.UpdateMerchantName(`;
   - `UpdateMerchantNameRequest` теперь из namespace `...Api.Modules.Merchants` (импорт
     `ReceiptCollector.Analytics.Api.Modules.Receipts` удалить, если больше не используется);
   - логика/ассерты не меняются.
2. Добавить новые тесты (паттерн проекта: `Substitute.For<IMerchantRepository>`,
   `Substitute.For<IUserRepository>`, `using var context = UserContext.SetUserId(userId);`):
   - `UpdateMerchantName_WithEmptyName_ReturnsBadRequest` — имя `""` → `400`,
     `AddAsync` не вызывается;
   - `UpdateMerchantName_WithWhitespaceName_ReturnsBadRequest` — имя `"   "` → `400`,
     `AddAsync` не вызывается;
   - `UpdateMerchantName_WithTooLongName_ReturnsBadRequest` — 257 символов → `400`,
     `AddAsync` не вызывается;
   - `UpdateMerchantName_WithMaxLengthName_Succeeds` — ровно 256 символов → `200`,
     `AddAsync` вызван;
   - `UpdateMerchantName_TrimsWhitespace` — имя `"  Пятёрочка  "` → в `AddAsync` приходит
     `Merchant` с `Name == "Пятёрочка"`.
3. Проверить, что новые тесты валидируют и доменный инвариант (T1) через trim/длину.

**Критерий готовности:**
- `cd Analytics && dotnet test` — **все** тесты зелёные (включая существующие по категориям/чекам —
  без регрессий).

**Оценка:** 0.5 дня.

---

## T4. Frontend: API-слой `updateMerchantName` в `api/merchants.ts` + фикс `credentials`

**Файлы:**
- `Analytics/frontend/src/api/merchants.ts`
- `Analytics/frontend/src/api/receipts.ts`
- `Analytics/frontend/src/components/ReceiptDetails.tsx`

**Действия** (ADR 011, решение C1 / план п. 2.1–2.3):

1. В `api/merchants.ts` добавить функцию (рядом с `updateMerchantCategory`):
   `updateMerchantName(merchantId: string, newName: string): Promise<void>` —
   `PUT /api/merchants/${merchantId}/name`, `headers: { 'Content-Type': 'application/json' }`,
   **`credentials: 'include'` на уровне опций `fetch` (не внутри `headers`)** — это исправление
   латентного бага из `api/receipts.ts` (ADR 011, п. 2 «Фактическое состояние»);
   `body: JSON.stringify({ name: newName })`; при `!response.ok` —
   `throw new Error(message || 'Не удалось обновить имя магазина')`.
2. Удалить `updateMerchantName` из `api/receipts.ts` (строки 58–72, вместе со всем «багованным»
   объектом `headers`).
3. В `ReceiptDetails.tsx` заменить импорт:
   `import { updateMerchantName } from '../api/receipts';` →
   `import { updateMerchantName } from '../api/merchants';`.
   **Логика компонента не меняется.**

**Критерий готовности:**
- `cd Analytics/frontend && npm run build && npm run lint` — без ошибок (TypeScript найдет
  забытые импорты).
- В `api/receipts.ts` нет `updateMerchantName`.

**Оценка:** 0.25 дня.

---

## T5. Frontend: inline-редактирование имени в `MerchantTable.tsx`

**Файлы:**
- `Analytics/frontend/src/components/MerchantTable.tsx`
- `Analytics/frontend/src/App.css` — **добавить минимальный `.form-error`** (класса в проекте нет:
  проверено grep; план п. 3.6 допускает добавление «минимальный красный текст»).

**Действия** (ADR 011, решение D1 / план п. 3.1–3.6):

1. Новое состояние (дополнительно к `categories`, `editingId`, `savingId`):
   - `editingNameId: string | null` — id магазина с редактируемым именем;
   - `nameDraft: string` — текущее значение поля;
   - `nameError: string | null` — текст ошибки.
   - Взаимоисключение с категорией: в `handleNameEditStart` вызывать `setEditingId(null)`;
     в существующий `handleEditStart` (категория) добавить `setEditingNameId(null)`.
2. Ячейка «Название» (первый `<td>`, строка 68) — три режима:
   - `savingId === merchant.id` → `<span>Сохранение...</span>`;
   - `editingNameId === merchant.id` → `<input type="text" value={nameDraft} onChange={...}
     maxLength={256} autoFocus disabled={savingId === merchant.id} className="merchant-name-input"
     onKeyDown={обработка Escape} />` + кнопки «Сохранить»/«Отмена» с классами
     `save-merchant-btn secondary` / `cancel-merchant-btn secondary` внутри
     `div.merchant-edit-controls` (все классы уже есть в `App.css` — проверено);
   - иначе → `<span>{merchant.name}</span>` + кнопка «ред.» (класс `edit-category-btn`,
     title «Редактировать имя») — **рендерится только при `isAdmin`** (симметрия с колонкой категории).
3. `handleNameEditStart(merchant)`: `setNameDraft(merchant.name); setNameError(null);
   setEditingNameId(merchant.id); setEditingId(null);`
4. `handleNameSave(merchantId)`:
   - `const trimmed = nameDraft.trim();`
   - пусто/пробелы → `setNameError('Имя магазина не может быть пустым')`, **API не вызывать**;
   - `trimmed.length > 256` → `setNameError('Имя не должно превышать 256 символов')`,
     **API не вызывать**;
   - иначе: `setSavingId(merchantId); setNameError(null);` → `await updateMerchantName(merchantId,
     trimmed)` (импорт из `../api/merchants`) → `setEditingNameId(null)` → `onRefresh()`;
   - `catch` → `setNameError('Не удалось обновить имя магазина. Попробуйте ещё раз.')` —
     **режим редактирования и `nameDraft` сохраняются** (критерий приёмки задачи п. 5);
   - `finally` → `setSavingId(null)`.
5. `handleNameCancel`: `setEditingNameId(null); setNameDraft(''); setNameError(null);`
   Плюс обработка клавиши `Escape` через `onKeyDown` на инпуте.
   **Отмену по `onBlur` не реализовывать** — гонка blur/click с кнопкой «Сохранить» (ADR 011, решение D1).
6. Строка ошибки под контролами: `<span className="form-error" role="alert">{nameError}</span>`.
   В `App.css` добавить минимальный стиль `.form-error` (например, `color: #dc2626` / `font-size: 0.85rem`),
   новые сущности/классы не вводить.

**Критерий готовности (ручная проверка на dev-стенде + сборка):**
- Админ на `/merchants` видит «ред.» у имени; клик открывает инпут с кнопками;
  сохранение обновляет список **без перезагрузки страницы** (через `onRefresh()`).
- Пустое/пробельное имя и имя длиннее 256 **не отправляется на сервер**, показывается сообщение.
- При ошибке сети поле остаётся в режиме редактирования с введённым текстом.
- `Escape` и «Отмена» возвращают исходное имя.
- Редактирование категории работает без регрессий.
- `cd Analytics/frontend && npm run build && npm run lint` — чисто.

**Оценка:** 1 день.

---

## T6. Сборка, тесты и ручной чек-лист приёмки

**Файлы:** изменений нет — только проверка.

**Действия** (план, Шаг 5; критерии приёмки задачи):

1. `cd Analytics/frontend && npm run build && npm run lint`
2. `cd Analytics && dotnet test`
3. Опционально локальный запуск: `cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run`,
   затем `cd ../ReceiptCollector.Analytics.Api && dotnet run`, frontend — `cd Analytics/frontend && npm run dev`.
4. Ручной чек-лист (10 пунктов из плана):
   1. Админ на `/merchants`: «ред.» → ввод → «Сохранить» → имя обновилось без перезагрузки;
      строка пересортирована по новому имени.
   2. «Детали чека» и список чеков этого магазина — имя новое.
   3. Раздел «Товары» чека этого магазина — имя новое.
   4. Поиск на `/merchants` по новому имени находит магазин.
   5. Пустое имя / пробелы — ошибка на клиенте, запрос не уходит.
   6. Имя из 257 символов — ошибка на клиенте; из 256 — сохраняется.
   7. Ошибка сети (выключить API) — поле остаётся в режиме редактирования, текст на месте, сообщение показано.
   8. «Отмена» и `Escape` возвращают исходное имя.
   9. Редактирование категории и пагинация работают без изменений.
   10. Не-админ не видит кнопку (страница `/merchants` закрыта).

**Критерий готовности:**
- Все пункты чек-листа пройдены; backend-тесты зелёные; frontend собирается и проходит lint.
- Эндпоинт имени доступен **только** по `/api/merchants/{merchantId}/name` (старый URL удалён);
  все вызовы frontend обновлены.
- Миграций БД и изменений Go/бот/nginx нет.

**Оценка:** 0.5 дня.

---

## Риски и зависимости

| Риск | Митигация |
|------|-----------|
| **T2 ↔ T3 связка:** после удаления символов из `ReceiptEndpoints.cs` тестовый проект не компилируется до перевода 3 тестов | Выполнять T2+T3 одним изменением (одна ветка/коммит); не оставлять промежуточное состояние в `main` |
| **Rolling deploy frontend→API:** новый frontend против старого API → 404 на новый URL | Монолитный релиз (`build.sh` собирает оба образа, `up.sh` поднимает вместе); обновлять API и frontend одним релизом |
| **Регрессия сортировки/пагинации:** после `onRefresh()` строка с новым именем может уйти на другую страницу (список отсортирован по имени) | Ожидаемое поведение (ADR 011); проверить вручную в T6, п. 1 |
| **Гонка в `AddAsync` (upsert):** поиск по Inn затем по Id — при гипотетическом дубле Inn можно обновить не тот магазин | Уникальный индекс на `Inn` уже существует; отдельный `UpdateNameAsync` не вводим (YAGNI, ADR 011) |
| **`.form-error` отсутствует в проекте** (проверено grep) | Добавить минимальный стиль в `App.css` в T5; иначе сообщение об ошибке не будет видно |
| **Неиспользуемые `using` в `ReceiptEndpoints.cs`** после удаления | Удалить 3 using в T2 (иначе warnings при сборке) |
| **ESLint (react-hooks/exhaustive-deps)** для новых `useCallback`-обработчиков в `MerchantTable` | Корректные зависимости в деп-массивах; контроль в `npm run lint` (T5/T6) |
| **`credentials` баг в fetch** | Исправляется в T4 (перенос в опции `fetch`) — латентный, сейчас маскируется same-origin |
| **Админ-гейтинг:** кнопка «ред.» видна только `isAdmin` | Гейтинг в T5; страница `/merchants` и так закрыта для не-админов (`MerchantsPage`) |
