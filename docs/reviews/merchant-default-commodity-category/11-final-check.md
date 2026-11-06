# Задача 11 - Финальная проверка и ручная приёмка

- **Задача**: docs/plans/merchant-default-commodity-category.md#t-11-финальная-проверка-и-ручная-приёмка
- **Дата**: 2026-11-06
- **Ревью**: раундов - 2, вердикт APPROVED (раунд 1 - CHANGES_REQUESTED: правки текста
  руководства; раунд 2 - оба blocker'а сняты)

## Что сделано

Автоматическая часть финальной проверки фичи «категория товаров по умолчанию для магазина»
(T-01…T-10 закоммичены): прогнаны все проверки (`dotnet test` 442/442, frontend lint + build —
чисто), каждый критерий приёмки задачи и каждый edge case сверен с фактом по коду и тестам
(таблицы ниже), фиксированы артефакты рабочего дерева (`wwwroot/index.html`, локальные
`appsettings.*`). Код в этой задаче не изменялся. Ручная приёмка на живой среде за меня
невыполнима — ниже точный пошаговый сценарий с SQL-проверками, включая отдельную проверку
`/receipts` и `/merchants/{id}/receipts` на реальной Postgres (заметка ревью T-10).

## Изменённые файлы

- `docs/plans/merchant-default-commodity-category.md` — статус T-11 `in-progress` -> `review`;
  отметки выполненного в чек-листе T-11 (п. 1 автоматический; п. 5 — эта сводка).
- `docs/reviews/merchant-default-commodity-category/11-final-check.md` — эта сводка.

## Как проверено (фактические результаты)

- `cd Analytics && dotnet test` — **Passed! Failed: 0, Passed: 442, Skipped: 0, Total: 442**
  (базлайн 442 совпал; Testcontainers-тесты T-04/`ReceiptReadServiceTests` реально исполнены —
  Docker доступен, ryuk-контейнер поднимался; без Docker эта часть прогона невалидна).
- `cd Analytics/frontend && npm run lint` — **чисто**, `eslint . --max-warnings 0` без вывода.
- `cd Analytics/frontend && npm run build` — **успешно** (tsc -b + vite, 60 modules, 240 ms):
  `wwwroot/index.html`, `assets/index-BZIFuWyn.css`, `assets/index-kGy9dn-A.js`.
- `git status` после пересборки: `M Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` —
  **артефакт**: HEAD содержит сборку T-09 (`index-D0ZJQYTx.js`), рабочее дерево — актуальную
  (`index-kGy9dn-A.js`, включает T-10); коммит T-10 не включил `index.html`. Пересборка даёт тот же
  хэш —diff воспроизводим и актуален. Паттерн `wwwroot` (`.gitignore`, строка 32) игнорирует всю
  директорию — в трекинге остаются только `index.html` и `vite.svg`; пересобранный `index.html`
  входит в финальный коммит за человеком.
- Локальный контейнер `rc-mig-verify` (postgres:18, поднят 2026-11-05) проверен read-only: БД `rc`
  пуста — доказательством применения миграции на dev он **не является**; пункт остаётся ручным.

## Критерии приёмки задачи -> реализация -> подтверждения

| Критерий | Где реализован | Чем подтверждён | Статус |
|---|---|---|---|
| Задать/изменить дефолт в `/merchants` и «Детали чека» | `PUT /api/merchants/{id}/default-commodity-category` (`MerchantEndpoints.UpdateDefaultCommodityCategory`); `MerchantTable.tsx` (колонка + select); `ReceiptDetails.tsx` (селект в блоке магазина) | `MerchantDefaultCategoryEndpointsTests` (set/reset/400/403/404/401); UI — ручная проверка | авто + ручная UI |
| Хранится отдельно от категории магазина | `merchants.default_commodity_category_id` (миграция T-01) ≠ `merchants.category`; `Merchant.DefaultCommodityCategory` | `MerchantEntityMappingTests` (round-trip), миграционный скрипт | авто + SQL в сценарии (шаг 1) |
| Новые чеки категоризируются автоматически | `ReceiptSynchronizationService.ApplyMerchantDefaultCategoryAsync` после `AddAsync` (C1) | `ReceiptSynchronizationServiceTests` (4 теста: применён/не настроен/сбой не валит/add fail) | авто; сквозная проверка — сценарий (шаг 3) |
| Дожим только позиции конкретного магазина без категории | `CommodityRepository.ApplyMerchantDefaultForMerchantAsync` (`merchant_id + NULL/0`, `IgnoreQueryFilters()` с grep-able комментарием E1); `MerchantDefaultCategoryService`; `POST .../apply` | `CommodityRepositoryMerchantDefaultTests` (все пользователи, чужие магазины не тронуты); `MerchantDefaultCategoryServiceTests` (6); `MerchantDefaultCategoryEndpointsTests` (apply) | авто; счётчик в UI — сценарий (шаг 4) |
| Порядок: ручная -> дефолт -> кэш -> precategorized -> AI | `CommodityCategoryResolver` шаг 2 между `existing` и `cache` (D1) | `CommodityCategoryResolverTests` (existing важнее дефолта; дефолт важнее кэша/очереди/AI; один batch-запрос) | авто |
| Ручные категории никогда не перезаписываются | Предикат `CategoryId == null || == 0` в обеих операциях; suggest read-only; `existing`-шаг выше | `ApplyForReceipt_UpdatesOnlyUncategorizedItemsAndMarksSource`, `Resolve_prefers_existing_category_over_merchant_default` | авто + SQL в сценарии (шаг 4) |
| Источник `merchant-default`, отличимый (в т.ч. после перезапуска) | persisted-колонка `commodities.category_source` (B1, единственная точка записи — операции T-04); wire `CategorizationSourceNames.MerchantDefault`; подпись фронта `categorizationSource.ts` | `ApplyForReceipt_...MarksSource`, `ApplyMerchantDefault_IsIdempotent_...KeepsSource`, `ExistingCategoryUpdatePaths_DoNotWriteCategorySource`, `ToWireName_maps_all_sources...` | авто + SQL в сценарии (шаги 3–5) |
| Магазины без дефолта — прежний поток | Resolver: `null`/нет ключа -> шаги cache/queue/AI (FR-M8); синхронизация: `null` -> блок пропускается | `Resolve_merchant_without_default_falls_through_to_cache`, `Resolve_without_provider_keeps_previous_pipeline_behaviour`, `SynchronizeAsync_does_not_apply_merchant_default_when_not_configured` | авто + edge E1 в сценарии |
| Смена/снятие дефолта не меняет сохранённые товары | FR-M7 по построению: ни один путь записи не читает дефолт ретроспективно; сброс только `UpdateDefaultCommodityCategory(null)` | `UpdateDefaultCategory_with_null_resets_and_returns_nulls` + предикат `NULL/0` (повторный дожим не трогает заполненные) | авто + сценарий (шаг 5) |
| Unit-тесты; ручная проверка UI | 442 теста | прогон выше | авто; UI — сценарий ниже |

### Edge cases задачи

| Edge case | Реализация | Подтверждение |
|---|---|---|
| Дефолт не задан | Resolver промах -> обычный поток; импорт пропускает блок; apply -> 400; UI скрывает кнопку | тесты resolver/sync/endpoints + E1 в сценарии |
| Уже есть ручная категория | Предикат `NULL/0` везде, `existing` выше | `ApplyForReceipt_UpdatesOnlyUncategorizedItemsAndMarksSource` + шаг 4 сценария |
| Категория от AI/precategorized/кэша (сохранённая) | Содержательная (`!= NULL/0`) не трогается; в suggest дефолт бьёт только машинные *предложения* (позиции без сохранённой категории) | `Resolve_prefers_existing_category_over_merchant_default`, `Resolve_uses_merchant_default_and_skips_cache_queue_ai` + примечание шага 4 |
| Смена/снятие дефолта | Строки не перечитываются; новые чеки -> новая категория; повторный дожим — только без категории | шаг 5 сценария |
| Дефолт -> код вне справочника | Валидация при установке (`Enum.IsDefined` + `!= 0`, CHECK `<> 0` в БД); безопасное чтение: `MapToDomain`/провайдер/`ToMerchantDto` -> `null` = «не задана» | `UpdateDefaultCategory_with_invalid_category_returns_bad_request`, `ApplyExistingAsync_invalid_registry_code_read_as_null_returns_default_not_configured`, `MerchantEntityMappingTests` + E5 сценария |
| Чек без merchant | Импорт без merchant пропускается (существующее правило); resolver по `receiptId` без записи промахивается | `SynchronizeAsync_when_add_fails_does_not_apply_merchant_default` + тесты resolver |

## Пункты T-11, требующие живой среды (ручная приёмка)

1. Применение миграции на dev (`dotnet run` в Migrations) + наличие скрипта в
   `migration_scripts_history`, `\d merchants` / `\d commodities`.
2. Сквозной сценарий UI (шаги 1–5 ниже) — `/merchants`, «Детали чека», дожим, подпись
   «по умолчанию магазина» в suggest.
3. Реальный импорт чека через синхронизацию из MongoDB (шаг 3).
4. `/receipts` и `/merchants/{id}/receipts` на реальной Postgres с применённой миграцией
   (заметка ревьюера T-10): summary-проекции читают колонку `default_commodity_category_id` —
   EF InMemory этого не валидирует (Testcontainers покрывает SQL по EF-схеме, но не dev-БД).
5. Edge cases E1–E6 со стенда.

## Пошаговый сценарий ручной приёмки

Обозначения: `:mid` — `merchants.id` выбранного магазина (столовая), SQL — в psql базы
`receipts` (Analytics Postgres). Ожидаемые коды справочника: `Undefined = 0`,
`AutomotiveGoods = 12` («Автотовары»), `ReadyMeals = 29` («Готовая еда и кулинария»).

### Подготовка

- P1. Применить миграцию: `cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run`
  (повторный прогон обязан пройти без ошибок — идемпотентность).
- P2. Проверить схему и историю:
  ```sql
  SELECT script_name, applied_on FROM migration_scripts_history
   WHERE script_name LIKE '%merchant_default%';            -- ровно 1 строка
  -- \d merchants  -> default_commodity_category_id integer NULL
  --                + CONSTRAINT ck_merchants_default_commodity_category CHECK (...)
  -- \d commodities -> category_source character varying(32)
  ```
- P3. Подняться в UI под **админом** (nginx -> Analytics API).

### Шаг 1. `/merchants` — задать дефолт (FR-M1)

1. Открыть `/merchants`: новая колонка «Категория товаров (по умолчанию)» = «Не задана».
   Запомнить/снять SQL-снапшот категории самого магазина:
   ```sql
   SELECT name, category, default_commodity_category_id FROM merchants WHERE id = ':mid';
   ```
2. «ред.» в новой колонке -> select из товарного справочника (без Undefined) -> выбрать
   «Готовая еда и кулинария» -> сохранить -> тост успеха.
3. **Перезагрузить страницу** — значение сохранилось. SQL: `default_commodity_category_id = 29`,
   колонка `category` (категория магазина) — **не изменилась** (отдельное хранение, FR-M1).
4. Сменить на «Автотовары» (12) и вернуть на 29 — работает из того же select'а.
5. Не-админ: колонки нет, таблица работает как раньше (выйти/второй браузер).

### Шаг 2. «Детали чека» — селект дефолта

1. Открыть любой чек этого магазина (`/receipts` -> клик).
2. В блоке магазина под именем — «Категория товаров по умолчанию»: «Готовая еда и кулинария»
   (значение из `merchant.defaultCommodityCategoryName` — ответ `GET /api/receipts/{id}`,
   DevTools Network).
3. «ред.» -> сменить на «Автотовары» -> сохранить -> значение обновилось без перезагрузки и
   после неё; SQL: `default_commodity_category_id = 12`. Вернуть на 29.
4. Соседние функции не сломаны: переименование магазина, «Категоризировать» чека, сохранение
   выбранных категорий — как раньше.

### Шаг 3. Новый чек магазина (FR-M2, источник `merchant-default`)

1. Убедиться, что синхронизация включена (`Infrastructure:Receipts:Synchronization:Skip = false`)
   и дефолт = 29.
2. Обеспечить новый чек магазина: новый чек с терминала, либо ручная вставка нового raw-тикета
   в MongoDB (`receipt_collection`, коллекция по `Infrastructure:Receipts:Mongo:Collection`,
   в dev — `raw_tickets`) с **новым**
   external id / естественным ключом (dedup пропускает уже импортированные), затем дождаться
   цикла синхронизации (или рестарт Analytics API). Вставку делать именно в MongoDB —
   автоприменение живёт в импорте, прямой INSERT в Postgres его не вызывает.
3. Дождаться импорта; проверить:
   ```sql
   SELECT r.id, c.name, c.category_id, c.category_name, c.category_source
     FROM commodities c JOIN receipts r ON c.receipt_id = r.id
    WHERE r.merchant_id = ':mid'
    ORDER BY r.purchased_at DESC LIMIT 20;      -- последние позиции нового чека
   ```
   Все позиции нового чека: `category_id = 29`, `category_name = 'Готовая еда и кулинария'`,
   `category_source = 'merchant-default'`. В логах Analytics — строка с числом применённых.
4. Подпись источника: открыть **исторический** чек этого магазина, позиции которого остались
   без категории (импортированы до фичи), нажать «Категоризировать» (или открыть эти имена на
   странице «Товары») -> предложения = «Готовая еда и кулинария» с подписью
   «по умолчанию магазина». Позиции шага 3 покажут source «existing» (категория уже сохранена) —
   это корректно; именно для несохранённых позиций должен быть `merchant-default`, а не
   «кэш»/«ИИ».

### Шаг 4. Кнопка «Категоризировать существующие» (FR-M3, дожим)

1. Снять снапшоты «до» (записать числа). Оговорка: между снапшотом и нажатием кнопки не должно
   пройти цикла синхронизации (`Infrastructure:Receipts:Synchronization:IntervalSeconds`,
   по умолчанию 600 с) и иных записей категорий (ручных/AI-подтверждений, confirm
   precategorized), иначе N разойдётся с фактическим числом дожима;
   ```sql
   -- позиции этого магазина без категории (кнопка обнулит этот счётчик)
   SELECT count(*) FROM commodities c JOIN receipts r ON c.receipt_id = r.id
    WHERE r.merchant_id = ':mid' AND (c.category_id IS NULL OR c.category_id = 0);
   -- позиции других магазинов без категории (не должны измениться)
   SELECT count(*) FROM commodities c JOIN receipts r ON c.receipt_id = r.id
    WHERE r.merchant_id <> ':mid' AND (c.category_id IS NULL OR c.category_id = 0);
   -- ручные/иные сохранённые категории этого магазина (set значений не должен измениться)
   SELECT c.id, c.category_id FROM commodities c JOIN receipts r ON c.receipt_id = r.id
    WHERE r.merchant_id = ':mid' AND c.category_id NOT IN (0)
      AND c.category_source IS DISTINCT FROM 'merchant-default' ORDER BY 1;
   ```
2. В «Детали чека» этого магазина нажать «Категоризировать существующие» (видна только при
   админе и заданном дефолте) -> тост «Обновлено товаров: N», N = счётчику из первого запроса.
3. Проверить после:
   - первый запрос = **0**; второй — **без изменений**; третий — строки и `category_id`
     **те же** (ручные/AI/precategorized не перезаписаны, `category_source` у них остался NULL);
   - ```sql
     SELECT count(*) FROM commodities c JOIN receipts r ON c.receipt_id = r.id
      WHERE r.merchant_id = ':mid' AND c.category_source = 'merchant-default';
     ```
     выросло ровно на N;
   - E1 (все пользователи):
     ```sql
     SELECT count(DISTINCT r.user_id) FROM commodities c JOIN receipts r ON c.receipt_id = r.id
      WHERE r.merchant_id = ':mid' AND c.category_source = 'merchant-default';
     ```
     значение больше 1, если у магазина есть чеки других пользователей (в UI это видно
     только владельцу — сверять SQL);
   - позиции **других** магазинов и других пользователей вне этого магазина — не тронуты.
4. **Идемпотентность**: нажать кнопку повторно -> тост «Все позиции уже категоризированы»
   (`updated = 0`), SQL-снимки не изменились, `category_source` ранее заполненных строк не
   переставлен.
5. Вручную категоризировать одну позицию чека в UI (другая категория), затем ещё раз кнопку ->
   она **не** перезаписана дефолтом (попадёт в «ручные» снимки шага 1).

### Шаг 5. Смена/снятие дефолта (FR-M7)

1. Сменить дефолт на «Автотовары» (12): SQL — `default_commodity_category_id = 12`, а
   ```sql
   SELECT count(*) FROM commodities c JOIN receipts r ON c.receipt_id = r.id
    WHERE r.merchant_id = ':mid' AND c.category_source = 'merchant-default' AND c.category_id = 29;
   ```
   (для столовой) — **не изменилось** (сохранённые товары не тронуты).
2. Снять дефолт («— не задана —»): DTO `categoryId = null`; кнопка «Категоризировать
   существующие» в деталях чека **исчезает**; прямой вызов
   `curl -X POST .../default-commodity-category/apply` (с авторизацией) -> `400`
   «Default commodity category is not set for this merchant.», а не 500.
3. Новый чек магазина (как шаг 3) без дефолта -> позиции без категории, `category_source` NULL
   (обычный поток). Вернуть дефолт 29 -> очередной новый чек снова `merchant-default`.

### Проверка заметки ревьюера T-10 (реальная Postgres)

- Открыть `/receipts` (список) и `/merchants/{id}/receipts` (чеки магазина) под обычным
  пользователем и админом: страницы отдают 200 без 500 (summary-проекции читают
  `merchants.default_commodity_category_id` — в ответе `merchant.defaultCommodityCategoryId/Name`
  присутствуют: с дефолтом — 29/«Готовая еда и кулинария», без — null/«Не задана»; невалидный
  код — см. E5). Проверять в DevTools Network на стенде **с применённой миграцией P1**.

### Edge cases со стенда

- E1. Магазин **без** дефолта: новый чек -> позиции без категории; «Категоризировать» ->
  обычные подписи (кэш/очередь/ИИ), без «по умолчанию магазина».
- E2. Товар с ручной категорией -> не перезаписан (шаг 4.5).
- E3. Товар с сохранённой категорией от AI/precategorized/кэша -> дожим не трогает; suggest
  такой позиции возвращает её же категорию («existing»). Дефолт перекрывает машинные
  предложения только у позиций **без** сохранённой категории — это и есть FR-M4.
- E4. Чек без привязки к merchant (пустой `UserInn`): импорт такого чека по-прежнему
  пропускается (существующее правило до фичи) — сам чек в Postgres не появляется, цикл
  синхронизации **не падает**: по нему логируется `InvalidOperationException`
  «Receipt seller is not specified.» (`ReceiptSynchronizationService.cs:266`) и Error
  «Failed to import receipt {ReceiptExternalId}.» (catch, :157-159) — как и до фичи;
  остальные чеки в пакете импортируются. Обычный suggest по остальным (импортированным)
  чекам работает как раньше: у непроимпортированного чека позиций в Postgres нет.
- E5. Нерукописная/устаревшая категория дефолта в БД (эмуляция «удалена из справочника»):
  ```sql
  UPDATE merchants SET default_commodity_category_id = 0  WHERE id = ':mid';  -- ОШИБКА 23514 (CHECK) — Undefined задать нельзя
  UPDATE merchants SET default_commodity_category_id = 999 WHERE id = ':mid'; -- ok (валидация приложения в обход)
  ```
  После 999: UI показывает «Не задана» (безопасное чтение), `/apply` -> 400, кнопка скрыта,
  `/receipts` и детали чека без 500 с `defaultCommodityCategoryId: null`; автоприменение при
  импорте не срабатывает. Админ выставляет корректную категорию через UI (999 перезаписывается).
- E6. AI/кэш предлагают другое (осознанное следствие F1, не баг): пока у позиции нет
  сохранённой категории, suggest возвращает дефолт магазина даже если кэш/AI знают иное
  (подпись «по умолчанию магазина»). После применения (импорт/дожим) suggest — «existing».
  То же **название товара в магазине без дефолта** после дожима предложит категорию дефолта с
  подписью «кэш» — так работает сквозной кэш (ADR-032 F1 «Отрицательные»).

## Заметки для коммита

- **Обязательно включить** `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html`
  (актуальная сборка `index-kGy9dn-A.js`/`index-BZIFuWyn.css`): HEAD содержит сборку T-09,
  T-10 его не включил — без этого SPA на проде не отдаст UI T-09/T-10. Финальная пересборка
  перед коммитом diff не меняет (хэши контентные).
- **Исключить** `appsettings.json` и `appsettings.Development.json` — локальные dev-настройки
  (AI BaseUrl `127.0.0.1:1234`, telegram id админа, креды Mongo, `Skip=false`); к фиче не
  относятся (ADR-032: новых env-настроек нет).
- Рекомендуемое сообщение коммита:
  `t-11 of default merchant commodity category: final verification and manual acceptance guide`
- При ручной приёмке фиксировать фактические числа дожимов (N, 0) — это единственное живое
  подтверждение FR-M3/E1, автоматикой не воспроизводится на dev-данных.

## Допущения

- Сводка создана до вердикта code-reviewer (артефакт T-11 по указанию оркестратора); статус в
  плане — `review`, в `approved` переведётся только после APPROVED, вердикт дописывается в
  шапку.
- Зафиксированный факт базлайна: полный прогон `dotnet test` — 442 passed (совпал с базлайном
  после T-10); T-11 тестов не добавлял.
- Testcontainers-тесты (в т.ч. «эталонные» T-04 по сноске плана) считаются исполненными —
  Docker в момент прогона доступен; на машине без Docker прогон 442 неполон.
- Пункт чек-листа «миграция применена на dev» автоматической проверкой не покрыт: локальный
  контейнер `rc-mig-verify` пуст и как доказательство не учитывается.

## Итоги ревью

- Исправлено по замечаниям ревьюера (раунд 1):
  - полные ключи конфигурации `Infrastructure:Receipts:*` (`...:Synchronization:Skip`,
    `...:Mongo:Collection` с dev-значением `raw_tickets`) вместо несуществующих сокращений;
  - переформулированный E4 с корректным ожиданием логов (чек без продавца -> Error
    «Failed to import receipt ...», цикл не падает, как и до фичи);
  - полные SQL с JOIN вместо «...» (E1/шаг 4.3 и шаг 5.1);
  - точная формулировка `.gitignore` (паттерн `wwwroot` игнорирует всю директорию, в трекинге
    остаются `index.html` и `vite.svg`);
  - оговорка про `IntervalSeconds` (между снапшотом «до» и кнопкой не должно пройти цикла
    синхронизации, иначе N разойдётся);
  - «обычный suggest по остальным (импортированным) чекам» вместо «по позициям»;
  - убрана арифметика базлайна «436 + 6 = 442» — оставлен зафиксированный факт полного
    прогона.
- Принято к сведению: номера строк в ссылках на код протухают — при следующем касании сводки
  заменить `ReceiptSynchronizationService.cs:266` / `:157-159` на имена методов
  (`ResolveMerchantAsync` / catch в цикле импорта `SynchronizeReceipts`).
