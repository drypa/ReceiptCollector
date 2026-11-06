using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests;

public class ReceiptReadServiceTests
{
    [Fact]
    public async Task GetRecentAsync_returns_empty_when_no_receipts()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ReceiptDbContext(options);
        var service = new ReceiptReadService(context);

        var summary = await service.GetRecentAsync(Guid.NewGuid(), 10, 0, CancellationToken.None);

        Assert.Empty(summary);
    }

    [Fact]
    public async Task Service_returns_receipt_details_for_existing_receipt()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ReceiptDbContext(options);

        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var merchantName = "Local Store";
        var merchantAddress = "Test Address";
        var merchantInn = "1234567890";
        var merchant = new MerchantEntity
        {
            Id = merchantId,
            Name = merchantName,
            Category = MerchantCategory.Undefined,
            Address = merchantAddress,
            Inn = merchantInn
        };

        var entity = new ReceiptEntity
        {
            Id = receiptId,
            UserId = userId,
            MerchantId = merchantId,
            ExternalId = "external",
            TotalAmount = 100m,
            PurchasedAt = DateTime.UtcNow,
            Items =
            [
                new CommodityEntity
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = receiptId,
                    Name = "Milk",
                    Quantity = 1,
                    UnitPrice = 100,
                    Nds = 0,
                    NdsSum = 0
                }
            ]
        };

        await context.Merchants.AddAsync(merchant);
        await context.Receipts.AddAsync(entity);
        await context.SaveChangesAsync();

        var service = new ReceiptReadService(context);

        var summaries = await service.GetRecentAsync(userId, 10, 0, CancellationToken.None);
        Assert.Single(summaries);

        var details = await service.GetByIdAsync(userId, receiptId, CancellationToken.None);
        Assert.NotNull(details);
        Assert.Equal(merchantName, details!.Merchant.Name);
        Assert.Equal(merchantInn, details!.Merchant.Inn);
        Assert.Equal(merchantAddress, details!.Merchant.Address);
        Assert.Single(details.Items);
    }

    // ——— T-10 (ADR-032): MerchantDto в деталях и сводках чека отдаёт категорию по умолчанию ———

    [Fact]
    public async Task GetByIdAsync_merchant_with_default_commodity_category_returns_id_and_display_name()
    {
        await using var context = CreateContext();
        var (userId, _, receiptId) = await SeedReceiptAsync(context, (int)CommodityCategory.ReadyMeals);
        var service = new ReceiptReadService(context);

        var details = await service.GetByIdAsync(userId, receiptId, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal((int)CommodityCategory.ReadyMeals, details.Merchant.DefaultCommodityCategoryId);
        Assert.Equal(
            CommodityCategoryHelper.GetDisplayName(CommodityCategory.ReadyMeals),
            details.Merchant.DefaultCommodityCategoryName);
    }

    [Fact]
    public async Task Merchant_without_default_commodity_category_yields_null_fields_in_details_and_summaries()
    {
        await using var context = CreateContext();
        var (userId, merchantId, receiptId) = await SeedReceiptAsync(context, null);
        var service = new ReceiptReadService(context);

        var details = await service.GetByIdAsync(userId, receiptId, CancellationToken.None);
        Assert.NotNull(details);
        Assert.Null(details.Merchant.DefaultCommodityCategoryId);
        Assert.Null(details.Merchant.DefaultCommodityCategoryName);

        var summaries = await service.GetRecentAsync(userId, 10, 0, CancellationToken.None);
        var summary = Assert.Single(summaries);
        Assert.Null(summary.Merchant.DefaultCommodityCategoryId);
        Assert.Null(summary.Merchant.DefaultCommodityCategoryName);
    }

    [Theory]
    // Безопасное чтение (ADR-032, A1): NULL/0 хранятся в колонке как «не задана», а невалидный
    // код (категория удалена из справочника) не должен попасть в DTO — тот же фильтр, что MapToDomain.
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(9999)]
    public async Task GetByIdAsync_invalid_or_empty_default_code_returns_null_fields(int? rawCode)
    {
        await using var context = CreateContext();
        var (userId, _, receiptId) = await SeedReceiptAsync(context, rawCode);
        var service = new ReceiptReadService(context);

        var details = await service.GetByIdAsync(userId, receiptId, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Null(details.Merchant.DefaultCommodityCategoryId);
        Assert.Null(details.Merchant.DefaultCommodityCategoryName);
    }

    [Fact]
    public async Task Summaries_fill_default_commodity_category_id_and_display_name()
    {
        await using var context = CreateContext();
        var (userId, merchantId, _) = await SeedReceiptAsync(context, (int)CommodityCategory.Groceries);
        var service = new ReceiptReadService(context);

        var expectedName = CommodityCategoryHelper.GetDisplayName(CommodityCategory.Groceries);

        var recent = Assert.Single(await service.GetRecentAsync(userId, 10, 0, CancellationToken.None));
        Assert.Equal((int)CommodityCategory.Groceries, recent.Merchant.DefaultCommodityCategoryId);
        Assert.Equal(expectedName, recent.Merchant.DefaultCommodityCategoryName);

        var byMerchant = Assert.Single(
            await service.GetByMerchantIdAsync(userId, merchantId, 10, 0, CancellationToken.None));
        Assert.Equal((int)CommodityCategory.Groceries, byMerchant.Merchant.DefaultCommodityCategoryId);
        Assert.Equal(expectedName, byMerchant.Merchant.DefaultCommodityCategoryName);
    }

    private static DbContextOptions<ReceiptDbContext> CreateOptions()
        => new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static ReceiptDbContext CreateContext() => new(CreateOptions());

    /// <summary>Магазин с указанным сырым кодом дефолта (null — не задан) + один чек этого пользователя.</summary>
    private static async Task<(Guid UserId, Guid MerchantId, Guid ReceiptId)> SeedReceiptAsync(
        ReceiptDbContext context,
        int? defaultCommodityCategoryId)
    {
        var userId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();

        await context.Merchants.AddAsync(new MerchantEntity
        {
            Id = merchantId,
            Name = "Canteen",
            Category = MerchantCategory.Flowers,
            Address = null,
            Inn = null,
            DefaultCommodityCategoryId = defaultCommodityCategoryId
        });

        await context.Receipts.AddAsync(new ReceiptEntity
        {
            Id = receiptId,
            UserId = userId,
            MerchantId = merchantId,
            ExternalId = $"seed-{Guid.NewGuid():N}",
            TotalAmount = 250m,
            PurchasedAt = DateTime.UtcNow,
            Items = []
        });

        await context.SaveChangesAsync();
        return (userId, merchantId, receiptId);
    }
}
