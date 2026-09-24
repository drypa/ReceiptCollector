using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты консистентности справочника <see cref="CommodityCategory"/> после расширений
/// (ADR 010: 43 значения, Food переименован в «Прочая еда», группировка UI по полю Group;
/// ADR 022: 47 значений — добавлены Сухофрукты, Соусы, Приправы, Колбасные изделия,
/// GetGroup переведён на словарь «категория → группа»).
/// Assert делаются на источнике данных <see cref="CommodityCategoryHelper.GetAll()"/> —
/// именно его отдаёт эндпоинт GET /api/commodities/categories.
/// </summary>
public class CommodityCategoryTests
{
    [Fact]
    public void CommodityCategory_EnumCount_ShouldBe47()
    {
        Assert.Equal(47, Enum.GetValues<CommodityCategory>().Length);
    }

    [Fact]
    public void GetAll_ContainsEveryEnumMemberWithDisplayName()
    {
        // Защита от регрессии: пропуск записи в DisplayNames -> GetAll().Count < 47
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
        // Новые продуктовые категории (ADR 022): Сухофрукты, Соусы, Приправы, Колбасные изделия
        Assert.Equal("Сухофрукты", CommodityCategoryHelper.GetDisplayName(CommodityCategory.DriedFruits));
        Assert.Equal("Соусы", CommodityCategoryHelper.GetDisplayName(CommodityCategory.Sauces));
        Assert.Equal("Приправы", CommodityCategoryHelper.GetDisplayName(CommodityCategory.Spices));
        Assert.Equal("Колбасные изделия", CommodityCategoryHelper.GetDisplayName(CommodityCategory.Sausages));
    }

    [Fact]
    public void GetGroup_ReturnsExpectedGroupsForNewCategories()
    {
        // Продукты: 18–30 + новые 42–45 (словарь, ADR 022)
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.Beverages));
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.FastFood));
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.DriedFruits));
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.Sauces));
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.Spices));
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.Sausages));
        // Транспорт: 31–37
        Assert.Equal("Транспорт", CommodityCategoryHelper.GetGroup(CommodityCategory.TollRoads));
        Assert.Equal("Транспорт", CommodityCategoryHelper.GetGroup(CommodityCategory.Parking));
        // Прочее: 38–41
        Assert.Equal("Прочее", CommodityCategoryHelper.GetGroup(CommodityCategory.Tobacco));
        Assert.Equal("Прочее", CommodityCategoryHelper.GetGroup(CommodityCategory.Entertainment));
    }

    [Fact]
    public void NewFoodDetailCategories_HaveExpectedCodes()
    {
        Assert.Equal(42, (int)CommodityCategory.DriedFruits);
        Assert.Equal(43, (int)CommodityCategory.Sauces);
        Assert.Equal(44, (int)CommodityCategory.Spices);
        Assert.Equal(45, (int)CommodityCategory.Sausages);
        Assert.Equal(255, (int)CommodityCategory.Other); // защита от переименования
    }

    [Fact]
    public void Enum_Values_AreUnique()
    {
        // Защита от коллизий кодов при будущих расширениях (ADR 022, риски).
        var values = Enum.GetValues<CommodityCategory>();
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void ExistingCategoryCodes_ArePreserved()
    {
        // Спот-проверка неизменности ключевых кодов (только добавление, ADR 010/022).
        Assert.Equal(1, (int)CommodityCategory.Food);
        Assert.Equal(18, (int)CommodityCategory.Beverages);
        Assert.Equal(30, (int)CommodityCategory.FastFood);
        Assert.Equal(31, (int)CommodityCategory.TollRoads);
        Assert.Equal(37, (int)CommodityCategory.Parking);
        Assert.Equal(38, (int)CommodityCategory.Tobacco);
        Assert.Equal(41, (int)CommodityCategory.Entertainment);
    }

    [Fact]
    public void GetGroup_ReturnsEmptyStringForLegacyCategories()
    {
        // Старые категории (0–17 и Other = 255) не относятся ни к одной группе —
        // они отображаются плоским списком без <optgroup> (решение D1 ADR 010).
        Assert.Equal("", CommodityCategoryHelper.GetGroup(CommodityCategory.Undefined));
        Assert.Equal("", CommodityCategoryHelper.GetGroup(CommodityCategory.Food));
        Assert.Equal("", CommodityCategoryHelper.GetGroup(CommodityCategory.Clothing));
        Assert.Equal("", CommodityCategoryHelper.GetGroup(CommodityCategory.Footwear));
        Assert.Equal("", CommodityCategoryHelper.GetGroup(CommodityCategory.Other));
    }
}
