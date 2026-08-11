# План: Расширение справочника категорий товаров (Commodity Categories Expansion)

## Описание задачи

Реализовать расширение справочника `CommodityCategory` по [задаче commodity-categories-expansion](../tasks/commodity-categories-expansion.md) в соответствии с архитектурным решением [ADR 010](../adr/010-commodity-categories-expansion.md).

Изменение затрагивает **только Analytics (.NET + frontend)**. Схема PostgreSQL, Go-backend, Telegram-бот и nginx **не меняются**. Существующие значения enum (коды 0–17 и 255) не перекодируются — только добавление 24 новых значений (коды 18–41) и смена отображаемого имени `Food = 1` с «Продукты питания» на **«Прочая еда»** (только в `DisplayNames` хелпера).

Итоговый состав: **43 значения enum** (19 существующих + 24 новых), в промт ИИ (ADR 009) попадают **42 категории** (без `Undefined = 0`).

Ключевые ограничения, которые нельзя нарушать:

- `CommodityCategoryHelper.GetAll()` используется и `ListCategories`, и генерацией промта ИИ (ADR 009) — **сигнатуру `GetAll()` не менять**, новые возможности добавлять аддитивно.
- `Other = 255` и `Food = 1` сохраняются (обратная совместимость данных в `commodities.category_id`).
- Максимальная длина отображаемого имени «ЖКХ и коммунальные услуги» (23 символа) < `varchar(128)` — схема БД не меняется.
- Перед деплоем проверить допущение заказчика о данных (нет строк `category_id = 1` / `category_name = 'Продукты питания'`) — решение C1 ADR 010.

---

## Шаг 1. Расширение enum `CommodityCategory` (P0, ~0.5 дня)

**Файлы:**
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs`

**Действия:**

1.1. В enum `CommodityCategory` добавить 24 новых члена **между `Footwear = 17` и `Other = 255`** (чтобы `Other = 255` остался последним). Точные имена и коды — строго из таблицы «Новые значения» ADR 010:

| Код | Enum | Отображаемое имя | Раздел UI |
|-----|------|------------------|-----------|
| 18 | `Beverages` | Напитки | Продукты |
| 19 | `Groceries` | Бакалея | Продукты |
| 20 | `Meat` | Мясо | Продукты |
| 21 | `Poultry` | Птица | Продукты |
| 22 | `FishAndSeafood` | Рыба и морепродукты | Продукты |
| 23 | `Dairy` | Молочные продукты | Продукты |
| 24 | `Eggs` | Яйца | Продукты |
| 25 | `Vegetables` | Овощи | Продукты |
| 26 | `Fruits` | Фрукты | Продукты |
| 27 | `Bakery` | Хлеб и выпечка | Продукты |
| 28 | `Confectionery` | Кондитерские изделия | Продукты |
| 29 | `ReadyMeals` | Готовая еда и кулинария | Продукты |
| 30 | `FastFood` | Фастфуд | Продукты |
| 31 | `TollRoads` | Платные дороги | Транспорт |
| 32 | `PublicTransport` | Общественный транспорт | Транспорт |
| 33 | `RailwayTickets` | Ж/Д билеты | Транспорт |
| 34 | `AirTickets` | Авиабилеты | Транспорт |
| 35 | `Taxi` | Такси | Транспорт |
| 36 | `Carsharing` | Каршеринг | Транспорт |
| 37 | `Parking` | Парковка | Транспорт |
| 38 | `Tobacco` | Табак | Прочее |
| 39 | `Telecommunication` | Связь и интернет | Прочее |
| 40 | `Utilities` | ЖКХ и коммунальные услуги | Прочее |
| 41 | `Entertainment` | Развлечения и досуг | Прочее |

1.2. В словаре `DisplayNames` хелпера `CommodityCategoryHelper`:
- заменить значение `{ CommodityCategory.Food, "Продукты питания" }` на `{ CommodityCategory.Food, "Прочая еда" }` (только отображаемое имя; `Food = 1` и enum-член не меняются);
- добавить 24 записи для новых членов с отображаемыми именами из таблицы выше (столбец «Отображаемое имя»).

1.3. `GetDisplayName()` и `GetAll()` **не трогать** — они автоматически подхватят новый состав.

**Критерий готовности:**
- `Enum.GetValues<CommodityCategory>().Length == 43`; коды 0–17 и 255 не изменены.
- `GetDisplayName(CommodityCategory.Food) == "Прочая еда"`.
- В `DisplayNames` присутствуют все 24 новые записи (проверяется тестом из шага 4).
- Существующие значения enum-членов (`Undefined, Food, Clothing, ... Other`) не переименованы.

---

## Шаг 2. Поле `Group` для группировки UI (решение D1 ADR 010) (P1, ~1 день, не блокирует)

> Группировка `<optgroup>` — желательная часть (задача п. 5, ADR 010 D1 «рекомендовано»). Может выполняться параллельно с шагом 1. Если по времени не входит — допустимо отложить без ущерба для остальных шагов.

**Файлы:**
- `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CategoryDto.cs`
- `Analytics/src/ReceiptCollector.Analytics.Domain/Modules/Commodities/CommodityCategory.cs` (хелпер)
- `Analytics/src/ReceiptCollector.Analytics.Api/Modules/Commodities/CommodityEndpoints.cs`
- `Analytics/frontend/src/types/commodity.ts`
- `Analytics/frontend/src/components/CommodityTable.tsx`

**Действия:**

2.1. **Backend: источник истины группы — хелпер.** В `CommodityCategoryHelper` добавить аддитивный метод (сигнатуру `GetAll()` не менять):

```csharp
public static string GetGroup(CommodityCategory category) => category switch
{
    >= CommodityCategory.Beverages and <= CommodityCategory.FastFood => "Продукты",        // 18–30
    >= CommodityCategory.TollRoads and <= CommodityCategory.Parking   => "Транспорт",        // 31–37
    >= CommodityCategory.Tobacco and <= CommodityCategory.Entertainment => "Прочее",        // 38–41
    _ => "",                                                                                 // 0–17 и 255
};
```

**Задокументированное решение:** старые категории (0–17 и `Other = 255`) получают **пустую строку `""`** (без группы — плоский список в `<select>`). Они **не** относятся к группе «Прочее»: группа «Прочее» содержит только новые категории 38–41 (Табак, Связь и интернет, ЖКХ, Развлечения). Основание — требование задачи: «`Food`-подобные старые категории к группе „Прочее“ НЕ относятся».

2.2. **DTO.** В `Analytics/src/ReceiptCollector.Analytics.Application/Modules/Commodities/Models/CategoryDto.cs` добавить поле:

```csharp
public sealed record CategoryDto(int Id, string Name, string Group);
```

2.3. **Эндпоинт.** В `CommodityEndpoints.ListCategories` добавить группу в маппинг:

```csharp
var categories = CommodityCategoryHelper.GetAll()
    .Select(c => new CategoryDto((int)c.Id, c.Name, CommodityCategoryHelper.GetGroup(c.Id)))
    .ToList();
```

Контракт `PUT /api/commodities/{id}/category` и `CommodityRepository.UpdateCategoryAsync` **не меняются** (новые категории принимаются автоматически через `Enum.IsDefined` и записываются через `GetDisplayName`).

2.4. **Frontend тип.** В `Analytics/frontend/src/types/commodity.ts` поле `group` сделать **опциональным** (`group?: string`) — тип `Category` переиспользуется `MerchantTable.tsx` для merchant-категорий, где поля `group` в ответе нет (совместимость):

```ts
export interface Category {
  id: number;
  name: string;
  group?: string;
}
```

2.5. **Frontend группировка.** В `Analytics/frontend/src/components/CommodityTable.tsx` в `<select>` заменить плоский `categories.map(...)` на группировку по полю `group`:
- опции с `group === ''` (или `undefined`) — **прямые дети `<select>`** (не оборачивать в `<optgroup>`: у `<optgroup>` обязателен непустой `label`);
- остальные — внутри `<optgroup label={group}>`, порядок групп — по порядку первого появления в массиве `categories`.

Проверить, что в `MerchantTable.tsx` ничего не сломается (там `group` не используется — правки не требуются).

**Критерий готовности:**
- `GET /api/commodities/categories` возвращает `[{id, name, group}]`; у 0–17 и 255 — `group: ""`, у 18–30 — `"Продукты"`, у 31–37 — `"Транспорт"`, у 38–41 — `"Прочее"`.
- В UI селекта категорий товаров отображаются `<optgroup>Продукты / Транспорт / Прочее</optgroup>`; старые категории — плоским списком; опция «—» сохранена.
- Список категорий магазинов (`MerchantTable`) не изменил поведение.

---

## Шаг 3. Проверка допущения о данных и опциональный data-fix (решение C1 ADR 010) (P0, ~0.5 дня)

> Задача п. 4/6.1 и ADR 010 решение C1: перед деплоем убедиться, что в `commodities` нет строк с `category_id = 1` или старым `category_name = 'Продукты питания'`. Если есть — выполнить одноразовый идемпотентный data-fix через миграционный раннер.

**Файлы:**
- (проверка) окружение PostgreSQL — connection string из `Analytics/src/ReceiptCollector.Analytics.Migrations/appsettings.json` (секция `Infrastructure:Postgres`), либо env `RECEIPTCOLLECTOR__Infrastructure__Postgres__ConnectionString`
- (data-fix, только при COUNT > 0) новый файл `Analytics/src/ReceiptCollector.Analytics.Migrations/Scripts/<timestamp>_fix_commodity_category_food_name.sql`

**Действия:**

3.1. **Проверочный SQL** (источник: решение C1 ADR 010):

```sql
SELECT COUNT(*) FROM commodities WHERE category_id = 1 OR category_name = 'Продукты питания';
```

Как выполнить (любой из вариантов; connection string из `Migrations/appsettings.json`: `Host=localhost;Port=5432;Database=receipt_collector`):
- dev-окружение (`docker-compose.develop.yml`, порт 5432 наружу, `PG_LOGIN=admin`, `PG_SECRET=secret` из `.env`):
  ```bash
  PGPASSWORD=secret psql -h localhost -p 5432 -U admin -d receipt_collector -c "SELECT COUNT(*) FROM commodities WHERE category_id = 1 OR category_name = 'Продукты питания';"
  ```
- prod-контейнер:
  ```bash
  docker exec receipt-postgres psql -U admin -d receipt_collector -c "SELECT COUNT(*) FROM commodities WHERE category_id = 1 OR category_name = 'Продукты питания';"
  ```

3.2. **Ветвление по результату:**

- **COUNT = 0** → допущение заказчика подтверждено. Data-fix **не создавать**. Зафиксировать результат в описании PR и в комментарии к задаче (критерий приёмки 5 задачи).
- **COUNT > 0** → создать data-fix скрипт в `Scripts/` (именование по образцу существующих: `<yyyyMMddHHmmss>_fix_commodity_category_food_name.sql`, timestamp — текущая дата/время, файл должен сортироваться последним):

```sql
BEGIN;

-- Идемпотентный data-fix: переименование отображаемого имени Food в denormalized category_name.
-- category_id не трогаем (код 1 сохраняется). Повторный запуск не меняет данные (WHERE-условие).
UPDATE commodities
SET category_name = 'Прочая еда'
WHERE category_id = 1 AND category_name = 'Продукты питания';

COMMIT;
```

Миграционный раннер (`MigrationRunner.cs`) применяет все `*.sql` из `Scripts/` по порядку имени **однократно** (история в `migration_scripts_history`) — скрипт применится один раз при следующем запуске миграций. Применить:

```bash
cd Analytics/src/ReceiptCollector.Analytics.Migrations && dotnet run
```

> Примечание: в Docker-образе Analytics (`Analytics/Dockerfile`) миграции **не запускаются автоматически** — они выполняются вручную командой выше (см. AGENTS.md). Data-fix должен попасть в `Scripts/` до деплоя.

**Критерий готовности:**
- Проверочный SQL выполнен, результат зафиксирован (0 строк — запись в PR; >0 — скрипт создан и применён).
- После применения: `SELECT COUNT(*) FROM commodities WHERE category_name = 'Продукты питания';` возвращает 0; `category_id` нигде не изменён (код 1 сохранён).
- Скрипт (если создан) идемпотентен — повторный `dotnet run` в Migrations не даёт ошибок и не меняет данные.

---

## Шаг 4. Тесты на состав справочника (P1, ~0.5 дня)

**Файлы:**
- новый файл `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/CommodityCategoryTests.cs` (namespace `ReceiptCollector.Analytics.Api.Tests`, xunit, по образцу `MerchantEndpointsTests`)

**Действия:**

4.1. Создать `CommodityCategoryTests.cs`. Образец — `GetMerchantCategories_ReturnsAllCategories` из `Analytics/tests/ReceiptCollector.Analytics.Api.Tests/MerchantEndpointsTests.cs`, но т.к. `CommodityEndpoints.ListCategories` — `private static` (в отличие от `MerchantEndpoints.ListCategories`), ассерты делать на источнике данных `CommodityCategoryHelper.GetAll()` (именно его отдаёт эндпоинт):

```csharp
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CommodityCategoryTests
{
    [Fact]
    public void CommodityCategory_EnumCount_ShouldBe43()
    {
        Assert.Equal(43, Enum.GetValues<CommodityCategory>().Length);
    }

    [Fact]
    public void GetAll_ContainsEveryEnumMemberWithDisplayName()
    {
        // Защита от регрессии: пропуск записи в DisplayNames -> GetAll().Count < 43
        var all = CommodityCategoryHelper.GetAll();
        Assert.Equal(Enum.GetValues<CommodityCategory>().Length, all.Count);
        Assert.All(Enum.GetValues<CommodityCategory>(), category =>
            Assert.False(string.IsNullOrWhiteSpace(CommodityCategoryHelper.GetDisplayName(category)),
                $"{category} не имеет отображаемого имени"));
    }

    [Fact]
    public void Food_And_Other_ArePreserved()
    {
        Assert.Equal(1, (int)CommodityCategory.Food);
        Assert.Equal(255, (int)CommodityCategory.Other);
        Assert.Equal("Прочая еда", CommodityCategoryHelper.GetDisplayName(CommodityCategory.Food));
        Assert.Equal("Не указана", CommodityCategoryHelper.GetDisplayName(CommodityCategory.Undefined));
    }

    [Fact]
    public void NewCategories_HaveExpectedDisplayNames()
    {
        Assert.Equal("Напитки", CommodityCategoryHelper.GetDisplayName(CommodityCategory.Beverages));
        Assert.Equal("ЖКХ и коммунальные услуги", CommodityCategoryHelper.GetDisplayName(CommodityCategory.Utilities));
        Assert.Equal("Платные дороги", CommodityCategoryHelper.GetDisplayName(CommodityCategory.TollRoads));
    }
}
```

4.2. (если шаг 2 выполнен) добавить тест группы: `GetGroup(Beverages) == "Продукты"`, `GetGroup(TollRoads) == "Транспорт"`, `GetGroup(Entertainment) == "Прочее"`, `GetGroup(Food) == ""`, `GetGroup(Other) == ""`.

4.3. Запустить все тесты и убедиться, что существующие не сломались:

```bash
cd Analytics && dotnet test
```

Ожидание по ADR 010 (проверено grep): ни один существующий тест не зависит от количества/состава `CommodityCategory` (`MerchantEndpointsTests` проверяет `MerchantCategory`, 21 значение — не затрагивается).

**Критерий готовности:**
- `dotnet test` зелёный: новые тесты проходят, существующие не сломаны.
- Тест фиксирует: 43 значения, `Food = 1`/`Other = 255` сохранены, `Food` отображается как «Прочая еда», все члены имеют отображаемое имя (`GetAll().Count == 43` — ловит пропуск записи в `DisplayNames`).

---

## Шаг 5. Перевалидация ADR 009 и плана auto-commodity-categorization.md (P1, ~0.5 дня, документация)

**Файлы:**
- `docs/adr/009-auto-commodity-categorization.md`
- `docs/plans/auto-commodity-categorization.md`

**Действия:**

5.1. **`docs/adr/009-auto-commodity-categorization.md`** — обновить упоминания состава категорий с «19 значений + `Other = 255`» на актуальный состав «43 значения (0–17, 18–41, `Other = 255`)», в промт без `Undefined` попадает **42 категории**:
- п. «Контекст», список справочника (строка с перечислением enum-членов);
- вариант B1 «Справочник категорий для ИИ» (описание перечисления и обоснование);
- раздел «Решение» (сводка выбранных вариантов: B1);
- раздел «Ответы на открытые вопросы», п. 1 (справочник категорий);
- раздел «Компромиссы» (упоминание 19 категорий);
- пример промта в п. 4 «ИИ-клиент и конфигурация» — заменить хардкод-список на актуальный или явно пометить, что список генерируется из `GetAll()` без `Undefined`; обновить пример ответа JSON (`"categoryName": "Продукты питания"` → `"Прочая еда"`).

5.2. В ADR 009 (п. 4 «Промт») зафиксировать **требование few-shot примеров**: несколько пар «название товара → категория» в промте для устойчивости качества при 42 категориях.

5.3. **`docs/plans/auto-commodity-categorization.md`**:
- обновить упоминания состава («19 значений + `Other = 255`, без `Undefined`» → «43 значения, без `Undefined` — 42 категории в промте»);
- в задачу 4 «ИИ-клиент и конфигурация» добавить пункт: промт должен содержать few-shot примеры и эвристики:
  - кофе/чай: «Кофе в зёрнах / молотый / растворимый / капсулы», «Чай в пакетиках / листовой» → `Бакалея`; «Капучино», «Латте», «Американо», «Кофе с собой» → `Напитки`;
  - разграничение схожих категорий: `Meat` vs `Poultry`; `Vegetables` vs `Fruits`; `Bakery` vs `Confectionery`; `ReadyMeals` vs `FastFood`; `Groceries` vs `Food` (запасная);
- в задачу 7 «Юнит-тесты» при необходимости добавить проверку консистентности справочника (см. шаг 4) — либо сослаться на `CommodityCategoryTests`.

5.4. Отметить в обоих документах: **каркас решения ADR 009 не меняется** (JSON `{"category": "Name"}`, валидация `Enum.TryParse` по имени + `Enum.IsDefined`, генерация промта из `GetAll()` без `Undefined`, приоритеты «existing → cache → ai»).

**Критерий готовности:**
- В ADR 009 и плане нет упоминаний старого состава «19 значений»/«Продукты питания» (кроме исторических отсылок при необходимости с пометкой).
- Требование few-shot и эвристик зафиксировано в ADR 009 (п. 4) и в плане (задача 4).
- Явно указано, что каркас ADR 009 не меняется.

---

## Шаг 6. Проверка/сборка (P0, ~0.5 дня)

**Файлы:**
- (при необходимости) `docs/adr/010-commodity-categories-expansion.md` — раздел «Ссылки»

**Действия:**

6.1. Сборка и тесты:

```bash
cd Analytics && dotnet build && dotnet test
```

6.2. Frontend: убедиться, что TS-типы и компоненты собираются (`npm run build` в `Analytics/frontend`).

6.3. Ручная проверка `GET /api/commodities/categories` (эндпоинт без авторизации — не требует cookie; аналитик запущен локально `cd Analytics/src/ReceiptCollector.Analytics.Api && dotnet run`, либо через dev-nginx):

```bash
curl -s http://localhost:5039/api/commodities/categories | jq 'length'        # ожидание: 43
curl -s http://localhost:5039/api/commodities/categories | jq '.[0:5]'       # Undefined, Прочая еда, Одежда, ...
curl -s http://localhost:5039/api/commodities/categories | jq '.[18].name'   # ожидание: "Напитки" (id=18)
```

Проверить: 43 элемента; у `id = 1` имя «Прочая еда»; новые категории 18–41 на месте; при выполнении шага 2 — поле `group` у категорий.

6.4. Ручная проверка приёма новых категорий через `PUT /api/commodities/{id}/category` (админ): `categoryId = 18` → `200` с `"categoryName": "Напитки"` (валидация `Enum.IsDefined` пропускает новые коды).

6.5. Если в ADR 010 раздел «Ссылки» не содержит ссылку на этот план — добавить: `[План: Расширение справочника категорий товаров](../plans/commodity-categories-expansion.md)`.

**Критерий готовности:**
- `dotnet build` и `dotnet test` проходят; frontend собирается.
- `GET /api/commodities/categories` возвращает 43 категории с корректными русскими именами; коды существующих категорий не изменились.
- `PUT /api/commodities/{id}/category` принимает новые категории.
- Результат проверки допущения о данных (шаг 3) зафиксирован; data-fix применён или не требовался.

---

## План выполнения (порядок работ)

1. **Шаг 1** (enum + DisplayNames) — стартовая точка, от неё зависят шаги 2 и 4.
2. **Шаг 3** (проверка данных + опциональный data-fix) — можно параллельно с шагом 1 (не зависит от кода); data-fix скрипт должен быть в `Scripts/` до деплоя.
3. **Шаг 2** (Group/UI) — параллельно с шагами 1/4; опционально.
4. **Шаг 4** (тесты) — сразу после шага 1.
5. **Шаг 5** (перевалидация ADR 009/плана) — независим, можно в любом месте, но до сдачи PR.
6. **Шаг 6** (сборка + ручная проверка) — финальный.

## Критический путь

Шаг 1 → Шаг 4 → Шаг 6 (код → тесты → сборка/приёмка). Шаг 3 — обязательное условие деплоя (может идти параллельно). Шаги 2 и 5 — не блокируют.

## Критерии приёмки задачи (из `docs/tasks/commodity-categories-expansion.md`, раздел 7)

1. `CommodityCategory` содержит все значения из п. 3 задачи; `Other = 255` сохранён; `Food = 1` отображается как «Прочая еда».
2. `GET /api/commodities/categories` возвращает полный список с корректными русскими именами; существующие коды не изменились.
3. Ручная категоризация (`PUT /api/commodities/{id}/category`) принимает новые категории; фронтенд отображает их в выпадающем списке (при группировке — по разделам).
4. Проект Analytics собирается, тесты проходят; добавлены тесты на состав справочника.
5. Допущение о нераспределённых данных проверено, результат зафиксирован (шаг 3).

## Открытые вопросы

- **Нет** — все решения приняты в ADR 010 (нумерация A1, переименование B1, данные C1, группировка D1, промт E1). Единственная развилка — результат проверки данных на шаге 3 (COUNT = 0 или > 0), ветвление описано.
