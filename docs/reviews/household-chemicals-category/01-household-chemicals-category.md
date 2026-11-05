# Задача 01 — Бытовая химия

- **Задача**: docs/plans/household-chemicals-category.md (категория «Бытовая химия», код 53 + группа «Для дома»)
- **Дата**: 2026-11-05
- **Ревью**: раундов — 1, вердикт APPROVED (0 blockers)

## Что сделано

Добавлена новая категория товаров «Бытовая химия» (`HouseholdChemicals`, код 53) со
составом по заказчику: чистящие средства, стиральные порошки, средства для мытья
посуды и посудомоечных машин, освежители воздуха, губки/перчатки для уборки,
бумажные полотенца и салфетки, мусорные пакеты, средства от насекомых. Введена
группа UI «Для дома», в которую входят четыре категории: «Товары для дома»,
«Мебель», «Бытовая техника» (три последние переехали из плоского списка) и новая
«Бытовая химия» — обновления фронтенда не потребовались, группа задаётся на
бэкенде. В AI-промпт добавлена одна строка разграничений с границами к
`HomeGoods` (посуда/текстиль/хранение/интерьер) и `CosmeticsAndHygiene`
(мыло, шампуни, зубная паста). Изменение строго аддитивное: без миграций БД,
SQL-скриптов, изменений фронтенда и Go-сервисов (ADR 031, решения A1/B1, C1, D1, E1, F1).

## Изменённые файлы

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs` —
  enum-член `HouseholdChemicals = 53` (после `FitnessAndSports = 52`, до `Other = 255`,
  с комментарием-составом); `DisplayNames` +1 запись «Бытовая химия» (последней в списке,
  перед `Other`); константа `HomeGroup = "Для дома"`; `CategoryGroups` +4 записи
  (`HomeGoods`, `Furniture`, `HomeAppliances`, `HouseholdChemicals`). устаревшие
  комментарии о «плоском» статусе кодов 48–50 и legacy 0–17 обновлены (только текст,
  коды не менялись).
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/AI/OpenAiCompatibleAiClient.cs` —
  одна строка разграничений в `BuildPayload` (блок «Разграничения схожих категорий»);
  существующие строки `Furniture`/`HomeAppliances` сохранены; реестр категорий и
  валидация ответа подхватывают новую категорию автоматически.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryTests.cs` —
  счётчик enum: `..._ShouldBe54` → `..._ShouldBe55` (55); новый тест
  `HouseholdChemicalsCategory_HasExpectedCodeAndGroup` (код 53, имя, группа);
  `NewRetailCategories_..._And_NoGroup` → `..._AndGroups` (`Furniture`/`HomeAppliances`
  перенесены в «Для дома», `CraftSupplies` остался плоским); ассерты группы «Для дома»
  в `GetGroup_ReturnsExpectedGroupsForNewCategories`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityEndpointsTests.cs` —
  `ListCategories_returns_key_equal_to_enum_name`: новая категория
  (Key `HouseholdChemicals`, Id 53, Name «Бытовая химия», Group «Для дома»);
  `Furniture` (49)/`HomeAppliances` (50) — теперь `Group == "Для дома"`.
- `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/OpenAiCompatibleAiClientTests.cs` —
  промпт содержит реестр `HouseholdChemicals`/«Бытовая химия» и текст разграничения
  (подстроки «средства от насекомых», «не CosmeticsAndHygiene»).

## Как проверено

- `cd Analytics && dotnet build` — 0 ошибок (30 предсуществующих warning'ов зависимостей,
  в том числе NU1903 Snappier, к изменению отношения не имеют).
- `cd Analytics && dotnet test` до обновления тестов — падение ровно двух
  прогнозируемых ассертов (`CommodityCategory_EnumCount_ShouldBe54`,
  `NewRetailCategories_HaveExpectedCodes_And_NoGroup`), остальные 350 зелёные —
  подтверждение локальности изменения.
- `cd Analytics && dotnet test` после обновления тестов — **Passed 353, Failed 0**
  (было 352, +1 новый тест).
- Самостоятельный прогон @code-reviewer — тот же результат: 353 пройдено, 0 упало.

## Итоги ревью

- Исправлено по замечаниям ревьюера: не требовалось (раунд 1, 0 blockers).
- Оставлено без изменений: `CommodityCategorizationServiceTests` (вне списка пяти файлов;
  его реестровые ассерты проходят без правок — полнота реестра проверяется динамически
  через `GetAll().Count(...)`) — осознанное решение, принято ревьюером.

## Заметки для коммита

- Рекомендуемое сообщение коммита:

  ```
  feat(analytics): категория «Бытовая химия» (HouseholdChemicals = 53) и группа «Для дома» (ADR 031)

  - enum-член HouseholdChemicals = 53 + отображаемое имя «Бытовая химия»
  - новая группа UI «Для дома»: HomeGoods, Furniture, HomeAppliances, HouseholdChemicals
    (Furniture и HomeAppliances переехали из плоского списка — ADR 031, решение D)
  - строка разграничений с HomeGoods/CosmeticsAndHygiene в AI-промпте
  - обновлены тесты: счётчик 55, код 53/имя/группа, промпт-ассерты (353 зелёных)
  ```

- Внимание при ручной проверке (чек-лист шага 4 плана, критерий приёмки задачи):
  1. Страница товаров и «Детали чека»: в выпадающих списках есть категория «Бытовая
     химия» внутри `<optgroup>` «Для дома» вместе с «Товары для дома», «Бытовая
     техника», «Мебель» (порядок групп: плоские legacy → «Для дома» → «Продукты»
     → «Транспорт» → «Прочее», плоский `Other`).
  2. Ручная категоризация товара как «Бытовая химия» сохраняется и корректно
     отображается после перезагрузки страницы.
  3. AI-подсказка на пограничниках: «Мистер Мускул», «Стиральный порошок» →
     «Бытовая химия»; «Шампунь», «Мыло» → остаётся «Косметика и гигиена».
  4. `/precategorized` доступен; механика подтверждения предложения с кодом 53
     не срабатывает со сбоем.
- **Предупреждение о рабочем дереве**: в нём есть несвязанный незакоммиченный diff —
  `Analytics/src/ReceiptCollector.Analytics.Api/appsettings.json` (локальные dev-настройки
  AI) и, возможно, `appsettings.Development.json`. В коммит этой задачи их **не включать**:
  добавляются только пять файлов, перечисленных выше, плюс файл декомпозиции
  `docs/plans/household-chemicals-category.md` и эта сводка.
