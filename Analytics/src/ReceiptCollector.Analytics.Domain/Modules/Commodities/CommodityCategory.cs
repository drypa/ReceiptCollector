namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

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

public static class CommodityCategoryHelper
{
    private static readonly Dictionary<CommodityCategory, string> DisplayNames = new()
    {
        { CommodityCategory.Undefined, "Не указана" },
        { CommodityCategory.Food, "Продукты питания" },
        { CommodityCategory.ClothingAndFootwear, "Одежда и обувь" },
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
        { CommodityCategory.Other, "Прочее" },
    };

    public static string GetDisplayName(CommodityCategory category)
        => DisplayNames.GetValueOrDefault(category, "Не указана");

    public static IReadOnlyCollection<(CommodityCategory Id, string Name)> GetAll()
        => DisplayNames.Select(kv => (kv.Key, kv.Value)).ToList();
}
