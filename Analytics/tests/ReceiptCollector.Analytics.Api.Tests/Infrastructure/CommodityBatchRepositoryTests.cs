using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты owner-scoped чтения и пакетной записи категорий (T-01, план 10.1, ADR-028 п. 9).
/// Глобальный query-filter на <c>Receipts</c> в <see cref="CommodityRepository"/> не активен
/// (<c>SetCurrentUser</c> не вызывается) — тесты проверяют именно явный owner-фильтр.
/// </summary>
public class CommodityBatchRepositoryTests
{
    private static ReceiptDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new ReceiptDbContext(options);
    }

    private sealed record Seed(Guid OwnerId, Guid StrangerId, Guid OwnerCommodityId, Guid StrangerCommodityId);

    private static async Task<Seed> SeedAsync(ReceiptDbContext context)
    {
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();

        var ownerReceiptId = Guid.NewGuid();
        var strangerReceiptId = Guid.NewGuid();
        var ownerCommodityId = Guid.NewGuid();
        var strangerCommodityId = Guid.NewGuid();

        await context.Merchants.AddAsync(new MerchantEntity
        {
            Id = merchantId,
            Name = "Test Store",
            Category = MerchantCategory.Undefined
        });

        await context.Receipts.AddRangeAsync(
            new ReceiptEntity
            {
                Id = ownerReceiptId,
                UserId = ownerId,
                MerchantId = merchantId,
                ExternalId = "owner",
                TotalAmount = 100m,
                PurchasedAt = DateTime.UtcNow,
                Items =
                [
                    new CommodityEntity
                    {
                        Id = ownerCommodityId,
                        ReceiptId = ownerReceiptId,
                        Name = "Молоко",
                        Quantity = 1,
                        UnitPrice = 100,
                        Nds = 0,
                        NdsSum = 0
                    }
                ]
            },
            new ReceiptEntity
            {
                Id = strangerReceiptId,
                UserId = strangerId,
                MerchantId = merchantId,
                ExternalId = "stranger",
                TotalAmount = 100m,
                PurchasedAt = DateTime.UtcNow,
                Items =
                [
                    new CommodityEntity
                    {
                        Id = strangerCommodityId,
                        ReceiptId = strangerReceiptId,
                        Name = "Чужой товар",
                        Quantity = 1,
                        UnitPrice = 100,
                        Nds = 0,
                        NdsSum = 0
                    }
                ]
            });

        await context.SaveChangesAsync();

        return new Seed(ownerId, strangerId, ownerCommodityId, strangerCommodityId);
    }

    [Fact]
    public async Task GetByOwnerAsync_returns_only_current_user_commodities()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var seed = await SeedAsync(context);
        var repository = new CommodityRepository(context);

        var items = await repository.GetByOwnerAsync(
            seed.OwnerId,
            [seed.OwnerCommodityId, seed.StrangerCommodityId],
            CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(seed.OwnerCommodityId, item.Id);
        Assert.Equal("Молоко", item.Name);
        Assert.Null(item.Category);
    }

    [Fact]
    public async Task GetByOwnerAsync_with_unknown_id_returns_partial_result_without_exception()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var seed = await SeedAsync(context);
        var repository = new CommodityRepository(context);

        var items = await repository.GetByOwnerAsync(
            seed.OwnerId,
            [seed.OwnerCommodityId, Guid.NewGuid()],
            CancellationToken.None);

        Assert.Single(items);
    }

    [Fact]
    public async Task GetByOwnerAsync_maps_undefined_category_as_category_zero()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var seed = await SeedAsync(context);
        var commodity = await context.Commodities.SingleAsync(c => c.Id == seed.OwnerCommodityId);
        commodity.CategoryId = (int)CommodityCategory.Undefined;
        commodity.CategoryName = CommodityCategoryHelper.GetDisplayName(CommodityCategory.Undefined);
        await context.SaveChangesAsync();

        var repository = new CommodityRepository(context);
        var item = Assert.Single(await repository.GetByOwnerAsync(
            seed.OwnerId, [seed.OwnerCommodityId], CancellationToken.None));

        Assert.NotNull(item.Category);
        Assert.Equal((int)CommodityCategory.Undefined, item.Category!.Id);
    }

    [Fact]
    public async Task UpdateCategoriesAsync_writes_category_id_and_name_and_returns_changed_count()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var seed = await SeedAsync(context);
        var repository = new CommodityRepository(context);

        var updated = await repository.UpdateCategoriesAsync(
            seed.OwnerId,
            new Dictionary<Guid, CommodityCategory>
            {
                [seed.OwnerCommodityId] = CommodityCategory.Dairy
            },
            CancellationToken.None);

        Assert.Equal(1, updated);

        var entity = await context.Commodities.AsNoTracking().SingleAsync(c => c.Id == seed.OwnerCommodityId);
        Assert.Equal((int)CommodityCategory.Dairy, entity.CategoryId);
        Assert.Equal(CommodityCategoryHelper.GetDisplayName(CommodityCategory.Dairy), entity.CategoryName);
    }

    [Fact]
    public async Task UpdateCategoriesAsync_with_same_value_returns_zero_and_keeps_entity_unchanged()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var seed = await SeedAsync(context);
        var commodity = await context.Commodities.SingleAsync(c => c.Id == seed.OwnerCommodityId);
        commodity.CategoryId = (int)CommodityCategory.Dairy;
        commodity.CategoryName = CommodityCategoryHelper.GetDisplayName(CommodityCategory.Dairy);
        await context.SaveChangesAsync();

        var repository = new CommodityRepository(context);
        var updated = await repository.UpdateCategoriesAsync(
            seed.OwnerId,
            new Dictionary<Guid, CommodityCategory>
            {
                [seed.OwnerCommodityId] = CommodityCategory.Dairy
            },
            CancellationToken.None);

        Assert.Equal(0, updated);
        Assert.All(
            context.ChangeTracker.Entries<CommodityEntity>(),
            entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public async Task UpdateCategoriesAsync_with_stranger_id_throws_and_writes_nothing()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var seed = await SeedAsync(context);
        var repository = new CommodityRepository(context);

        var exception = await Assert.ThrowsAsync<CommoditiesNotAccessibleException>(() =>
            repository.UpdateCategoriesAsync(
                seed.OwnerId,
                new Dictionary<Guid, CommodityCategory>
                {
                    [seed.OwnerCommodityId] = CommodityCategory.Dairy,
                    [seed.StrangerCommodityId] = CommodityCategory.Electronics
                },
                CancellationToken.None));

        Assert.Equal(1, exception.NotAccessibleCount);

        // Ничего не записано: обе строки остались без категории.
        context.ChangeTracker.Clear();
        var persisted = await context.Commodities.AsNoTracking().ToListAsync();
        Assert.All(persisted, item =>
        {
            Assert.Null(item.CategoryId);
            Assert.Null(item.CategoryName);
        });
    }

    [Fact]
    public async Task UpdateCategoriesAsync_with_unknown_id_throws_and_writes_nothing()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var seed = await SeedAsync(context);
        var repository = new CommodityRepository(context);

        await Assert.ThrowsAsync<CommoditiesNotAccessibleException>(() =>
            repository.UpdateCategoriesAsync(
                seed.OwnerId,
                new Dictionary<Guid, CommodityCategory>
                {
                    [seed.OwnerCommodityId] = CommodityCategory.Dairy,
                    [Guid.NewGuid()] = CommodityCategory.Dairy
                },
                CancellationToken.None));

        context.ChangeTracker.Clear();
        var entity = await context.Commodities.AsNoTracking().SingleAsync(c => c.Id == seed.OwnerCommodityId);
        Assert.Null(entity.CategoryId);
    }
}
