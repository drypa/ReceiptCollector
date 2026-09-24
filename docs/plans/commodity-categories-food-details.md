# План: Добавление новых категорий товаров группы «Продукты» (Сухофрукты, Соусы, Приправы, Колбасные изделия)

**Связанный ADR:** [ADR-022](../adr/022-commodity-categories-food-detailing.md)
**Файл задачи:** [commodity-categories-food-details.md](../tasks/commodity-categories-food-details.md)

## Область изменений

Только сервис Analytics (.NET). Затрагиваются:

- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs` — enum + `CommodityCategoryHelper`.
- `Analytics/src/ReceiptCollector.Analytics.Infrastructure/AI/OpenAiCompatibleAiClient.cs` — блок эвристик-разграничений в `BuildPayload`.
- Тесты: `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/` (4 файла, см. ниже).

**Без изменений:** `CommodityEndpoints.cs`, `CategoryDto.cs`, `CommodityCategorizationService.cs`, фронтенд (`CommodityTable.tsx`, `categoryOptions.tsx`, `types/commodity.ts`), PostgreSQL-схема, миграции, Go-backend, Telegram-бот, nginx. Внешних зависимостей не добавляется.

## Что меняется

### Изменение 1: `CommodityCategory.cs` — enum (4 новых значения)

Вставить между `Entertainment = 41` и `Other = 255`:

```csharp
// Продукты (детализация, расширение) — коды 42–45
DriedFruits = 42,   // Сухофрукты: изюм, курага, чернослив, финики, сушёные ягоды
Sauces = 43,        // Соусы: кетчуп, майонез, соевый соус, горчица, томатная паста
Spices = 44,        // Приправы: перец, специи, приправы, лавровый лист, орегано
Sausages = 45,      // Колбасные изделия: колбаса, сосиски, сардельки, ветчина
```

Проверки перед коммитом:

- коды 42–45 не заняты (заняты 0–17, 18–41, 255);
- имена членов уникальны (критично для формата `{"category": "<имя>"}`).
- `dotnet build` в `Analytics/`.

### Изменение 2: `CommodityCategory.cs` — `DisplayNames` (4 записи)

Добавить в словарь `DisplayNames` после записи `Entertainment` (перед `Other`):

```csharp
{ CommodityCategory.DriedFruits, "Сухофрукты" },
{ CommodityCategory.Sauces, "Соусы" },
{ CommodityCategory.Spices, "Приправы" },
{ CommodityCategory.Sausages, "Колбасные изделия" },
```

Порядок записей в словаре = порядок выдачи `GetAll()` (UI и реестр промта). Новые продуктовые категории окажутся в конце optgroup «Продукты» — это корректно, фронтенд группирует по полю `group`.

### Изменение 3: `CommodityCategory.cs` — `GetGroup()` на словарь (замена диапазонного switch)

1. Добавить константы имён групп:

```csharp
private const string ProductsGroup = "Продукты";
private const string TransportGroup = "Транспорт";
private const string OtherGroup = "Прочее";
```

2. Добавить словарь `CategoryGroups` (после `DisplayNames`):

```csharp
private static readonly IReadOnlyDictionary<CommodityCategory, string> CategoryGroups = new Dictionary<CommodityCategory, string>
{
    { CommodityCategory.Beverages, ProductsGroup },
    { CommodityCategory.Groceries, ProductsGroup },
    { CommodityCategory.Meat, ProductsGroup },
    { CommodityCategory.Poultry, ProductsGroup },
    { CommodityCategory.FishAndSeafood, ProductsGroup },
    { CommodityCategory.Dairy, ProductsGroup },
    { CommodityCategory.Eggs, ProductsGroup },
    { CommodityCategory.Vegetables, ProductsGroup },
    { CommodityCategory.Fruits, ProductsGroup },
    { CommodityCategory.Bakery, ProductsGroup },
    { CommodityCategory.Confectionery, ProductsGroup },
    { CommodityCategory.ReadyMeals, ProductsGroup },
    { CommodityCategory.FastFood, ProductsGroup },
    { CommodityCategory.DriedFruits, ProductsGroup },   // 42
    { CommodityCategory.Sauces, ProductsGroup },        // 43
    { CommodityCategory.Spices, ProductsGroup },        // 44
    { CommodityCategory.Sausages, ProductsGroup },      // 45
    { CommodityCategory.TollRoads, TransportGroup },
    { CommodityCategory.PublicTransport, TransportGroup },
    { CommodityCategory.RailwayTickets, TransportGroup },
    { CommodityCategory.AirTickets, TransportGroup },
    { CommodityCategory.Taxi, TransportGroup },
    { CommodityCategory.Carsharing, TransportGroup },
    { CommodityCategory.Parking, TransportGroup },
    { CommodityCategory.Tobacco, OtherGroup },
    { CommodityCategory.Telecommunication, OtherGroup },
    { CommodityCategory.Utilities, OtherGroup },
    { CommodityCategory.Entertainment, OtherGroup },
};
```

3. Заменить тело `GetGroup`:

```csharp
public static string GetGroup(CommodityCategory category)
    => CategoryGroups.GetValueOrDefault(category, "");
```

4. Обновить XML-doc комментарий метода (убрать упоминание диапазонов 18–30/31–37/38–41; описать словарь и поведение legacy `""`).

Поведение legacy-категорий (0–17) и `Other = 255` сохраняется: их нет в словаре → `""`.

### Изменение 4: `OpenAiCompatibleAiClient.cs` — эвристики в промте

В `BuildPayload`, в user-сообщение (после строки про выбор категории из реестра), добавить блок:

```csharp
"Выбери ОДНУ наиболее подходящую категорию из реестра: {catalog}.\n" +
"Ответь строго в формате JSON: {\"category\": \"НазваниеКатегории\"} без пояснений.\n" +
"Разграничения схожих категорий:\n" +
"- Сухофрукты (DriedFruits): изюм, курага, чернослив, финики — не Fruits (свежие фрукты/ягоды).\n" +
"- Соусы (Sauces) и Приправы (Spices): кетчуп, майонез, соевый соус, специи — не Groceries " +
"(бакалея — крупы, мука, макароны, сахар, соль, масло, чай/кофе, консервы).\n" +
"- Колбасные изделия (Sausages): колбаса, сосиски, сардельки, ветчина — не Meat " +
"(свежее/замороженное мясо)."
```

Существующий тест `SuggestCategoryAsync_posts_to_chat_completions_endpoint_with_model` (assert `"НазваниеКатегории"` в теле) не ломается — маркер сохраняется.

## Тесты

### `CommodityCategoryTests.cs`

1. `CommodityCategory_EnumCount_ShouldBe43` → переименовать в `CommodityCategory_EnumCount_ShouldBe47`, `43` → `47`. Обновить XML-комментарий класса (ADR 010 → 47 значений).
2. `GetAll_ContainsEveryEnumMemberWithDisplayName` — assert динамический, менять не нужно; при желании обновить комментарий.
3. `NewCategories_HaveExpectedDisplayNames` — добавить 4 assert'а:
   - `GetDisplayName(DriedFruits) == "Сухофрукты"`, `GetDisplayName(Sauces) == "Соусы"`, `GetDisplayName(Spices) == "Приправы"`, `GetDisplayName(Sausages) == "Колбасные изделия"`.
4. `GetGroup_ReturnsExpectedGroupsForNewCategories` — добавить в блок «Продукты»:
   - `GetGroup(DriedFruits) == "Продукты"`, `GetGroup(Sauces) == "Продукты"`, `GetGroup(Spices) == "Продукты"`, `GetGroup(Sausages) == "Продукты"`. Обновить комментарии (диапазоны → словарь).
5. `GetGroup_ReturnsEmptyStringForLegacyCategories` — без изменений (поведение legacy сохранено).
6. **Новые тесты:**
   - `NewFoodDetailCategories_HaveExpectedCodes` — `(int)DriedFruits == 42`, `(int)Sauces == 43`, `(int)Spices == 44`, `(int)Sausages == 45`; `(int)Other == 255` (защита от переименования).
   - `Enum_Values_AreUnique` — `Enum.GetValues<CommodityCategory>().Distinct().Count() == Enum.GetValues<CommodityCategory>().Length` (защита от коллизий).
   - `ExistingCategoryCodes_ArePreserved` — спот-проверка неизменности ключевых кодов: `Food=1`, `Beverages=18`, `FastFood=30`, `TollRoads=31`, `Parking=37`, `Tobacco=38`, `Entertainment=41`.

### `CommodityEndpointsTests.cs`

7. `ListCategories_returns_key_equal_to_enum_name` — добавить:
   - `Assert.Contains(categories, c => c.Key == "DriedFruits" && c.Id == 42 && c.Group == "Продукты");`
   - `Assert.Contains(categories, c => c.Key == "Sausages" && c.Group == "Продукты");`

### `CommodityCategorizationServiceTests.cs`

8. `Suggest_passes_catalog_of_categories_without_undefined_to_ai` — добавить проверку, что реестр содержит новые имена:
   - `names.Contains("DriedFruits") && names.Contains("Sauces") && names.Contains("Spices") && names.Contains("Sausages")`.
   (Счётчик в assert уже динамический — обновлять не требуется.)

### `OpenAiCompatibleAiClientTests.cs`

9. **Новый тест** `SuggestCategoryAsync_prompt_contains_new_category_heuristics` — по образцу `..._posts_to_chat_completions_endpoint_with_model`: перехватить тело запроса, assert `requestBody` содержит `DriedFruits`, `Sauces`, `Sausages` (производные от реестра `CategoryCatalog` НЕ обязаны там быть — эвристики статичны; достаточно проверить наличие строк `"Сухофрукты"` и `"не Meat"`).

## Проверка (критерии приёмки задачи)

- [ ] `cd Analytics && dotnet test` — все тесты зелёные.
- [ ] `cd Analytics && dotnet build` — без предупреждений об ошибках.
- Функционально (ручная проверка после деплоя dev-среды): `GET /api/commodities/categories` возвращает 47 категорий; новые 4 — в группе «Продукты»; `PUT /api/commodities/{id}/category` с `42..45` сохраняет категорию.

## Что НЕ делаем

- Не переклассифицируем существующие товары (без data-fix, без SQL-миграций).
- Не меняем контракты API (`CategoryDto`, `UpdateCategoryRequest`) и фронтенд.
- Не правим legacy-категории (0–17, `Other=255`) — их группа остаётся `""`.

## Порядок работ

1. Изменение 1–3 (`CommodityCategory.cs`) + тесты 1–6 → `dotnet test`.
2. Изменение 4 (`OpenAiCompatibleAiClient.cs`) + тесты 7–9 → `dotnet test`.
3. Финальная проверка `dotnet build` и `dotnet test` в `Analytics/`.