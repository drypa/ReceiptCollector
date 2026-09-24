namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

public enum CommodityCategory
{
    Undefined = 0,
    Food = 1,
    Clothing = 2,
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
    Fuel = 14,
    Alcohol = 15,
    Tools = 16,
    Footwear = 17,

    // Продукты (детализация Food = 1) — коды 18–30
    Beverages = 18,
    Groceries = 19,
    Meat = 20,
    Poultry = 21,
    FishAndSeafood = 22,
    Dairy = 23,
    Eggs = 24,
    Vegetables = 25,
    Fruits = 26,
    Bakery = 27,
    Confectionery = 28,
    ReadyMeals = 29,
    FastFood = 30,

    // Транспорт — коды 31–37
    TollRoads = 31,
    PublicTransport = 32,
    RailwayTickets = 33,
    AirTickets = 34,
    Taxi = 35,
    Carsharing = 36,
    Parking = 37,

    // Прочие пробелы — коды 38–41
    Tobacco = 38,
    Telecommunication = 39,
    Utilities = 40,
    Entertainment = 41,

    // Продукты (детализация, расширение) — коды 42–46
    DriedFruits = 42,   // Сухофрукты: изюм, курага, чернослив, финики, сушёные ягоды
    Sauces = 43,        // Соусы: кетчуп, майонез, соевый соус, горчица, томатная паста
    Spices = 44,        // Приправы: перец, специи, приправы, лавровый лист, орегано
    Sausages = 45,      // Колбасные изделия: колбаса, сосиски, сардельки, ветчина
    Sweets = 46,        // Конфеты: конфеты, шоколад, мармелад, зефир, печенье

    Other = 255
}

public static class CommodityCategoryHelper
{
    private static readonly Dictionary<CommodityCategory, string> DisplayNames = new()
    {
        { CommodityCategory.Undefined, "Не указана" },
        { CommodityCategory.Food, "Прочая еда" },
        { CommodityCategory.Clothing, "Одежда" },
        { CommodityCategory.Electronics, "Электроника" },
        { CommodityCategory.CosmeticsAndHygiene, "Косметика и гигиена" },
        { CommodityCategory.Pharmacy, "Аптека" },
        { CommodityCategory.SportingGoods, "Товары для спорта" },
        { CommodityCategory.ChildrenGoods, "Товары для детей" },
        { CommodityCategory.StationeryAndBooks, "Канцтовары и книги" },
        { CommodityCategory.PetSupplies, "Зоотовары" },
        { CommodityCategory.HomeGoods, "Товары для дома" },
        { CommodityCategory.ConstructionAndRepair, "Строительство и ремонт" },
        { CommodityCategory.AutomotiveGoods, "Автотовары" },
        { CommodityCategory.Flowers, "Цветы" },
        { CommodityCategory.Fuel, "Топливо" },
        { CommodityCategory.Alcohol, "Алкоголь" },
        { CommodityCategory.Tools, "Инструменты" },
        { CommodityCategory.Footwear, "Обувь" },
        { CommodityCategory.Beverages, "Напитки" },
        { CommodityCategory.Groceries, "Бакалея" },
        { CommodityCategory.Meat, "Мясо" },
        { CommodityCategory.Poultry, "Птица" },
        { CommodityCategory.FishAndSeafood, "Рыба и морепродукты" },
        { CommodityCategory.Dairy, "Молочные продукты" },
        { CommodityCategory.Eggs, "Яйца" },
        { CommodityCategory.Vegetables, "Овощи" },
        { CommodityCategory.Fruits, "Фрукты" },
        { CommodityCategory.Bakery, "Хлеб и выпечка" },
        { CommodityCategory.Confectionery, "Кондитерские изделия" },
        { CommodityCategory.ReadyMeals, "Готовая еда и кулинария" },
        { CommodityCategory.FastFood, "Фастфуд" },
        { CommodityCategory.TollRoads, "Платные дороги" },
        { CommodityCategory.PublicTransport, "Общественный транспорт" },
        { CommodityCategory.RailwayTickets, "Ж/Д билеты" },
        { CommodityCategory.AirTickets, "Авиабилеты" },
        { CommodityCategory.Taxi, "Такси" },
        { CommodityCategory.Carsharing, "Каршеринг" },
        { CommodityCategory.Parking, "Парковка" },
        { CommodityCategory.Tobacco, "Табак" },
        { CommodityCategory.Telecommunication, "Связь и интернет" },
        { CommodityCategory.Utilities, "ЖКХ и коммунальные услуги" },
        { CommodityCategory.Entertainment, "Развлечения и досуг" },
        { CommodityCategory.DriedFruits, "Сухофрукты" },
        { CommodityCategory.Sauces, "Соусы" },
        { CommodityCategory.Spices, "Приправы" },
        { CommodityCategory.Sausages, "Колбасные изделия" },
        { CommodityCategory.Sweets, "Конфеты" },
        { CommodityCategory.Other, "Прочее" },
    };

    public static string GetDisplayName(CommodityCategory category)
        => DisplayNames.GetValueOrDefault(category, "Не указана");

    public static IReadOnlyCollection<(CommodityCategory Id, string Name)> GetAll()
        => DisplayNames.Select(kv => (kv.Key, kv.Value)).ToList();

    private const string ProductsGroup = "Продукты";
    private const string TransportGroup = "Транспорт";
    private const string OtherGroup = "Прочее";

    /// <summary>
    /// Явный маппинг «категория → группа» (ADR 022, решение B1).
    /// Замена диапазонного switch по кодам: группировка не зависит от числовых значений enum.
    /// </summary>
    private static readonly IReadOnlyDictionary<CommodityCategory, string> CategoryGroups = new Dictionary<CommodityCategory, string>
    {
        // Продукты (18 категорий)
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
        { CommodityCategory.DriedFruits, ProductsGroup },   // NEW 42
        { CommodityCategory.Sauces, ProductsGroup },        // NEW 43
        { CommodityCategory.Spices, ProductsGroup },        // NEW 44
        { CommodityCategory.Sausages, ProductsGroup },      // NEW 45
        { CommodityCategory.Sweets, ProductsGroup },         // NEW 46
        // Транспорт (7 категорий) 31–37
        { CommodityCategory.TollRoads, TransportGroup },
        { CommodityCategory.PublicTransport, TransportGroup },
        { CommodityCategory.RailwayTickets, TransportGroup },
        { CommodityCategory.AirTickets, TransportGroup },
        { CommodityCategory.Taxi, TransportGroup },
        { CommodityCategory.Carsharing, TransportGroup },
        { CommodityCategory.Parking, TransportGroup },
        // Прочее (4 категории) 38–41
        { CommodityCategory.Tobacco, OtherGroup },
        { CommodityCategory.Telecommunication, OtherGroup },
        { CommodityCategory.Utilities, OtherGroup },
        { CommodityCategory.Entertainment, OtherGroup },
    };

    /// <summary>
    /// Группа категории для группировки UI (&lt;optgroup&gt; в &lt;select&gt;).
    /// Источник истины группы — backend (решение D1 ADR 010), а не хардкод диапазонов на фронте.
    /// </summary>
    /// <remarks>
    /// Группа определяется словарём <see cref="CategoryGroups"/>, а не диапазонами кодов,
    /// поэтому новые категории с кодами вне исторических диапазонов корректно попадают в группу.
    /// Категории, отсутствующие в словаре (legacy 0–17 и Other = 255), возвращают пустую строку "" —
    /// они не относятся ни к одной группе и отображаются плоским списком.
    /// </remarks>
    public static string GetGroup(CommodityCategory category)
        => CategoryGroups.GetValueOrDefault(category, "");
}
