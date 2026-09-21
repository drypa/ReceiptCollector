using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CommodityReadServiceFilterTests
{
    private static ReceiptDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ReceiptDbContext(options);
    }

    private static async Task<Guid> SeedAsync(ReceiptDbContext context)
    {
        var userId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();

        var merchant = new MerchantEntity
        {
            Id = merchantId,
            Name = "Test Store",
            Category = MerchantCategory.Undefined
        };

        var receipt = new ReceiptEntity
        {
            Id = receiptId,
            UserId = userId,
            MerchantId = merchantId,
            ExternalId = "external",
            TotalAmount = 300m,
            PurchasedAt = DateTime.UtcNow,
            Items =
            [
                new CommodityEntity { Id = Guid.NewGuid(), ReceiptId = receiptId, Name = "Без категории", Quantity = 1, UnitPrice = 100, Nds = 0, NdsSum = 0 },
                new CommodityEntity { Id = Guid.NewGuid(), ReceiptId = receiptId, Name = "Не указана", Quantity = 1, UnitPrice = 100, Nds = 0, NdsSum = 0, CategoryId = 0, CategoryName = "Не указана" },
                new CommodityEntity { Id = Guid.NewGuid(), ReceiptId = receiptId, Name = "Молоко", Quantity = 1, UnitPrice = 100, Nds = 0, NdsSum = 0, CategoryId = 23, CategoryName = "Молочные продукты" }
            ]
        };

        await context.Merchants.AddAsync(merchant);
        await context.Receipts.AddAsync(receipt);
        await context.SaveChangesAsync();

        return userId;
    }

    [Fact]
    public async Task GetAsync_without_filter_returns_all_commodities()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var userId = await SeedAsync(context);
        var service = new CommodityReadService(context);

        var items = await service.GetAsync(userId, 10, 0, CommodityCategoryFilter.Any, CancellationToken.None);

        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task GetAsync_uncategorized_returns_only_commodities_with_null_category()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var userId = await SeedAsync(context);
        var service = new CommodityReadService(context);

        var items = await service.GetAsync(userId, 10, 0, CommodityCategoryFilter.Uncategorized, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Без категории", item.Name);
        Assert.Null(item.CategoryId);
    }

    [Fact]
    public async Task GetAsync_undefined_returns_only_categorized_as_undefined()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var userId = await SeedAsync(context);
        var service = new CommodityReadService(context);

        var items = await service.GetAsync(userId, 10, 0, CommodityCategoryFilter.Undefined, CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal("Не указана", item.Name);
        Assert.Equal(0, item.CategoryId);
    }

    [Fact]
    public async Task GetTotalCountAsync_respects_the_filter()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var userId = await SeedAsync(context);
        var service = new CommodityReadService(context);

        var uncategorizedCount = await service.GetTotalCountAsync(userId, CommodityCategoryFilter.Uncategorized, CancellationToken.None);
        var undefinedCount = await service.GetTotalCountAsync(userId, CommodityCategoryFilter.Undefined, CancellationToken.None);

        Assert.Equal(1, uncategorizedCount);
        Assert.Equal(1, undefinedCount);
    }

    [Fact]
    public async Task ReceiptItemDto_contains_commodity_id()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var userId = await SeedAsync(context);
        var service = new ReceiptReadService(context);

        var receiptId = context.Receipts.Single().Id;
        var details = await service.GetByIdAsync(userId, receiptId, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(3, details!.Items.Count);
        Assert.All(details.Items, item => Assert.NotEqual(Guid.Empty, item.Id));
        Assert.Equal(details.Items.Single(i => i.Name == "Молоко").Id, context.Commodities.Single(c => c.Name == "Молоко").Id);
    }
}