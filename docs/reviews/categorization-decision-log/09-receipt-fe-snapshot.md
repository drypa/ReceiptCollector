# Задача 09 - Frontend: снимок предложения при сохранении чека

- **Задача**: docs/plans/categorization-decision-log.md#t-09-frontend-снимок-предложения-при-сохранении-чека
- **Дата**: 2026-11-08
- **Ревью**: раундов - 1, вердикт APPROVED

## Что сделано

Сохранение категорий на странице чека (`PUT /api/receipts/{id}/categories`) теперь отправляет по
каждой позиции аддитивный снимок предложения, которое человек видел на экране (решение A1,
ADR-033 п. 3): `suggestedCategory` (ключ enum, как `category`) и `suggestionSource` (wire-имя).
Снимок формируется только для источников `ai`/`precategorized`/`cache`/`merchant-default` и
только если `categoryId` маппится в `key` через справочник категорий; `existing`, `undefined`,
неизвестный источник и несопоставленный код снимка не дают. Снимок не зависит от выбранного
значения select'а, поэтому сброс категории («—») тоже отправляет снимок — сервер запишет событие
с `final = Undefined` (`0`). Если suggest не запускался, элемент запроса остаётся ровно
`{ commodityId, category }`; разметка, кнопки, тосты, режим `categorizeModeActive`, контракты
`suggest` и `SaveCategoriesResult` не менялись.

## Изменённые файлы

- `Analytics/frontend/src/api/receipts.ts` - `SaveCategoriesItem` += опциональные
  `suggestedCategory`/`suggestionSource` с XML-доком; `ReceiptItemSuggestion.source` расширен до
  `CategorizationSource | string` (устойчивость к будущим аддитивным значениям сервера, как у
  `CommodityCategorizationItem.source`). Сигнатуры и wire-контракты не менялись.
- `Analytics/frontend/src/components/ReceiptDetails.tsx` - константа
  `SUGGESTION_SNAPSHOT_SOURCES` (четыре wire-имени); в `saveSelectedCategories` снимок
  собирается из состояния `suggestions` и добавляется поверх `commodityId`/`category`; карта
  `id -> key` строится по `selectableCategories` (единственный источник истины «Undefined — не
  категория»), объявление `selectableCategories` перенесено выше функции сохранения.
- `docs/plans/categorization-decision-log.md` - статус T-09: `pending` -> `in-progress` ->
  `review` -> `approved`.
- `docs/reviews/categorization-decision-log/09-receipt-fe-snapshot.md` - эта сводка (новый файл).

## Как проверено

- **Автотестов фронта в проекте нет** (ADR-033 п. 8; в `package.json` нет `test`): выполнены
  только статические проверки и разбор кода.
- `cd Analytics/frontend && npm run lint` - exit 0, вывод пустой (`eslint . --max-warnings 0`).
- `cd Analytics/frontend && npm run build` - успешно: `tsc -b && vite build`, 60 modules
  transformed, `built in 228ms`.
- **Ручной сценарий с БД не прогонялся** (нет доступа к окружению с PostgreSQL из этой сессии).
  Он вынесен человеку в «Заметки для коммита» ниже: DevTools-payload, строки в
  `categorization_decisions`, сброс -> `final = 0`, пустой `AI:BaseUrl` -> `503`.
- Разбор кода по веткам: при `suggestions === null` карта предложений пуста и каждый элемент -
  ровно `{ commodityId, category }` (состав полей прежний); при валидном снимке добавляются
  только два новых ключа; `category` остаётся `selectedCategories[item.id] || null`, поэтому
  сброс даёт `null` при сохранённом снимке.
- После замечаний ревью (не блокирующих) повторно прогнаны `npm run lint` (exit 0) и
  `npm run build` (успешно).

## Итоги ревью

- Вердикт: **APPROVED**, раунд 1, блокеров нет. Ревьюер подтвердил совпадение четырёх wire-имён
  с серверным `CategorizationDecisionSources`, отсечение `existing`/`undefined`/неизвестного
  источника, построение ключа категории по справочнику с отсечением `Undefined`, прежний payload
  без suggest, независимость снимка от выбранного значения, неизменность UI/контрактов и
  аддитивность типов.
- **`Undefined` исключён из карты `id -> key`** - защита от невалидного снимка
  (`suggestedCategory: "Undefined"`): справочник `/api/commodities/categories` содержит
  `Undefined` (id 0). Для четырёх допустимых источников resolver код 0 не отдаёт
  (`existing` - только `Id: not 0`, `merchant-default` отсекает `Undefined`), поведение не
  меняется.
- **Локальная константа `SUGGESTION_SNAPSHOT_SOURCES`** оставлена намеренно в
  `ReceiptDetails.tsx`: план T-09 ограничивает файлы, а переиспользование нужно только странице
  «Товары». **Требование к T-10:** при реализации T-10 вынести список допустимых источников и
  правило «снимок только при валидном источнике и категории» в общий util (например,
  `utils/categorizationSource.ts`), чтобы не дублировать четвёрку wire-имён.
- **Замечание о дублировании фильтра `'Undefined'` с `selectableCategories` - применено.**
  Карта `id -> key` теперь строится по `selectableCategories`, объявление перенесено выше
  `saveSelectedCategories`; отдельного строкового литерала `'Undefined'` в функции сохранения
  больше нет. Поведение не изменилось, lint/build зелёные.
- **Замечание о расхождении типов `ReceiptItemSuggestion.source` (union) и
  `CommodityCategorizationItem.source` (`CategorizationSource | string`) - применено.**
  Тип расширен до `CategorizationSource | string` с комментарием: сервер вправе аддитивно
  добавить источник, а снимок всё равно берут только четыре известных wire-имени. Wire-контракт
  и рантайм не затронуты.
- Правки по замечаниям сделаны после вердикта APPROVED: они не меняют поведение (рефакторинг
  фильтра и расширение TS-типа), повторно проверены `npm run lint` и `npm run build`. Новых
  блокеров нет, повторный раунд ревью не требовался.

## Заметки для коммита

- Рекомендуемое сообщение: `T-09 receipt save suggestion snapshot`.
- Включить в коммит:
  - `Analytics/frontend/src/api/receipts.ts`
  - `Analytics/frontend/src/components/ReceiptDetails.tsx`
  - `docs/plans/categorization-decision-log.md`
  - `docs/reviews/categorization-decision-log/09-receipt-fe-snapshot.md`
- **Не включать:**
  - `Analytics/src/ReceiptCollector.Analytics.Api/wwwroot/index.html` - артефакт сборки фронта,
    локальный `npm run build` обновил ссылку на новый bundle (не коммитим);
  - `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` и
    `appsettings.Development.json` - предсуществующие локальные правки, к задаче не относятся.
- В `docs/plans/categorization-decision-log.md` в рабочем дереве есть ещё одна незакоммиченная
  правка до начала T-09 - статус T-08 `approved -> committed` (выставлен после коммита T-08); она
  относится к предыдущей задаче, но находится в том же файле.
- Ручная проверка человеком:
  1. DevTools, Network: чек -> suggest -> «Сохранить категории». В
     `PUT /api/receipts/{id}/categories` у позиций с источниками `ai`/`precategorized`/`cache`/
     `merchant-default` есть `suggestedCategory` (ключ enum) и `suggestionSource` (wire-имя); у
     позиций с `existing`/`undefined`/без предложения - только `commodityId`/`category`.
  2. Suggest не запускался -> состав полей запроса прежний (`commodityId`, `category`).
  3. Suggest -> сохранить без правок -> в `categorization_decisions` появились согласия по
     позициям с предложением (`suggested_category_id = final_category_id`).
  4. Suggest -> сбросить одну позицию в «—» -> по ней событие с `final_category_id = 0` и тем же
     `suggested_category_id`.
  5. Пустой `AI:BaseUrl` -> suggest отвечает `503` -> сохранение категорий работает как раньше,
     записей в `categorization_decisions` нет.
