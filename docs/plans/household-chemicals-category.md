# План реализации: категория «Бытовая химия» (HouseholdChemicals = 53) и группа UI «Для дома»

> Декомпозиция утверждённой задачи для @senior-developer. Источники:
> [задача household-chemicals-category](../tasks/household-chemicals-category.md)
> (критерии приёмки, edge cases, уточнения заказчика о составе категории),
> [ADR-031](../adr/031-household-chemicals-category.md) — основной документ реализации
> (решения A1/B1, C1, D1, E1, F1), а также ADR 010 (правило «только добавление»,
> группы с бэкенда), ADR 022 (словарь «категория → группа», стиль строк разграничений),
> ADR 005 (enum вместо таблицы БД).
>
> **Новых архитектурных решений план не содержит.** Всё ниже — порядок работ, файлы и проверки.
> Противоречие требований (если обнаружится) — эскалация к @software-architect, а не
> самостоятельное решение.

## Ход выполнения

| № | Задача | Слаг / файлы | Статус |
|---|--------|--------------|--------|
| 01 | Категория «Бытовая химия» (код 53) + группа «Для дома» + строка разграничений в AI-промпте + обновление тестов | `household-chemicals-category` | approved |

Задача одна, выполняется одним инкрементом с единым коммитом (2 исходных файла + 3 тестовых
файла, изменение локализовано). Дробление на «enum» и «строку промпта» искусственно: тесты
сгруппированы вокруг одного изменения, а согласованность имени/группы/промпта проверяется
совместно. Ручная проверка UI — **критерий приёмки задачи 01**, не отдельная задача кода.

## 0. Инварианты (из ADR-031 — обязательны, не обсуждаются)

1. **Только добавление** (ADR 010/022): ни один существующий код enum не меняется,
   `Undefined = 0` и `Other = 255` сохраняются, код 53 уникален.
2. **SQL-скрипт не требуется** (решение E1): `category_id` — целое число без FK/CHECK,
   `category_name` пересчитывается при каждой записи через `GetDisplayName`; проект
   `ReceiptCollector.Analytics.Migrations` не трогается; миграция данных не выполняется.
3. **Группа — данные, а не код**: `GetGroup` (словарь, `""` для отсутствующих) не меняется;
   фронт (`categoryOptions.tsx`) не изменяется — новый `<optgroup>` получается сменой значений
   `Group` на бэкенде.
4. **Контракты эндпоинтов не меняются**: `CategoryDto { Id, Key, Name, Group }` прежний;
   поведение страниц чеков и товаров аддитивное; пустые категории при сохранении — «не трогать».
5. **Существующие строки разграничений** `Furniture` и `HomeAppliances` в промте остаются
   (разграничивают с `HomeGoods` по другим признакам, конфликта нет).
6. **Edge cases (не реализуются как отдельные действия, но соблюдаются):** мыло/шампуни/зубная
   паста остаются в `CosmeticsAndHygiene`; уже категоризированные товары не переносятся;
   повторная AI-категоризация старых товаров `HomeGoods` может вернуть новую категорию — это
   допустимо.

## 1. Карта изменений по файлам

| Файл | Изменение |
|---|---|
| `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs` | enum-член `HouseholdChemicals = 53` после `FitnessAndSports = 52` с комментарием из ADR (раздел «Решение», п. 1); в `CommodityCategoryHelper`: запись `DisplayNames` после `FitnessAndSports`, перед `Other`; константа `HomeGroup = "Для дома"` (рядом с `OtherGroup`); 4 записи в `CategoryGroups` |
| `Analytics/src/ReceiptCollector.Analytics.Infrastructure/AI/OpenAiCompatibleAiClient.cs` | +1 строка разграничений в блок «Разграничения схожих категорий» метода `BuildPayload` |
| `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryTests.cs` | счётчик enum-членов 54 → 55; ассерты кода/имени/группы новых категорий; перенос `Furniture`/`HomeAppliances` из ассертов «без группы» в группу «Для дома» |
| `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityEndpointsTests.cs` | `HouseholdChemicals` в списке `GET /api/commodities/categories`; группа «Для дома» у переезжающих категорий |
| `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/OpenAiCompatibleAiClientTests.cs` | реестр промта содержит `HouseholdChemicals`; промт содержит текст разграничения |

Не затрагиваются: `Analytics/frontend/*`, `Analytics/src/ReceiptCollector.Analytics.Migrations/*`,
`backend/*`, `bot/*`, `docker/*`, `nginx`.

## 2. Порядок работ в задаче 01

### Шаг 1. Домен: enum + справочник (`CommodityCategory.cs`)

1. В enum после `FitnessAndSports = 52`, до `Other = 255`, отдельной строкой с комментарием:

   ```csharp
   // Бытовая химия — код 53 (см. ADR 031)
   HouseholdChemicals = 53, // Бытовая химия: чистящие средства, стиральные порошки,
                            // средства для мытья посуды и посудомоечных машин,
                            // освежители воздуха, губки/перчатки для уборки,
                            // бумажные полотенца и салфетки, мусорные пакеты,
                            // средства от насекомых
   ```

2. В `CommodityCategoryHelper`:
   - константа `private const string HomeGroup = "Для дома";` (по образцу `OtherGroup`);
   - `DisplayNames`: запись `{ CommodityCategory.HouseholdChemicals, "Бытовая химия" }`
     после `FitnessAndSports`, перед `Other` (порядок словаря = порядок отображения);
   - `CategoryGroups`: четыре записи — `HomeGoods`, `Furniture`, `HomeAppliances`,
     `HouseholdChemicals` (см. сниппет в ADR, раздел «Решение», п. 2, решение C1);
     записи остальных групп не трогаются; механика `GetGroup` не меняется.

**Проверка шага:** `cd Analytics && dotnet test` — без правок тестов на этом шаге упадут
`CommodityCategory_EnumCount_ShouldBe54` (54 → теперь 55) и
`NewRetailCategories_HaveExpectedCodes_And_NoGroup` — это ожидаемо, тесты обновляются
в шаге 3; важно, что падают **только** эти ассерты.

### Шаг 2. AI-клиент: строка разграничений (`OpenAiCompatibleAiClient.cs`)

1. В `BuildPayload`, блок «Разграничения схожих категорий» — добавить одну строку
   (компактно, как прецеденты ADR 022):

   ```
   - Бытовая химия (HouseholdChemicals): чистящие и моющие средства, стиральные
     порошки, средства для мытья посуды и посудомоечных машин, освежители воздуха,
     губки и перчатки для уборки, бумажные полотенца, мусорные пакеты, средства
     от насекомых — не HomeGoods (посуда, текстиль, хранение, интерьер) и
     не CosmeticsAndHygiene (личная гигиена: мыло, шампуни, зубная паста).
   ```

2. Реестр категорий (`CategoryNames` = `GetAll()` без `Undefined`) подхватывает новую
   категорию **автоматически** — код реестра не править; валидация ответа
   (`Enum.TryParse` + `Enum.IsDefined`) примет `HouseholdChemicals` сама.

### Шаг 3. Тесты

1. `CommodityCategoryTests.cs`:
   - `CommodityCategory_EnumCount_ShouldBe54` → переименовать в `..._ShouldBe55`
     и ожидать 55 (текущее поведение: 52 категории + `Undefined` + `Other` + новая = 55);
   - код/имя новой категории: `(int)CommodityCategory.HouseholdChemicals == 53`,
     `GetDisplayName(...) == "Бытовая химия"`;
   - группа: `GetGroup(HouseholdChemicals) == "Для дома"` в
     `GetGroup_ReturnsExpectedGroupsForNewCategories` (или рядом);
   - `NewRetailCategories_HaveExpectedCodes_And_NoGroup`: перенести ассерты
     `Furniture` и `HomeAppliances` из «без группы» в группу «Для дома»
     (ADR 031, раздел «Риски»); `CraftSupplies` остаётся `""` (плоский);
     rename теста (`..._And_NoGroup` больше не соответствует всем его ассертам) —
     на усмотрение разработчика, но имя должно правдиво описывать содержимое.
   - консистентность `GetAll_ContainsEveryEnumMemberWithDisplayName` и
     `Enum_Values_AreUnique` должны пройти **без правок** — это регрессионная сетка.
2. `CommodityEndpointsTests.cs` (список `GET /api/commodities/categories`):
   - `Assert.Contains(categories, c => c.Key == "HouseholdChemicals" && c.Id == 53
     && c.Name == "Бытовая химия" && c.Group == "Для дома")`;
   - обновить ассерты `Furniture` (49)/`HomeAppliances` (50) — теперь `Group == "Для дома"`.
3. `OpenAiCompatibleAiClientTests.cs` (район строк 263–300):
   - `Assert.Contains("HouseholdChemicals", requestBody)` (реестр);
   - `Assert.Contains("не HomeGoods", requestBody)` уже есть (строка 300) — оставить;
   - добавить ассерт на текст разграничения (например, `"HouseholdChemicals"` в блоке
     разграничений либо характерная подстрока новой строки промпта, например
     «средства от насекомых» — минимально хрупкая; точный текст — по реализации шага 2).

**Проверка шага (критерий выхода):**

```bash
cd Analytics && dotnet test
```

Все тесты Analytics зелёные. Это единственная автоматизированная проверка задачи;
`dotnet build` не падает (`CGO`-контексты Go-сервисов не при чём, их не собирать).

### Шаг 4. Ручная приёмка в UI (критерий приёмки, не отдельный артефакт кода)

Выполнить по `./up.dev.sh` (+ миграции, если окружение свежее) и оформить результат
в сводке `docs/reviews/household-chemicals-category/`:

1. Страница товаров и «Детали чека»: в выпадающих списках `<select>` есть категория
   «Бытовая химия» — внутри `<optgroup>` «Для дома» вместе с «Товары для дома»,
   «Бытовая техника», «Мебель» (порядок групп: плоские legacy → «Для дома» → «Продукты»
   → «Транспорт» → «Прочее», плоский `Other` — как в разделе D ADR).
2. Ручная категоризация товара как «Бытовая химия» сохраняется, после перезагрузки
   страницы отображается корректно (имя из `GetDisplayName`).
3. AI-подсказка на реальном товаре-пограничнике (например, «Мистер Мускул»,
   «Стиральный порошок») возвращает «Бытовая химия» (`HouseholdChemicals`);
   товар вида «Шампунь» — не категория гигиены не «уезжает» (остаётся
   `CosmeticsAndHygiene`) — edge case задачи.
4. Фоновый precategorization: `/precategorized` доступен, валидация
   `proposed_category_id != 0` не вызывает сбоев при коде 53 (наблюдение, без
   ожидания ночных сессий — достаточно, что механика не падает при попытке
   confirm/редактирования предложения).

## 3. Что в задачу НЕ входит

- Изменения фронтенда (`categoryOptions.tsx`, компоненты), стилей, `package.json`.
- SQL-скрипты, изменения `ReceiptCollector.Analytics.Migrations`, docker-compose,
  env-переменных, README, AGENTS.md (новых настроек нет).
- Go-backend и Bot (ADR 031, п. 6: упоминаний товарных категорий там нет).
- Перенос/миграция уже категоризированных данных.
- Few-shot-примеры в промт, типизированные группы, вынос групп в БД/конфиг (YAGNI, решение F1/B).

## 4. Критерии приёмки (сводные, из задачи и ADR-031)

- [ ] В enum категорий добавлена `HouseholdChemicals = 53` с отображаемым именем
      «Бытовая химия»; все прежние коды и `Other = 255` не изменены; имя enum уникально.
- [ ] В `CategoryGroups` есть константа `HomeGroup = "Для дома"` и 4 записи
      (`HomeGoods`, `HomeAppliances`, `Furniture`, `HouseholdChemicals`);
      группы остальных категорий без изменений.
- [ ] В AI-промпте (`BuildPayload`) есть одна строка разграничений для
      «Бытовая химия» с границами к `HomeGoods` и `CosmeticsAndHygiene`.
- [ ] `GET /api/commodities/categories` и `GET /api/receipts/categories` отдают новую
      категорию с `Group = "Для дома"`; контракты (`CategoryDto`) не меняются; поведение
      страниц аддитивно.
- [ ] Ручная категоризация «Бытовая химия» сохраняется и отображается корректно.
- [ ] Обновлены и проходят: `CommodityCategoryTests` (в т.ч. переезжающие категории
      из «без группы» в «Для дома»), `CommodityEndpointsTests`,
      `OpenAiCompatibleAiClientTests` (реестр + разграничение).
- [ ] `cd Analytics && dotnet test` — все тесты зелёные.
- [ ] Ручная проверка UI по чек-листу шага 4 (группа «Для дома», AI-подсказка
      на реальном товаре) описана в сводке изменений.

## 5. Риски и митигация

| Риск | Митигация |
|---|---|
| AI путает новую категорию с `HomeGoods`/`CosmeticsAndHygiene` | Строка разграничений (F1), строгая валидация ответа; при сохранении проблемы — отдельная задача на few-shot, вне объёма этой задачи |
| Пропуск записи в `DisplayNames`/`CategoryGroups` → fallback «Не указана»/`""` | Регрессионные тесты `GetAll_ContainsEveryEnumMemberWithDisplayName` (без правок), явный ассерт `GetGroup(HouseholdChemicals) == "Для дома"` |
| Все ещё падающие неснятые ассерты «без группы» в тестах | Полный grep по тестам `HomeAppliances`/`Furniture` перед сдачей (файлы-источники ассертов указаны в разделе «Риски» ADR) |
| Ломкий ассерт на текст промпта | Ассерт только на подстроки, а не на всю абзацную формулировку |
