# План реализации: категоризация страницы «Товары» (порциями) + пакетное сохранение

> Декомпозиция утверждённой задачи для @senior-developer. Источники:
> [задача commodities-page-categorization](../tasks/commodities-page-categorization.md)
> (решения заказчика C1–C11, критерии приёмки, edge cases),
> [ADR-028](../adr/028-commodities-page-categorization.md) — основной документ реализации,
> а также ADR 009 (`existing → cache → precategorized → ai`, `AI:*`), ADR 019 (кэш, `TryAdd`),
> ADR 020 (тосты), ADR 021 (env-проброс), ADR 026 (`IAiClient`), ADR 027 (очередь предкатегорий,
> стиль админских эндпоинтов, `TimeProvider`).
>
> **Новых архитектурных решений план не содержит.** Всё ниже — порядок работ, файлы и проверки.
> Противоречие требований — эскалация к @software-architect, а не самостоятельное решение
> (см. «Замечания для реализации»).

## 0. Инварианты (из ADR-028 — обязательны, не обсуждаются)

1. **Stateless-сервер**: порционность обеспечивает клиент (≤ 5 уникальных названий в запросе);
   сервер о «странице» не знает, состояния между запросами не держит, сессий/TTL/job'ов **нет**.
2. **Только строки текущей страницы** (C1) обеспечивает клиент, отправляя снапшот страницы; сервер
   принимает любое подмножество товаров **текущего пользователя**.
3. **Suggest read-only**: не пишет в `commodities`, кэш и `precategorized_commodities`
   (проверяется тестом).
4. **Семантика пустого значения** (C4): `category == null`/отсутствует ⇒ элемент пропускается
   («не трогаем»); сброса категории новым контрактом нет; `Undefined` запрещён (`400`).
5. **Сохранение атомарно и owner-scoped**: одна транзакция (`SaveChangesAsync`), `409` и «ничего не
   записано» при недоступном id; `updated` = число строк, где значение реально изменилось.
6. **Пустой `AI:BaseUrl`** ⇒ suggest страницы отвечает `200` с `aiAvailable = false`, ИИ не
   вызывается, кэш/очередь работают (C7). **`503` не возвращается** (в отличие от suggest чека).
7. **Права на сервере**: `401` при отсутствии пользователя, `403` **явным**
   `Results.StatusCode(StatusCodes.Status403Forbidden)` (не `Results.Forbid()` — он даёт 500).
8. **Бюджет запроса** `CommodityBatchCategorization:SuggestTimeout` (по умолчанию `00:00:25`):
   по истечении — частичный результат (`200`), незавершённые названия `undefined` + текст ошибки;
   отмена клиентом (его `CancellationToken`) по-прежнему прерывает весь запрос.
9. **Один конвейер категоризации**: алгоритм `existing → cache → precategorized → ai` живёт в
   `ICommodityCategoryResolver`; `CommodityCategorizationService` делегирует ему, его публичный
   контракт и наблюдаемое поведение **не меняются** (существующие тесты — регрессионная сетка).
10. **Кэш (ADR-019)** пополняется только при сохранении: `TryAdd(normalizedName, category)` для
    каждой изменённой строки (first-wins, no-op при «замороженном» кэше). Очередь (ADR-027) —
    **только чтение**.
11. **Порядок строк стабилен**: режим проверки работает на снапшоте страницы, список во время
    проверки не перезапрашивается, ключ строки — `commodityId`.
12. **Вне объёма**: «Детали чека» и её контракты (включая `503` и семантику reset), массовая
    категоризация вне страницы, дефолт/глобальный размер страницы, справочник категорий, миграции
    БД, индексы, nginx, Go-сервисы, MongoDB, фронтенд-тестовая инфраструктура.

## 1. Карта изменений по слоям

| Слой / файл | Действие | Шаг |
|---|---|---|
| `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommoditiesNotAccessibleException.cs` | новое доменное исключение (по образцу `ReceiptNotFoundException`) | 1 |
| `Domain/Modules/Commodities/ICommodityRepository.cs` | + `GetByOwnerAsync(userId, ids, ct)`, + `UpdateCategoriesAsync(userId, changes, ct)` → `int` | 1 |
| `Infrastructure/Persistence/Postgres/CommodityRepository.cs` | реализация: явный `c.Receipt.UserId == userId`, один `SaveChangesAsync`, проверка доступности всех id | 1 |
| `Infrastructure/Configuration/Options/CommodityBatchCategorizationOptions.cs` | новая опция (секция `CommodityBatchCategorization`, `SuggestTimeout`) + валидация `> 0` | 2 |
| `Infrastructure/Configuration/DependencyInjectionExtensions.cs` | биндинг опции (`ValidateOnStart`), регистрация resolver и batch-сервиса | 2, 3, 4 |
| `Application/Modules/Commodities/Contracts/ICommodityCategoryResolver.cs` | контракт общего конвейера | 3 |
| `Infrastructure/Modules/Commodities/CommodityCategoryResolver.cs` | вынесенный алгоритм (existing → cache → precategorized → ai, дедуп, ошибки/таймаут ИИ, `aiEnabled`) | 3 |
| `Infrastructure/Modules/Commodities/CommodityCategorizationService.cs` | делегирует resolver'у; поведение/контракт без изменений | 3 |
| `Application/Modules/Commodities/Models/CommodityBatchCategorizationModels.cs` | `CommodityBatchCategorizationResult`, `CommodityCategoryChange` | 4 |
| `Application/Modules/Commodities/Contracts/ICommodityBatchCategorizationService.cs` | `SuggestAsync`, `ApplyAsync` | 4 |
| `Infrastructure/Modules/Commodities/CommodityBatchCategorizationService.cs` | оркестрация порции, дедлайн, `aiAvailable`, валидация пустых имён, сохранение + кэш | 4 |
| `Api/Modules/Users/AdminAuthorization.cs` | общий helper `401`/`403` (используется новым модулем и `PrecategorizedCommodityEndpoints`) | 5 |
| `Api/Modules/Commodities/CommodityBatchCategorizationEndpoints.cs` | `POST /api/commodities/categories/suggest`, `PUT /api/commodities/categories` | 5 |
| `Api/Program.cs` | `app.MapCommodityBatchCategorizationEndpoints();` | 5 |
| `Infrastructure/Modules/Commodities/PrecategorizedCommodityService.cs` / `Api/Modules/PrecategorizedCommodities/PrecategorizedCommodityEndpoints.cs` | только переход на общий `AdminAuthorization` (поведение идентично) | 5 |
| `appsettings.json` | секция `CommodityBatchCategorization: { SuggestTimeout }` | 2 |
| `docker-compose.yml`, `.env.example`, `AGENTS.md` | проброс `CommodityBatchCategorization__SuggestTimeout` + строка в таблице настроек | 2 |
| `frontend/src/types/commodity.ts` | + `CategorizationSource`, `CommodityCategorizationItem`, `CommodityCategoriesSuggestion` | 6 |
| `frontend/src/api/commodities.ts` | + `suggestCommodityCategories`, `saveCommodityCategories`, ошибка со `status` | 6 |
| `frontend/src/utils/categorizationSource.ts` | перенос `getSuggestionNote` из `ReceiptDetails.tsx` (общие подписи) | 6 |
| `frontend/src/hooks/useCommodityPageCategorization.ts` | новая машина состояний режима проверки | 7 |
| `frontend/src/hooks/useCommodities.ts` | нормализация пустой/несуществующей страницы (догрузка последней доступной) | 8 |
| `frontend/src/components/CommoditiesPage.tsx` | кнопка «Категоризировать страницу (N)», прогресс, предупреждение об ИИ, блокировки, тосты | 8 |
| `frontend/src/components/CommodityTable.tsx` | режим проверки: селект + подпись источника + `title` ошибки; блокировка «ред.»/даты | 8 |
| `frontend/src/components/Pagination.tsx` | + проп `disabled` | 8 |
| `frontend/src/components/ReceiptDetails.tsx` | только импорт `getSuggestionNote` (поведение не меняется) | 6 |
| `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/**` | новые тесты (раздел 10) | 1–5, 9 |

## 2. Шаги реализации

### Шаг 1. Домен и репозиторий: owner-scoped чтение и пакетная запись

1. `CommoditiesNotAccessibleException : Exception` — сообщение с количеством недоступных id
   (без самих id в тексте ответа).
2. `ICommodityRepository`:
   - `Task<IReadOnlyList<Commodity>> GetByOwnerAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct)`;
   - `Task<int> UpdateCategoriesAsync(Guid userId, IReadOnlyDictionary<Guid, CommodityCategory> changes, CancellationToken ct)`;
   - XML-doc: «явный owner-фильтр обязателен: глобальный query-filter на `Receipts` в
     `CommodityRepository` не активен — `SetCurrentUser` вызывается только в `ReceiptRepository`».
3. `CommodityRepository`:
   - `GetByOwnerAsync`: `AsNoTracking().Where(c => ids.Contains(c.Id) && c.Receipt.UserId == userId)`
     (нужен `.Include(c => c.Receipt)` либо проекция; маппинг в доменный `Commodity` — как в `GetByIdAsync`);
   - `UpdateCategoriesAsync`: один запрос сущностей по id + `c.Receipt.UserId == userId`;
     если найдено меньше, чем запрошено → `CommoditiesNotAccessibleException` (ничего не записано);
     иначе — для каждого: сравнить `CategoryId` с новым значением; изменённые обновить
     (`CategoryId`, `CategoryName = CommodityCategoryHelper.GetDisplayName(...)`) и посчитать;
     один `SaveChangesAsync`; вернуть число изменённых.

**Проверка шага:** `dotnet test` (существующие тесты зелёные), новые тесты репозитория (раздел 10.1).

### Шаг 2. Конфигурация бюджета запроса

1. `CommodityBatchCategorizationOptions` (секция `CommodityBatchCategorization`):
   `TimeSpan SuggestTimeout = TimeSpan.FromSeconds(25)`.
2. `appsettings.json`: `"CommodityBatchCategorization": { "SuggestTimeout": "00:00:25" }`.
3. `DependencyInjectionExtensions`: `AddOptions<CommodityBatchCategorizationOptions>().Bind(...)
   .Validate(o => o.SuggestTimeout > TimeSpan.Zero, "...").ValidateOnStart()`.
4. `docker-compose.yml`: `- CommodityBatchCategorization__SuggestTimeout=${CommodityBatchCategorization__SuggestTimeout:-00:00:25}`
   (рядом с `Precategorization__*`, стиль ADR-021). `.env.example` — закомментированный блок с
   пояснением (бюджет одного suggest-запроса страницы; должен быть существенно меньше
   `proxy_read_timeout` nginx = 120 с). `AGENTS.md` — строка в таблице настроек Analytics.

**Проверка шага:** тест валидации опции (`<= 0` → ошибка старта), значения из env.

### Шаг 3. Вынос конвейера категоризации в resolver (рефакторинг без изменения поведения)

1. `ICommodityCategoryResolver` (Application):
   ```csharp
   Task<IReadOnlyList<ReceiptItemCategorizationResult>> ResolveAsync(
       IReadOnlyCollection<Commodity> items,
       bool aiEnabled,
       CancellationToken aiCancellationToken,   // только для вызовов ИИ (может нести дедлайн)
       CancellationToken cancellationToken);    // токен запроса (отмена клиентом)
   ```
2. `CommodityCategoryResolver` (Infrastructure) — перенос шагов 1–4 из
   `CommodityCategorizationService.SuggestForReceiptAsync` **без изменения порядка и семантики**:
   `existing` (`Category is { Id: not 0 }`) → `cache` (`CommodityNameNormalizer`, дисплейное имя из
   `CommodityCategoryHelper`) → `precategorized` (один batch-запрос по точным именам, ошибка чтения →
   Warning + деградация) → `ai` (дедуп по нормализованному имени, `_aiSemaphore` с `AI:Concurrency`,
   `AiClientException` по позиции → `undefined` + текст).
   Дополнительно: `OperationCanceledException` от вызова ИИ → если `cancellationToken.IsCancellationRequested`,
   пробросить; иначе `undefined` + «ИИ не ответил за отведённое время.» (дедлайн). При `aiEnabled == false`
   ИИ не вызывается: остаток → `undefined` + «ИИ не настроен.».
3. `CommodityCategorizationService`: зависимости сокращаются до `IReceiptRepository`,
   `ICommodityRepository`, `ICommodityCategoryCache`, `ICommodityCategoryResolver`
   (`SuggestForReceiptAsync` = загрузка чека → `ResolveAsync(items, aiEnabled: BaseUrl есть,
   aiCancellationToken: cancellationToken, cancellationToken)`); `ApplyConfirmedCategoriesAsync`
   не меняется (в т.ч. reset-семантика и `TryAdd`). DI: resolver — Scoped.
4. **Правило ревью:** ни один существующий тест `CommodityCategorizationServiceTests` не меняется;
   любые правки в нём — красный флаг (поведение чека должно остаться идентичным).

**Проверка шага:** `dotnet test` — все существующие тесты зелёные без правок.

### Шаг 4. Application/Infrastructure: batch-сервис

1. Модели (Application):
   - `CommodityCategoryChange(Guid CommodityId, CommodityCategory Category)`;
   - `CommodityBatchCategorizationResult(IReadOnlyCollection<ReceiptItemCategorizationResult> Items, bool AiAvailable)`
     (элемент результата — **существующий** `ReceiptItemCategorizationResult`, wire-формат идентичен suggest чека).
2. `ICommodityBatchCategorizationService`:
   - `Task<CommodityBatchCategorizationResult> SuggestAsync(Guid userId, IReadOnlyCollection<Guid> commodityIds, CancellationToken ct)`;
   - `Task<int> ApplyAsync(Guid userId, IReadOnlyCollection<CommodityCategoryChange> changes, CancellationToken ct)`.
3. `CommodityBatchCategorizationService` (Infrastructure):
   - `SuggestAsync`:
     1. `GetByOwnerAsync(userId, ids.Distinct())`; пропущенные id — Warning (только количество);
     2. строки с пустым/пробельным названием **не** уходят в resolver: сразу `undefined` + текст
        «Название товара не заполнено.» (поведение suggest чека не меняется);
     3. уникальных названий > 20 → `ArgumentException` (эндпоинт → `400`);
     4. `aiEnabled = !string.IsNullOrWhiteSpace(aiOptions.BaseUrl)`;
     5. дедлайн через `TimeProvider`: `CancellationTokenSource.CreateLinkedTokenSource(ct)` +
        отмена по `SuggestTimeout` (реализация отмены — через `Task.Delay(timeout, timeProvider, ct)`,
        чтобы `FakeTimeProvider` управлял временем);
     6. `ResolveAsync(found, aiEnabled, deadlineToken, ct)`; `aiAvailable = aiEnabled`;
     7. логи (раздел 11 ADR-028).
   - `ApplyAsync`:
     1. `changes` с `Undefined` → `ArgumentException` (защита в сервисе, не только в эндпоинте);
     2. `UpdateCategoriesAsync(userId, dict, ct)` → `updated`; `CommoditiesNotAccessibleException`
        пробрасывается (эндпоинт → `409`);
     3. после успеха — `ICommodityCategoryCache.TryAdd` для каждой изменённой строки: нормализованное
        имя нужно из доменной сущности → либо `GetByOwnerAsync` для изменённых id до записи, либо
        возврат пар «id → name» из репозитория. Выбрать минимальный вариант, но **без второго
        `SaveChanges`** и без записи в кэш при откате;
     4. в `precategorized_commodities` не писать (проверяется тестом).
4. DI-регистрация: `services.AddScoped<ICommodityBatchCategorizationService, CommodityBatchCategorizationService>();`

**Проверка шага:** тесты сервиса (раздел 10.2).

### Шаг 5. API: эндпоинты и права

1. `Api/Modules/Users/AdminAuthorization.cs`:
   `internal static Task<IResult?> AuthorizeAdminAsync(this ...)` или статический метод
   `AdminAuthorization.AuthorizeAsync(IUserRepository, CancellationToken)` — тело и XML-doc
   переносятся из `PrecategorizedCommodityEndpoints` (включая объяснение про `Results.Forbid()` → 500).
   `PrecategorizedCommodityEndpoints` переключается на helper (поведение идентично), `MerchantEndpoints`
   **не** трогается (его `Forbid` — отдельный бэклог).
2. `CommodityBatchCategorizationEndpoints`:
   - `POST /categories/suggest` (тело `{ commodityIds }`):
     `401` → `403` → валидация (`пусто`, `> 200`, `Guid.Empty`, дубликаты) → сервис →
     `200 { items, aiAvailable }`. `ArgumentException` (уникальных названий > 20) → `400`.
   - `PUT /categories` (тело `{ items: [{ commodityId, category }] }`):
     `401` → `403` → валидация (`items` пуст, `Guid.Empty`, дубликаты, `category` — число/неизвестное
     имя/`Undefined`) → фильтрация «не трогаем» (`null`/пробелы) → если нечего менять →
     `200 { updated = 0 }` без обращения к сервису → иначе сервис → `200 { updated }`;
     `CommoditiesNotAccessibleException` → `409` (текст: «Некоторые строки больше недоступны.
     Обновите страницу и повторите сохранение.» — по-английски, как остальные тексты API).
     Валидация категории — зеркально `ReceiptCategorizationEndpoints.Save`
     (`Enum.TryParse(ignoreCase: true)`, `Enum.IsDefined`, отказ для `Undefined`, отказ для чисел).
   - Регистрация в `Program.cs`; тег `Commodities`; XML-doc с ссылкой на ADR-028.
3. Оба обработчика — `public static async Task<IResult>` (для тестов уровня хендлера, как в проекте).

**Проверка шага:** тесты эндпоинтов (раздел 10.3).

### Шаг 6. Frontend: типы, API-клиент, общие подписи

1. `types/commodity.ts`:
   ```ts
   export type CategorizationSource = 'existing' | 'cache' | 'precategorized' | 'ai' | 'undefined';
   export interface CommodityCategorizationItem {
     commodityId: string; name: string; categoryId: number | null;
     categoryName: string | null; source: CategorizationSource | string; error: string | null;
   }
   export interface CommodityCategoriesSuggestion { items: CommodityCategorizationItem[]; aiAvailable: boolean; }
   ```
2. `api/commodities.ts`:
   - `suggestCommodityCategories(commodityIds: string[], signal?: AbortSignal)`;
   - `saveCommodityCategories(items: Array<{ commodityId: string; category: string | null }>)`;
   - ошибка со `status` (класс/интерфейс `ApiRequestError { status: number; message: string }`),
     чтобы страница различала `401/403/409/сеть`. Сообщения для пользователя — русские, на клиенте.
3. `utils/categorizationSource.ts` — `getSuggestionNote(source)` (перенос из `ReceiptDetails.tsx`,
   подписи дословно: «уже присвоена» / «по прошлым чекам» / «из очереди предкатегорий» /
   «предложено ИИ» / «не определена»). `ReceiptDetails.tsx` импортирует утилиту (поведение не меняется).

**Проверка шага:** `npm run build`, `npm run lint`.

### Шаг 7. Frontend: машина состояний режима проверки

`hooks/useCommodityPageCategorization.ts`:

```ts
type Phase = 'idle' | 'review' | 'saving';
interface DraftEntry { value: string; initialValue: string; source?: string; error?: string; touched: boolean; }
```

- `start(rows: CommodityItem[])`:
  1. снапшот `rows` (в режиме проверки страница рендерит снапшот, не свежие данные);
  2. `candidates = rows.filter(r => r.categoryId == null || r.categoryId === 0)`;
  3. черновик: содержательная категория → `value = key` (из справочника `GET /api/commodities/categories`,
     `categoryId → key`), `source = 'existing'`, `initialValue = value`; иначе `value = ''`,
     `initialValue = ''`;
  4. группировка кандидатов по **точному** названию → порции по 5 названий → последовательный цикл
     `suggestCommodityCategories(idsChunk, signal)`;
  5. применение ответа: для каждого `item` — если `draft[touched]` → не перезаписывать значение
     (подпись «изменено вручную»); иначе `value = key(categoryId) || ''`, `source`, `error`;
     `progress.processed +=` число строк завершённой порции;
  6. `aiAvailable === false` хоть в одной порции → `aiWarning = true` (постоянное предупреждение);
  7. `generation`-счётчик: ответы после `cancel()`/`save()`/unmount игнорируются.
- `setValue(id, value)` → `value`, `touched = true`, `source` сбрасывается/«изменено вручную».
- `changed` = `value !== initialValue && value !== ''`.
- `save()`:
  1. `phase = 'saving'`, `abort()` текущей порции;
  2. если `changed` пуст → выйти из режима, `toast.success('Сохранено категорий: 0')` (ADR-020: N = 0 —
     тост показывается), без запроса;
  3. иначе `saveCommodityCategories(changed)` → `phase = 'idle'` → `onSaved(updated)`;
     ошибка → `phase = 'review'`, черновик сохраняется, тост с русским текстом по `status`
     (`409` — «Часть строк больше недоступна, обновите страницу»; `403` — «Недостаточно прав»;
     прочее — текст ошибки/дефолт).
- `cancel()` → `abort()`, `phase = 'idle'`, черновик очищается.

**Проверка шага:** `npm run build`, `npm run lint`; ручной прогон (раздел 11).

### Шаг 8. Frontend: страница, таблица, пагинация

1. `CommoditiesPage.tsx`:
   - `candidateCount = data.filter(r => r.categoryId == null || r.categoryId === 0).length`;
   - кнопка «Категоризировать страницу ({candidateCount})»: рендер только `isAdmin`; `disabled` при
     `candidateCount === 0 || phase !== 'idle'`; `title` «На этой странице нет товаров без категории»
     при нуле;
   - в режиме проверки вместо неё — «Сохранить категории» (активна всегда, кроме `saving`) и «Отмена»,
     строка прогресса «Определяем категории… X из Y», inline-предупреждение при `aiWarning`
     (`role="status"`);
   - `controlsLocked = phase !== 'idle'`: `disabled` на `pageSize`-селекте, `categoryFilter`-селекте,
     «Обновить», `Pagination` (проп `disabled`), ссылка-дата в таблице и «ред.»;
     `title="Сначала сохраните или отмените изменения"`;
   - `onSaved(updated)`: `refresh()` текущей страницы + `toast.success('Сохранено категорий: ' + updated)`;
   - передать в `CommodityTable`: `reviewMode`, `drafts`, `onDraftChange`, `categories` (справочник
     поднимается на уровень страницы, чтобы не грузить его дважды);
   - рекомендация (не обязательна для приёмки): `beforeunload`-предупреждение, пока `phase === 'review'`.
2. `CommodityTable.tsx`:
   - `reviewMode`: в колонке «Категория» — селект (`value = drafts[id].value`, пустая опция «—» первая,
     `Undefined` не выводится — как `selectableCategories` в `ReceiptDetails`) + подпись источника под
     ним (`category-suggestion category-suggestion-<source>`, классы уже есть в `App.css`) + `title={error}`;
     подпись выводится **у каждой** строки, включая повторяющиеся названия (C8);
   - вне режима — текущее поведение без изменений.
3. `Pagination.tsx`: аддитивный `disabled?: boolean` → все кнопки `disabled`, `aria-disabled`.
4. `useCommodities.ts`: нормализация страницы — если загрузка вернула пустой массив, а
   `requestedPage > computedTotalPages`, догрузить `computedTotalPages` (не более одной повторной
   попытки; защита от цикла). Счётчик `totalItems` уже берётся из `X-Total-Count`.

**Проверка шага:** `npm run build`, `npm run lint`, ручная приёмка по чек-листу (раздел 11).

### Шаг 9. Документация

- `AGENTS.md`: строка `CommodityBatchCategorization__SuggestTimeout` в таблице настроек Analytics
  (единственный оставшийся документационный пункт: amend-заметки в ADR-009/019/027 уже добавлены
  вместе с ADR-028).
- Статус задачи — на усмотрение PM (не менять самостоятельно).

## 3. План тестирования

### 10.1 Репозиторий (`Infrastructure/CommodityRepositoryTests.cs` — новый или расширение существующих)

EF InMemory (как `CommodityReadServiceFilterTests`), сид: два пользователя, чеки с товарами.

1. `GetByOwnerAsync` возвращает только товары указанного пользователя (чужие id отсутствуют в результате).
2. `GetByOwnerAsync` с неизвестным id → пустой/частичный результат без исключения.
3. `UpdateCategoriesAsync` пишет `category_id` + `category_name` для своих строк и возвращает число изменённых.
4. `UpdateCategoriesAsync` со значением, равным текущему, → `0` изменённых, сущность не помечена изменённой.
5. `UpdateCategoriesAsync` с чужим/несуществующим id → `CommoditiesNotAccessibleException`, **ни одна**
   строка не изменена (проверить после отката/повторного чтения).
6. `UpdateCategoriesAsync` не сбрасывает категорию (`null` невозможен по типу) и не трогает `Undefined`
   (валидация выше по стеку).

### 10.2 Batch-сервис (`CommodityBatchCategorizationServiceTests.cs` — новый)

NSubstitute для `ICommodityRepository`, `ICommodityCategoryCache`, `IPrecategorizedCommodityRepository`,
`IAiClient`, `IOptions<AiOptions>`, `IOptions<CommodityBatchCategorizationOptions>`, `FakeTimeProvider`.

Suggest:
1. `existing`-категория → `source = existing`, ИИ и кэш не вызываются.
2. Попадание в кэш → `source = cache`, дисплейное имя из `CommodityCategoryHelper`.
3. Попадание в очередь (batch-запрос вызван **один раз** на порцию) → `source = precategorized`.
4. Дедупликация: 3 строки с одинаковым названием → `IAiClient` получил **один** вызов, все три строки
   в ответе с `source = ai` и одинаковым значением.
5. `AI:BaseUrl` пуст → `IAiClient` не вызывался, `AiAvailable = false`, строки из кэша/очереди
   заполнены, остальные `undefined` (`error` заполнен).
6. `AiClientException` по одному названию → эта строка `undefined`, остальные получают результат;
   запрос не падает.
7. Дедлайн: `IAiClient` «висит» (`Task.Delay(Timeout.Infinite, ct)`), `SuggestTimeout = 50ms`,
   `FakeTimeProvider` двигает время → результат **частичный**, `undefined` + текст таймаута, HTTP-уровень
   вернул бы `200`.
8. Отмена запроса клиентом (`ct.Cancel()`) → `OperationCanceledException` пробрасывается (не маскируется
   под частичный результат).
9. Пустое/пробельное название → `undefined` + текст, `IAiClient` не вызывался, остальные строки обработаны.
10. `> 20` уникальных названий → `ArgumentException`.
11. Suggest **не пишет**: `ICommodityRepository` не получил `UpdateCategoriesAsync`, кэш — ни одного
    `TryAdd`, очередь — ни одной мутации.
12. Неизвестные id не попадают в `Items`.

Apply:
13. Одно изменение → `updated = 1`, кэш получил `TryAdd(normalized, category)`.
14. Запрос «того же значения» → `updated = 0`, `TryAdd` не вызывался.
15. `CommoditiesNotAccessibleException` из репозитория → пробрасывается наружу, `TryAdd` не вызывался.
16. `Undefined` в изменениях → `ArgumentException`, репозиторий не вызывался.
17. Очередь предкатегорий не мутируется (ни одного вызова записи/смены статуса).

### 10.3 Эндпоинты (`CommodityBatchCategorizationEndpointsTests.cs` — новый)

По образцу `PrecategorizedCommodityEndpointsTests` / `ReceiptCategorizationEndpointsTests`
(`UserContext.SetUserId`, исполнение `IResult` на `DefaultHttpContext`, проверка кодов **исполнением**).

Suggest:
1. Нет пользователя → `401`, сервис не вызван.
2. Не админ (`IUserRepository` → `IsAdmin = false`) → `403` (`IStatusCodeHttpResult` = 403, **не** 500).
3. Пустой `commodityIds` → `400`.
4. `> 200` id → `400`; `Guid.Empty` → `400`; дубликаты → `400`.
5. Успех → `200`, `Ok<...>` содержит `items` и `aiAvailable`.
6. `AI:BaseUrl` пуст → **`200`** (не `503`), сервис вызван, `aiAvailable = false`.
7. `ArgumentException` из сервиса (лимит названий) → `400`.
8. Неизвестный id → `200` и отсутствие записи в `items` (на уровне сервиса-заглушки — просто `200`).

Save:
9. Нет пользователя → `401`; не админ → `403`.
10. Пустой `items` → `400`.
11. `category = "123"` (число) → `400`; `category = "NotACategory"` → `400`; `category = "Undefined"` → `400`.
12. `category = null`/`""`/`"   "` → элемент не передаётся в сервис (`Received` с ожидаемым набором);
    если все элементы пропущены → `200 { updated = 0 }`, сервис не вызван.
13. Успех → `200 { updated = N }` из ответа сервиса.
14. `CommoditiesNotAccessibleException` из сервиса → `409`.
15. Дубликат `commodityId` → `400`.
16. Регистр имени категории (`"dairy"`) принимается (как в `ReceiptCategorizationEndpoints`).

### 10.4 Права и рефакторинг

17. `AdminAuthorization` используется обоими модулями; `PrecategorizedCommodityEndpointsTests`
    зелёные **без правок** (403 остаётся 403).
18. `CommodityCategorizationServiceTests` зелёные **без правок** (регрессия после выноса resolver).
19. `Architecture/ProjectDependencyTests` зелёные: новые контракты — в Application, реализации — в
    Infrastructure/Api; Domain не зависит от внешних слоёв.

### 10.5 Frontend

Автотестов фронтенда в проекте нет (ADR 011 E1, ADR 020 факт 5). Барьер:
`cd Analytics/frontend && npm run build && npm run lint` + ручная приёмка.

### 10.6 Ручная приёмка (чек-лист)

1. Админ, фильтр «Без категории», размер страницы 5 → кнопка «Категоризировать страницу (5)».
2. Запуск: селекты появляются сразу, строки заполняются порциями, прогресс «X из Y», страница не «висит».
3. Уже категоризированная строка (фильтр «Все товары») — значение предзаполнено, «уже присвоена»,
   в порции не участвует.
4. Одинаковое название в 3 строках — одинаковое предложение и подпись у каждой; в логах — один вызов ИИ
   на название (проверить по счётчику запросов к ИИ/логам Debug).
5. «Сохранить категории» во время обработки → сохраняется видимое, тост с числом изменённых строк.
6. «Отмена» → черновик выброшен, блокировки сняты, данные не изменены.
7. Пустой селект у ранее категоризированной строки + сохранение → категория **не сброшена**.
8. Под фильтром «Без категории» после сохранения строки исчезают, страница наполняется следующими;
   на последней странице — переход на предыдущую; «Найдено товаров» обновлено.
9. `AI__BaseUrl=` (пусто) на dev: категоризация запускается, предупреждение об ИИ, часть строк заполнена
   из кэша/очереди, остальные «не определена», сохранение работает.
10. ИИ отвечает медленно/таймаут по части названий → частичный результат, «не определена», точечный
    повтор одной строки работает.
11. Не-админ: кнопки нет; прямой `curl` на оба эндпоинта → `403`; `GET /api/commodities` не изменился.
12. Конвейер из 2–3 страниц подряд без ручной пагинации.
13. Режим проверки: пагинация, размер страницы, фильтр, «Обновить», дата-ссылка — заблокированы с
    подсказкой; смена страницы невозможна.

## 4. Команды проверки

```bash
cd Analytics && dotnet test                      # бэкенд + архитектурные тесты
cd Analytics && dotnet build
cd Analytics/frontend && npm run lint && npm run build
```

## 5. Соответствие критериям приёмки (карта)

| Критерий | Где закрывается |
|---|---|
| Кнопка только админам, N строк без категории, неактивна при N = 0 | шаг 8 (`CommoditiesPage`), сервер: шаг 5 |
| Только строки текущей страницы | шаг 8 (снапшот страницы → порции), ADR-028 п. 1.3 |
| Режим проверки: селект, «уже присвоена», источник у каждой строки | шаги 7, 8 (`CommodityTable`) |
| Одинаковые названия — одно предложение, без повторных вызовов ИИ | шаг 7 (группировка), шаг 3 (дедуп в resolver), тест 10.2/4 |
| Постепенные результаты, прогресс | шаги 7, 8 |
| «Сохранить» доступна во время и после обработки | шаг 7 (`save()` в любой фазе), шаг 8 |
| Пустое значение не сбрасывает категорию | ADR-028 п. 1.2, шаги 4, 5; тест 10.3/12 |
| Блокировка пагинации/фильтра/размера в режиме проверки | шаг 8 (+ `Pagination.disabled`) |
| После сохранения: перезагрузка, тост N, порядок не «прыгает» | шаги 7, 8, ADR-028 п. 7 |
| Под фильтром «Без категории»/«Не указана» страница наполняется, пустая → предыдущая | шаг 8 (`useCommodities`), тесты 10.5 (ручные 8) |
| Пустой/недоступный ИИ → работает кэш/очередь + понятное сообщение | шаги 4, 5, 8; тесты 10.2/5, 10.3/6 |
| Сбой по названию не блокирует остальные и сохранение | шаг 3 (resolver), тест 10.2/6, 10.2/7 |
| Повтор на размеченной странице корректен | шаг 8 (`candidateCount = 0` → кнопка неактивна) |
| Не-админы: нет кнопки и нет доступа (сервер) | шаг 5, тесты 10.3/1–2, 10.3/9 |
| Автотесты Analytics проходят, поведение покрыто | разделы 10.1–10.4 |
| Ручной прогон на dev | раздел 10.6 |

## 6. Замечания для реализации

1. **Не менять** `PUT /api/receipts/{id}/categories` и `POST /api/receipts/{id}/categories/suggest`
   (ни `503`, ни reset-семантику) — это прямо запрещено C11.
2. **Owner-фильтр писать явно** (`c.Receipt.UserId == userId`): глобальный query-filter в
   `CommodityRepository` не активен — полагаться на него нельзя (ловушка, см. ADR-028, факты кода).
3. **`Results.Forbid()` не использовать**: в приложении нет схемы аутентификации → 500 вместо 403.
4. **Не кэшировать результаты suggest**: `TryAdd` только после успешного сохранения (ADR-019).
5. **Не писать в `precategorized_commodities`** из страницы (ADR-027): только чтение.
6. **Дедлайн не должен отменять запрос клиента** и наоборот: два разных токена (`aiCancellationToken`
   vs `cancellationToken`), тесты 10.2/7–8 обязательны.
7. **Не перезаписывать `touched`-строки** пришедшими предложениями (иначе поздняя порция стирает
   введённое).
8. **Не дублировать нормализацию названий на клиенте** — группировка по точному названию, правила
   нормализации живут только в `CommodityNameNormalizer`.
9. **Не перезапрашивать список во время режима проверки** (стабильность порядка и сохранность черновика).
10. При любом противоречии с ADR-028 или критериями приёмки — эскалация к @software-architect
    (не решать самостоятельно), с указанием пункта ADR.
