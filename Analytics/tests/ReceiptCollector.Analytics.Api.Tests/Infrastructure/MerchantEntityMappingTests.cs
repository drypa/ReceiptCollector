using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Маппинг дефолтной товарной категории магазина (T-02, ADR-032 A1/B1): domain→entity
/// (<c>DefaultCommodityCategory?.Id</c>), безопасное чтение в <c>MapToDomain</c>
/// (невалидный код / 0 → «не задана», прецедент PrecategorizedCommodity ADR-027 D11)
/// и соответствие колонок схеме миграции 20261105130000.
/// </summary>
public sealed class MerchantEntityMappingTests
{
    [Fact]
    public void Create_MapsDefaultCommodityCategoryCode()
    {
        var merchant = new Merchant(Guid.NewGuid(), "Столовая", defaultCommodityCategory: CommodityCategory.ReadyMeals);

        var entity = MerchantEntity.Create(merchant);

        Assert.Equal((int)CommodityCategory.ReadyMeals, entity.DefaultCommodityCategoryId);
    }

    [Fact]
    public void Create_MapsMissingDefaultToNullColumn()
    {
        var merchant = new Merchant(Guid.NewGuid(), "Магазин");

        var entity = MerchantEntity.Create(merchant);

        Assert.Null(entity.DefaultCommodityCategoryId);
    }

    [Fact]
    public void MapToDomain_ValidCode_ReturnsCategory()
    {
        var entity = CreateEntity(defaultCommodityCategoryId: (int)CommodityCategory.Dairy);

        var domain = entity.MapToDomain();

        Assert.Equal(CommodityCategory.Dairy, domain.DefaultCommodityCategory);
    }

    [Fact]
    public void MapToDomain_NullCode_ReturnsNull()
    {
        var entity = CreateEntity(defaultCommodityCategoryId: null);

        Assert.Null(entity.MapToDomain().DefaultCommodityCategory);
    }

    [Fact]
    public void MapToDomain_ZeroCode_ReturnsNull()
    {
        // 0 (Undefined) недопустим CHECK'ом миграции, но безопасное чтение обязано
        // трактовать его как «не задан» (оборонительное поведение, ADR-032 A1).
        var entity = CreateEntity(defaultCommodityCategoryId: 0);

        Assert.Null(entity.MapToDomain().DefaultCommodityCategory);
    }

    [Fact]
    public void MapToDomain_CodeOutsideReference_ReturnsNull()
    {
        // Edge case «категория удалена из справочника»: код вне enum → «не задана».
        var entity = CreateEntity(defaultCommodityCategoryId: 9999);

        Assert.Null(entity.MapToDomain().DefaultCommodityCategory);
    }

    [Fact]
    public void RoundTrip_SetDefault_Create_MapToDomain_PreservesValue()
    {
        var merchant = new Merchant(Guid.NewGuid(), "Фитнес-клуб");

        merchant.UpdateDefaultCommodityCategory(CommodityCategory.FitnessAndSports);
        var domain = MerchantEntity.Create(merchant).MapToDomain();

        Assert.Equal(CommodityCategory.FitnessAndSports, domain.DefaultCommodityCategory);
    }

    [Fact]
    public void UpdateDefaultCommodityCategory_SetsChangesAndResets()
    {
        var merchant = new Merchant(Guid.NewGuid(), "Магазин");
        Assert.Null(merchant.DefaultCommodityCategory);

        merchant.UpdateDefaultCommodityCategory(CommodityCategory.AutomotiveGoods);
        Assert.Equal(CommodityCategory.AutomotiveGoods, merchant.DefaultCommodityCategory);

        merchant.UpdateDefaultCommodityCategory(CommodityCategory.Pharmacy);
        Assert.Equal(CommodityCategory.Pharmacy, merchant.DefaultCommodityCategory);

        // Сброс = null (FR-M1); инварианта на enum в домене нет (валидация — эндпоинт, T-07).
        merchant.UpdateDefaultCommodityCategory(null);
        Assert.Null(merchant.DefaultCommodityCategory);
    }

    [Fact]
    public void CommodityEntityCreate_LeavesCategorySourceNull()
    {
        // Новая позиция не имеет persisted-источника: NULL = manual/legacy (ADR-032, B1);
        // 'merchant-default' пишут только операции применения (T-04).
        var commodity = new Commodity(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Молоко",
            1m,
            100m,
            0,
            0m,
            new Category((int)CommodityCategory.Dairy, "Молочные продукты"));

        var entity = CommodityEntity.Create(commodity);

        Assert.Null(entity.CategorySource);
        Assert.Equal((int)CommodityCategory.Dairy, entity.CategoryId);
    }

    [Fact]
    public void ColumnMapping_MatchesMigrationSchema()
    {
        // Имена колонок задаёт UseSnakeCaseNamingConvention — проверка соответствия
        // миграции 20261105130000_add_merchant_default_commodity_category (модель строится без подключения).
        using var context = new ReceiptDbContext(
            new DbContextOptionsBuilder<ReceiptDbContext>()
                .UseNpgsql("Host=localhost;Database=mapping_check;Username=u;Password=test")
                .UseSnakeCaseNamingConvention()
                .Options);

        var merchantColumn = context.Model
            .FindEntityType(typeof(MerchantEntity))!
            .FindProperty(nameof(MerchantEntity.DefaultCommodityCategoryId))!;
        Assert.Equal("default_commodity_category_id", merchantColumn.GetColumnName());
        Assert.True(merchantColumn.IsNullable);

        var commodityColumn = context.Model
            .FindEntityType(typeof(CommodityEntity))!
            .FindProperty(nameof(CommodityEntity.CategorySource))!;
        Assert.Equal("category_source", commodityColumn.GetColumnName());
        Assert.Equal(32, commodityColumn.GetMaxLength());
    }

    private static MerchantEntity CreateEntity(int? defaultCommodityCategoryId)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = "Тестовый магазин",
            Category = MerchantCategory.Undefined,
            DefaultCommodityCategoryId = defaultCommodityCategoryId
        };
}
