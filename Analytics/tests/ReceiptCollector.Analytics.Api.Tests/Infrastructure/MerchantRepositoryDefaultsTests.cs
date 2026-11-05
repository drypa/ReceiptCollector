using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Batch-чтение дефолтов магазинов и round-trip записи через репозиторий (T-02, ADR-032 A1/D1).
/// Схема — EF InMemory (прецедент <see cref="CommodityBatchRepositoryTests"/>); невалидные коды
/// сеются напрямую в entity: CHECK «&lt;&gt; 0» живёт только в SQL-миграции, безопасное чтение
/// проверяется на уровне приложения.
/// </summary>
public sealed class MerchantRepositoryDefaultsTests
{
    private static ReceiptDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ReceiptDbContext(options);
    }

    [Fact]
    public async Task GetDefaultCommodityCategoriesAsync_ReturnsOnlyValidNonZeroDefaults()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var repository = new MerchantRepository(context);

        var valid = CreateMerchant(defaultCommodityCategoryId: (int)CommodityCategory.ReadyMeals);
        var missing = CreateMerchant(defaultCommodityCategoryId: null);
        var zero = CreateMerchant(defaultCommodityCategoryId: 0);
        var stale = CreateMerchant(defaultCommodityCategoryId: 9999);
        await context.Merchants.AddRangeAsync(valid, missing, zero, stale);
        await context.SaveChangesAsync();

        var result = await repository.GetDefaultCommodityCategoriesAsync(
            new[] { valid.Id, missing.Id, zero.Id, stale.Id },
            CancellationToken.None);

        // Магазины без валидного ненулевого дефолта в словаре отсутствуют (ADR-032, D1).
        Assert.Single(result);
        Assert.Equal(CommodityCategory.ReadyMeals, result[valid.Id]);
        Assert.False(result.ContainsKey(missing.Id));
        Assert.False(result.ContainsKey(zero.Id));
        Assert.False(result.ContainsKey(stale.Id));
    }

    [Fact]
    public async Task GetDefaultCommodityCategoriesAsync_IgnoresMerchantsNotRequested()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var repository = new MerchantRepository(context);

        var requested = CreateMerchant(defaultCommodityCategoryId: (int)CommodityCategory.Dairy);
        var other = CreateMerchant(defaultCommodityCategoryId: (int)CommodityCategory.Food);
        await context.Merchants.AddRangeAsync(requested, other);
        await context.SaveChangesAsync();

        var result = await repository.GetDefaultCommodityCategoriesAsync([requested.Id], CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(CommodityCategory.Dairy, result[requested.Id]);
        Assert.False(result.ContainsKey(other.Id));
    }

    [Fact]
    public async Task GetDefaultCommodityCategoriesAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var repository = new MerchantRepository(context);

        await context.Merchants.AddAsync(CreateMerchant(defaultCommodityCategoryId: (int)CommodityCategory.Dairy));
        await context.SaveChangesAsync();

        var result = await repository.GetDefaultCommodityCategoriesAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByIdAsync_SafeReadsDefaultColumn()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());

        var valid = CreateMerchant(defaultCommodityCategoryId: (int)CommodityCategory.FitnessAndSports);
        var zero = CreateMerchant(defaultCommodityCategoryId: 0);
        var stale = CreateMerchant(defaultCommodityCategoryId: 9999);
        var missing = CreateMerchant(defaultCommodityCategoryId: null);
        await context.Merchants.AddRangeAsync(valid, zero, stale, missing);
        await context.SaveChangesAsync();

        var repository = new MerchantRepository(context);

        Assert.Equal(CommodityCategory.FitnessAndSports, (await repository.GetByIdAsync(valid.Id))!.DefaultCommodityCategory);
        Assert.Null((await repository.GetByIdAsync(zero.Id))!.DefaultCommodityCategory);
        Assert.Null((await repository.GetByIdAsync(stale.Id))!.DefaultCommodityCategory);
        Assert.Null((await repository.GetByIdAsync(missing.Id))!.DefaultCommodityCategory);
    }

    [Fact]
    public async Task AddAsync_InsertPersistsDefaultCategory()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var repository = new MerchantRepository(context);

        var merchant = new Merchant(Guid.NewGuid(), "Столовая", inn: "7701111111");
        merchant.UpdateDefaultCommodityCategory(CommodityCategory.ReadyMeals);
        await repository.AddAsync(merchant, CancellationToken.None);

        var stored = await context.Merchants.SingleAsync(m => m.Id == merchant.Id);
        Assert.Equal((int)CommodityCategory.ReadyMeals, stored.DefaultCommodityCategoryId);

        var reloaded = await repository.GetByIdAsync(merchant.Id, CancellationToken.None);
        Assert.Equal(CommodityCategory.ReadyMeals, reloaded!.DefaultCommodityCategory);
    }

    [Fact]
    public async Task AddAsync_UpdateExisting_PersistsDefaultChangeAndReset()
    {
        // «Существующий repo-путь редактирования merchant» (AddAsync, прецедент ADR 011):
        // эндпоинт дефолта (T-07) сохраняет изменение и сброс именно через него.
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var repository = new MerchantRepository(context);

        var merchant = new Merchant(Guid.NewGuid(), "Магазин", inn: "7702222222");
        await repository.AddAsync(merchant, CancellationToken.None);

        var loaded = await repository.GetByIdAsync(merchant.Id, CancellationToken.None);
        Assert.Null(loaded!.DefaultCommodityCategory);

        loaded.UpdateDefaultCommodityCategory(CommodityCategory.AutomotiveGoods);
        await repository.AddAsync(loaded, CancellationToken.None);
        Assert.Equal(
            CommodityCategory.AutomotiveGoods,
            (await repository.GetByIdAsync(merchant.Id, CancellationToken.None))!.DefaultCommodityCategory);

        loaded = await repository.GetByIdAsync(merchant.Id, CancellationToken.None);
        loaded!.UpdateDefaultCommodityCategory(null);
        await repository.AddAsync(loaded, CancellationToken.None);

        var stored = await context.Merchants.SingleAsync(m => m.Id == merchant.Id);
        Assert.Null(stored.DefaultCommodityCategoryId);
        Assert.Null((await repository.GetByIdAsync(merchant.Id, CancellationToken.None))!.DefaultCommodityCategory);
    }

    [Fact]
    public async Task UpdateCategoryAsync_DoesNotTouchDefaultCommodityCategory()
    {
        // Существующий метод не меняет поведения и не задевает новую колонку (T-02).
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var repository = new MerchantRepository(context);

        var merchant = new Merchant(Guid.NewGuid(), "Книжный", inn: "7703333333");
        merchant.UpdateDefaultCommodityCategory(CommodityCategory.StationeryAndBooks);
        await repository.AddAsync(merchant, CancellationToken.None);

        await repository.UpdateCategoryAsync(merchant.Id, MerchantCategory.GroceryStores, CancellationToken.None);

        var stored = await context.Merchants.SingleAsync(m => m.Id == merchant.Id);
        Assert.Equal(MerchantCategory.GroceryStores, stored.Category);
        Assert.Equal((int)CommodityCategory.StationeryAndBooks, stored.DefaultCommodityCategoryId);
    }

    [Fact]
    public async Task CommodityEntityCategorySource_RoundTripsThroughStore()
    {
        // Колонка category_source читается/пишется на уровне entity; доменный Commodity её не знает
        // (ADR-032 B1, п. 2) — проверка, что маппинг сущности значение не теряет.
        var databaseName = Guid.NewGuid().ToString();

        Guid merchantId;
        Guid commodityId;
        await using (var context = CreateContext(databaseName))
        {
            var receiptId = Guid.NewGuid();
            merchantId = Guid.NewGuid();
            commodityId = Guid.NewGuid();

            await context.Merchants.AddAsync(CreateMerchant(merchantId));
            await context.Receipts.AddAsync(new ReceiptEntity
            {
                Id = receiptId,
                UserId = Guid.NewGuid(),
                MerchantId = merchantId,
                ExternalId = "external-1",
                TotalAmount = 100m,
                PurchasedAt = DateTime.UtcNow,
                Items =
                [
                    new CommodityEntity
                    {
                        Id = commodityId,
                        ReceiptId = receiptId,
                        Name = "Обед",
                        Quantity = 1,
                        UnitPrice = 250,
                        Nds = 0,
                        NdsSum = 0,
                        CategoryId = (int)CommodityCategory.ReadyMeals,
                        CategoryName = "Готовая еда и кулинария",
                        CategorySource = "merchant-default"
                    }
                ]
            });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(databaseName))
        {
            var stored = await context.Commodities.SingleAsync(c => c.Id == commodityId);
            Assert.Equal("merchant-default", stored.CategorySource);
        }
    }

    private static MerchantEntity CreateMerchant(
        int? defaultCommodityCategoryId = null)
        => CreateMerchant(Guid.NewGuid(), defaultCommodityCategoryId);

    private static MerchantEntity CreateMerchant(
        Guid id,
        int? defaultCommodityCategoryId = null)
        => new()
        {
            Id = id,
            Name = $"Store {id:N}",
            Category = MerchantCategory.Undefined,
            DefaultCommodityCategoryId = defaultCommodityCategoryId
        };
}
