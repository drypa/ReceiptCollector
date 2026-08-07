# План: Редактирование имени магазина в списке магазинов (/merchants)

## Описание задачи

Реализовать inline-редактирование имени магазина на странице `/merchants` по
[задаче merchant-name-editing](../tasks/merchant-name-editing.md) в соответствии с
архитектурным решением [ADR 011](../adr/011-merchant-name-editing.md).

Изменение затрагивает **только Analytics (.NET API) и frontend (React)**. Схема PostgreSQL,
Go-backend, Telegram-бот и nginx **не меняются**. Миграций БД **нет**.

Ключевые ограничения, которые нельзя нарушать:

- Имя магазина нигде не денормализовано (чеки ссылаются на `merchants` по FK) — после
  переименования все представления получают новое имя автоматически. **Никаких фоновых
  задач/сигналов на обновление чеков создавать не нужно.**
- Максимальная длина имени — **256 символов** (лимит колонки `merchants.name`,
  `MerchantConfiguration.HasMaxLength(256)`). Одна и та же цифра на клиенте и сервере.
- Контракт запроса `{ "name": "..." }` сохраняется; HTTP-коды ответов по стилю проекта:
  `401 Unauthorized`, `403 Forbid`, `400 Bad Request`, `404 Not Found`, `200 OK`.
- Frontend-тестовой инфраструктуры в проекте нет — тесты только backend (xUnit + NSubstitute);
  фронтенд проверяется сборкой, lint и ручным чек-листом приёмки.
- Паттерн «Сохранение...»/`onRefresh()` повторяет редактирование категории в той же таблице.

---

## Шаг 1. Backend: перенос эндпоинта в группу `/api/merchants` + валидация (P0, ~0.5 дня)

**Файлы:**
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Merchants/MerchantEndpoints.cs`
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Receipts/ReceiptEndpoints.cs`
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Merchants/Merchant.cs`
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantEndpointsTests.cs`

**Действия:**

1.1. В `MerchantEndpoints.cs` добавить маршрут
`group.MapPut("/{merchantId:guid}/name", UpdateMerchantName);` — объявить **после**
`/{merchantId:guid}/category` (статический `/categories` не конфликтует из-за констрейнта
`:guid`, но порядок объявления оставляем по образцу существующего).

1.2. Перенести из `ReceiptEndpoints.cs` метод `UpdateMerchantName` и рекорд
`UpdateMerchantNameRequest` **без изменения логики проверок** (401/403/404), затем добавить
валидацию в начало после проверки магазина:

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
```

Порядок обработки сохранить: авторизация (401) → права админа (403) → существование магазина
(404) → валидация имени (400) → обновление.

1.3. Из `ReceiptEndpoints.cs` удалить `UpdateMerchantName` и `UpdateMerchantNameRequest`
(и ставшие ненужными `using`), т.к. маршрут полностью переезжает.

1.4. В `Merchant.cs` усилить `UpdateName`: в начале метода выполнить
`name = name.Trim();`, затем существующую проверку `IsNullOrWhiteSpace`, затем
`name.Length > 256 → ArgumentException("Merchant name must be at most 256 characters.", nameof(name));`.
Конструктор **не трогать**.

1.5. В `MerchantEndpointsTests.cs`:
- три существующих теста `UpdateMerchantName_*` переключить на вызов
  `MerchantEndpoints.UpdateMerchantName(...)` (импорт `ReceiptCollector.Analytics.Api.Modules.Merchants`);
- добавить новые тесты (см. Шаг 4).

**Критерий готовности:**
- `dotnet test` в `Analytics/tests/ReceiptCollector.Analytics.Api.Tests` — зелёный.
- В `ReceiptEndpoints.cs` не осталось упоминаний `UpdateMerchantName`/`UpdateMerchantNameRequest`.
- Swagger: маршрут виден под тегом «Merchants», под тегом «Receipts» его больше нет.

---

## Шаг 2. Frontend: API-слой (P0, ~0.25 дня)

**Файлы:**
- `Analytics/frontend/src/api/merchants.ts`
- `Analytics/frontend/src/api/receipts.ts`
- `Analytics/frontend/src/components/ReceiptDetails.tsx`

**Действия:**

2.1. В `api/merchants.ts` добавить:

```ts
export async function updateMerchantName(merchantId: string, newName: string): Promise<void> {
  const response = await fetch(`/api/merchants/${merchantId}/name`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ name: newName }),
  });

  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || 'Не удалось обновить имя магазина');
  }
}
```

Внимание: `credentials: 'include'` — на уровне опций `fetch`, а **не** внутри `headers`
(в текущем коде он ошибочно внутри `headers` — это исправляется переносом).

2.2. Удалить `updateMerchantName` из `api/receipts.ts`.

2.3. В `ReceiptDetails.tsx` заменить импорт: `import { updateMerchantName } from '../api/receipts';`
→ `import { updateMerchantName } from '../api/merchants';`. Логика компонента не меняется.

**Критерий готовности:**
- `npm run build` и `npm run lint` в `Analytics/frontend` — без ошибок (TypeScript найдёт
  забытые импорты).
- В `api/receipts.ts` нет `updateMerchantName`.

---

## Шаг 3. Frontend: inline-редактирование имени в `MerchantTable.tsx` (P0, ~1 день)

**Файлы:**
- `Analytics/frontend/src/components/MerchantTable.tsx`
- `Analytics/frontend/src/App.css` (только при необходимости мелкие отступы; новые сущности не вводить)

**Действия:**

3.1. Состояние (дополнительно к существующим `categories`, `editingId` (категория), `savingId`):
- `editingNameId: string | null` — id магазина с редактируемым именем;
- `nameDraft: string` — текущее значение поля ввода;
- `nameError: string | null` — текст ошибки валидации/сохранения.

Существующие `editingId`/`savingId` остаются для категории; редактирование имени и категории
в одной строке одновременно не допускается (состояния взаимоисключающие: при старте
редактирования имени сбросить `editingId` и наоборот).

3.2. В ячейке «Название» (первый `<td>`) три режима:
- `savingId === merchant.id` (или отдельный `nameSavingId`) → «Сохранение...»;
- `editingNameId === merchant.id` → `<input type="text" value={nameDraft}
  onChange={...} maxLength={256} autoFocus disabled={savingId === merchant.id}
  className="merchant-name-input" />` + кнопки «Сохранить»/«Отмена» с классами
  `save-merchant-btn secondary` / `cancel-merchant-btn secondary` внутри
  `div.merchant-edit-controls`;
- иначе → `<span>{merchant.name}</span>` + кнопка «ред.» (класс `edit-category-btn`,
  title «Редактировать имя») — кнопка рендерится только при `isAdmin` (для симметрии
  с колонкой категории; на практике страница и так только для админов).

3.3. `handleNameEditStart(merchant)`: `setNameDraft(merchant.name); setNameError(null);
setEditingNameId(merchant.id); setEditingId(null);`

3.4. `handleNameSave(merchantId)`:
- `const trimmed = nameDraft.trim();`
- если пусто → `setNameError('Имя магазина не может быть пустым')`, сохранить режим
  редактирования, **не вызывать API**;
- если `trimmed.length > 256` → `setNameError('Имя не должно превышать 256 символов')`,
  не вызывать API;
- иначе: `setSavingId(merchantId); setNameError(null);` → `await updateMerchantName(merchantId,
  trimmed)` → `setEditingNameId(null)` → `onRefresh()`;
- `catch`: `setNameError('Не удалось обновить имя магазина. Попробуйте ещё раз.')` — поле
  и режим редактирования **остаются**, `nameDraft` не сбрасывается;
- `finally`: `setSavingId(null)`.

3.5. `handleNameCancel`: `setEditingNameId(null); setNameDraft(''); setNameError(null);`
Дополнительно — обработка `Escape` через `onKeyDown` на инпуте. **Не** отменять по `onBlur`
(гонка blur/click с кнопкой «Сохранить»; решение D1 ADR 011).

3.6. Строка с ошибкой: под контролами (или под инпутом) `<span className="form-error"
role="alert">{nameError}</span>`; класс `form-error` — проверить наличие в `App.css`,
при отсутствии добавить минимальный (красный текст).

**Критерий готовности:**
- Админ на `/merchants` видит «ред.» у имени; клик открывает инпут с кнопками;
  сохранение обновляет список без перезагрузки страницы.
- Пустое/пробельное имя и имя длиннее 256 не отправляется на сервер, показывается сообщение.
- При ошибке сети поле остаётся в режиме редактирования с введённым текстом.
- `Escape` и «Отмена» возвращают исходное имя.
- Редактирование категории работает без регрессий.

---

## Шаг 4. Backend-тесты (P0, ~0.5 дня)

**Файл:** `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantEndpointsTests.cs`

**Действия:**

4.1. Обновить три существующих теста на вызов `MerchantEndpoints.UpdateMerchantName` (см. Шаг 1.5).

4.2. Добавить тесты:
- `UpdateMerchantName_WithEmptyName_ReturnsBadRequest` — имя `""` → `400`;
- `UpdateMerchantName_WithWhitespaceName_ReturnsBadRequest` — имя `"   "` → `400`;
- `UpdateMerchantName_WithTooLongName_ReturnsBadRequest` — 257 символов → `400`;
- `UpdateMerchantName_WithMaxLengthName_Succeeds` — ровно 256 символов → `200`;
- `UpdateMerchantName_TrimsWhitespace` — имя `"  Пятёрочка  "` → в `AddAsync` приходит
  `Merchant` с `Name == "Пятёрочка"`.

Во всех тестах использовать паттерн проекта: `Substitute.For<IMerchantRepository>(...)`,
`Substitute.For<IUserRepository>(...)`, `using var context = UserContext.SetUserId(userId);`.

**Критерий готовности:**
- `cd Analytics && dotnet test` — все тесты зелёные (включая существующие категории/чеки,
  без регрессий).

---

## Шаг 5. Сборка и ручной чек-лист приёмки (P0, ~0.5 дня)

**Команды:**
- `cd Analytics/frontend && npm run build && npm run lint`
- `cd Analytics && dotnet test`
- (опционально локально) `cd ../ReceiptCollector.Analytics.Migrations && dotnet run`, затем
  `cd ../ReceiptCollector.Analytics.Api && dotnet run`

**Ручной чек-лист (соответствует критериям приёмки задачи):**
1. Админ на `/merchants`: «ред.» → ввод → «Сохранить» → имя в таблице обновилось без
   перезагрузки страницы; строка пересортирована по новому имени.
2. Открыть чек этого магазина («Детали чека») и список чеков — имя новое.
3. Раздел «Товары» для чека этого магазина — имя магазина новое.
4. Поиск на `/merchants` по новому имени находит магазин.
5. Пустое имя / пробелы — ошибка на клиенте, запрос не уходит.
6. Имя из 257 символов — ошибка на клиенте; из 256 — сохраняется.
7. Ошибка сети (выключить API) — поле остаётся в режиме редактирования, введённый текст на
   месте, показано сообщение.
8. «Отмена» и `Escape` возвращают исходное имя.
9. Редактирование категории магазина и пагинация работают без изменений.
10. Не-админ не видит кнопку (страница `/merchants` и так закрыта).

---

## Шаг 6 (ИСКЛЮЧЁН из объёма, по решению PM от 07.08.2026): frontend-тесты

**Решение:** фронтенд-тесты в рамках этой задачи **не выполняются** (тестовой инфраструктуры
в проекте нет; ввод vitest/RTL — отдельное инфраструктурное решение). Покрытие остаётся
за Шагом 5 (сборка + lint + ручной чек-лист приёмки).

---

## Критерии успеха (общие)

- Все шаги P0 выполнены; backend-тесты зелёные; frontend собирается и проходит lint.
- Чек-лист приёмки (Шаг 5) пройден полностью.
- Миграций БД и изменений Go/бот/nginx нет.
- Эндпоинт имени доступен только по `/api/merchants/{merchantId}/name` (старый URL удалён);
  все вызовы frontend обновлены.
