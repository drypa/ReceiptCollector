namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public static class MerchantCategoryHelper
{
    private static readonly Dictionary<MerchantCategory, string> DisplayNames = new()
    {
        { MerchantCategory.Undefined, "Не указана" },
        { MerchantCategory.GroceryStores, "Продуктовые магазины" },
        { MerchantCategory.ClothingAndFootwear, "Одежда и обувь" },
        { MerchantCategory.Electronics, "Электроника" },
        { MerchantCategory.Cosmetics, "Косметика" },
        { MerchantCategory.Pharmacies, "Аптеки" },
        { MerchantCategory.SportingGoods, "Спортивные товары" },
        { MerchantCategory.ChildrenGoods, "Детские товары" },
        { MerchantCategory.StationeryAndBooks, "Канцтовары и книги" },
        { MerchantCategory.PetStores, "Зоомагазины" },
        { MerchantCategory.HomeGoods, "Товары для дома" },
        { MerchantCategory.HouseholdGoods, "Хозяйственные товары" },
        { MerchantCategory.ConstructionAndRepairMaterials, "Строительство и ремонт" },
        { MerchantCategory.AutomotiveGoods, "Автотовары" },
        { MerchantCategory.Jewelry, "Ювелирные изделия" },
        { MerchantCategory.Flowers, "Цветы" },
        { MerchantCategory.Hobbies, "Хобби" },
        { MerchantCategory.GardenSupplies, "Садовые товары" },
        { MerchantCategory.MusicalInstruments, "Музыкальные инструменты" },
        { MerchantCategory.KitchenAccessories, "Кухонные принадлежности" },
        { MerchantCategory.HouseholdService, "Бытовые услуги" },
    };

    public static string GetDisplayName(MerchantCategory category)
        => DisplayNames.GetValueOrDefault(category, "Не указана");

    public static IReadOnlyCollection<(MerchantCategory Id, string Name)> GetAll()
        => DisplayNames.Select(kv => (kv.Key, kv.Value)).ToList();
}
