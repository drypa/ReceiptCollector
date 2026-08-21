using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты консистентности справочника <see cref="CommodityCategory"/> после расширения
/// (ADR 010: 43 значения, Food переименован в «Прочая еда», группировка UI по полю Group).
/// Assert делаются на источнике данных <see cref="CommodityCategoryHelper.GetAll()"/> —
/// именно его отдаёт эндпоинт GET /api/commodities/categories.
/// </summary>
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

    [Fact]
    public void GetGroup_ReturnsExpectedGroupsForNewCategories()
    {
        // Продукты: 18–30
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.Beverages));
        Assert.Equal("Продукты", CommodityCategoryHelper.GetGroup(CommodityCategory.FastFood));
        // Транспорт: 31–37
        Assert.Equal("Транспорт", CommodityCategoryHelper.GetGroup(CommodityCategory.TollRoads));
        Assert.Equal("Транспорт", CommodityCategoryHelper.GetGroup(CommodityCategory.Parking));
        // Прочее: 38–41
        Assert.Equal("Прочее", CommodityCategoryHelper.GetGroup(CommodityCategory.Tobacco));
        Assert.Equal("Прочее", CommodityCategoryHelper.GetGroup(CommodityCategory.Entertainment));
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
