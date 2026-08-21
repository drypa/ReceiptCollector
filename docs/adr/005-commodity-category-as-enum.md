# ADR 005: Справочник категорий товаров — Enum вместо таблицы БД

## Статус
Принято

## Контекст
В рамках задачи «Раздел «Товары» (Commodities)» требуется создать предопределённый справочник категорий товаров. Справочник содержит поля: `int Id`, `string Name`. 

В проекте уже есть аналогичный справочник для магазинов — `MerchantCategory`, реализованный как enum. Для товаров категория должна выбираться из предопределённого списка администратором.

Рассматривались варианты:
1. **Enum + статический словарь названий** (как `MerchantCategory`)
2. **Отдельная таблица `commodity_categories` в БД** (с миграцией, FK, CRUD)
3. **Таблица с предзаполненными данными** (seed migration)

## Решение
Выбран **вариант 1: Enum + статический словарь**.

Создаётся enum `CommodityCategory` в `Domain.Modules.Commodities` со статическим helper-классом `CommodityCategoryHelper`, предоставляющим отображение `enum → string name`.

### Состав категорий
```csharp
public enum CommodityCategory
{
    Undefined = 0,
    Food = 1,
    ClothingAndFootwear = 2,
    Electronics = 3,
    CosmeticsAndHygiene = 4,
    Pharmacy = 5,
    SportingGoods = 6,
    ChildrenGoods = 7,
    StationeryAndBooks = 8,
    PetSupplies = 9,
    HomeGoods = 10,
    ConstructionAndRepair = 11,
    AutomotiveGoods = 12,
    Flowers = 13,
    Other = 14
}
```

### API
`GET /api/commodities/categories` возвращает список `[{id, name}, ...]`.

### Хранение в БД
Столбцы `category_id` (int?) и `category_name` (varchar(128)) уже существуют в таблице `commodities` (начальная миграция). При назначении категории:
- `CategoryId` = (int)commodityCategory
- `CategoryName` = CommodityCategoryHelper.GetDisplayName(commodityCategory)

## Обоснование
1. **Согласованность**: Полностью аналогично `MerchantCategory` — уже принятому в проекте паттерну.
2. **Простота**: Не требует миграции схемы БД, seed-данных, нового DbSet и FK.
3. **Производительность**: Нет join'ов для получения имени категории — denormalized `CategoryName` хранится прямо в строке товара.
4. **Достаточность**: Список категорий стабилен, не требует администрирования из UI.

## Компромиссы
- **Минус**: Если в будущем потребуется добавлять/редактировать категории без пересборки приложения, потребуется миграция на таблицу.
- **Миграция**: В таком случае создаётся таблица `commodity_categories`, в неё переносятся значения enum, а FK добавляется к `commodities.category_id`. Это обратно совместимое изменение.

## Последствия
- Новых миграций БД не требуется.
- Новый Api-эндпоинт для получения списка категорий.
- Необходимо поддерживать enum в актуальном состоянии при добавлении категорий.
