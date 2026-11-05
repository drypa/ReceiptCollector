using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// EF-тесты batch-провайдера дефолтов магазинов (T-03, ADR-032 D1): запрос
/// <c>receipts JOIN merchants</c> по <c>receipts.id IN (...)</c> + безопасное чтение кода
/// (NULL/0/устаревший → null — «дефолта нет»). Схема — EF InMemory (прецедент
/// <see cref="MerchantRepositoryDefaultsTests"/>); невалидные коды сеются напрямую в entity:
/// CHECK «&lt;&gt; 0» живёт только в SQL-миграции.
/// </summary>
public sealed class MerchantDefaultCategoryProviderTests
{
    private static ReceiptDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ReceiptDbContext(options);
    }

    private static MerchantEntity CreateMerchant(Guid id, int? defaultCommodityCategoryId) =>
        new()
        {
            Id = id,
            Name = $"Store {id:N}",
            Category = MerchantCategory.Undefined,
            DefaultCommodityCategoryId = defaultCommodityCategoryId
        };

    private static ReceiptEntity CreateReceipt(Guid id, Guid userId, Guid merchantId) =>
        new()
        {
            Id = id,
            UserId = userId,
            MerchantId = merchantId,
            ExternalId = $"external-{id:N}",
            TotalAmount = 100m,
            PurchasedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task GetDefaultsByReceiptIdsAsync_MapsDefaultsThroughReceiptMerchantJoin()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var provider = new MerchantDefaultCategoryProvider(context);

        var userId = Guid.NewGuid();
        var defaultMerchant = CreateMerchant(Guid.NewGuid(), (int)CommodityCategory.ReadyMeals);
        var plainMerchant = CreateMerchant(Guid.NewGuid(), null);
        var defaultReceipt = CreateReceipt(Guid.NewGuid(), userId, defaultMerchant.Id);
        var plainReceipt = CreateReceipt(Guid.NewGuid(), userId, plainMerchant.Id);

        await context.Merchants.AddRangeAsync(defaultMerchant, plainMerchant);
        await context.Receipts.AddRangeAsync(defaultReceipt, plainReceipt);
        await context.SaveChangesAsync();

        var result = await provider.GetDefaultsByReceiptIdsAsync(
            [defaultReceipt.Id, plainReceipt.Id],
            CancellationToken.None);

        // Чек магазина с дефолтом — категория; чек магазина без дефолта — null («дефолта нет»).
        Assert.Equal(2, result.Count);
        Assert.Equal(CommodityCategory.ReadyMeals, result[defaultReceipt.Id]);
        Assert.Null(result[plainReceipt.Id]);
    }

    [Fact]
    public async Task GetDefaultsByReceiptIdsAsync_SafeReadsZeroAndStaleCodes()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var provider = new MerchantDefaultCategoryProvider(context);

        var userId = Guid.NewGuid();
        var zeroMerchant = CreateMerchant(Guid.NewGuid(), 0);
        var staleMerchant = CreateMerchant(Guid.NewGuid(), 9999);
        var zeroReceipt = CreateReceipt(Guid.NewGuid(), userId, zeroMerchant.Id);
        var staleReceipt = CreateReceipt(Guid.NewGuid(), userId, staleMerchant.Id);

        await context.Merchants.AddRangeAsync(zeroMerchant, staleMerchant);
        await context.Receipts.AddRangeAsync(zeroReceipt, staleReceipt);
        await context.SaveChangesAsync();

        var result = await provider.GetDefaultsByReceiptIdsAsync(
            [zeroReceipt.Id, staleReceipt.Id],
            CancellationToken.None);

        // 0 (Undefined) и код вне справочника трактуются как «дефолта нет» (ADR-032, A1).
        Assert.Null(result[zeroReceipt.Id]);
        Assert.Null(result[staleReceipt.Id]);
    }

    [Fact]
    public async Task GetDefaultsByReceiptIdsAsync_UnknownReceipts_AreAbsent()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var provider = new MerchantDefaultCategoryProvider(context);

        var merchant = CreateMerchant(Guid.NewGuid(), (int)CommodityCategory.Food);
        var known = CreateReceipt(Guid.NewGuid(), Guid.NewGuid(), merchant.Id);
        var unknown = Guid.NewGuid();
        await context.Merchants.AddAsync(merchant);
        await context.Receipts.AddAsync(known);
        await context.SaveChangesAsync();

        var result = await provider.GetDefaultsByReceiptIdsAsync([known.Id, unknown], CancellationToken.None);

        // Чека нет в хранилище — ключа нет в словаре (в отличие от null — «нет дефолта», контракт провайдера).
        Assert.True(result.ContainsKey(known.Id));
        Assert.False(result.ContainsKey(unknown));
        Assert.Single(result);
    }

    [Fact]
    public async Task GetDefaultsByReceiptIdsAsync_DuplicateInputIds_ProduceSingleEntryPerReceipt()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var provider = new MerchantDefaultCategoryProvider(context);

        var merchant = CreateMerchant(Guid.NewGuid(), (int)CommodityCategory.Dairy);
        var receipt = CreateReceipt(Guid.NewGuid(), Guid.NewGuid(), merchant.Id);
        await context.Merchants.AddAsync(merchant);
        await context.Receipts.AddAsync(receipt);
        await context.SaveChangesAsync();

        var result = await provider.GetDefaultsByReceiptIdsAsync(
            [receipt.Id, receipt.Id, receipt.Id],
            CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(CommodityCategory.Dairy, result[receipt.Id]);
    }

    [Fact]
    public async Task GetDefaultsByReceiptIdsAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var provider = new MerchantDefaultCategoryProvider(context);

        var result = await provider.GetDefaultsByReceiptIdsAsync([], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDefaultsByReceiptIdsAsync_RespectsOwnerQueryFilter()
    {
        // Query-filter Receipts не обходится (в отличие от дожима E1 из ADR-032): suggest-запрос
        // видит дефолты только чеков текущего пользователя.
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var provider = new MerchantDefaultCategoryProvider(context);

        var merchant = CreateMerchant(Guid.NewGuid(), (int)CommodityCategory.ReadyMeals);
        var ownReceipt = CreateReceipt(Guid.NewGuid(), userId: Guid.NewGuid(), merchant.Id);
        var foreignReceipt = CreateReceipt(Guid.NewGuid(), userId: Guid.NewGuid(), merchant.Id);
        await context.Merchants.AddAsync(merchant);
        await context.Receipts.AddRangeAsync(ownReceipt, foreignReceipt);
        await context.SaveChangesAsync();

        context.SetCurrentUser(ownReceipt.UserId);

        var result = await provider.GetDefaultsByReceiptIdsAsync(
            [ownReceipt.Id, foreignReceipt.Id],
            CancellationToken.None);

        Assert.True(result.ContainsKey(ownReceipt.Id));
        Assert.False(result.ContainsKey(foreignReceipt.Id));
    }
}
