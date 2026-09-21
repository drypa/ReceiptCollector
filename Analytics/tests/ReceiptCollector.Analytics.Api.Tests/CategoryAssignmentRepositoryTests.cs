using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CategoryAssignmentRepositoryTests
{
    private static ReceiptDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ReceiptDbContext(options);
    }

    [Fact]
    public async Task GetByNormalizedNameAsync_returns_null_when_no_assignment()
    {
        await using var context = CreateContext();
        var repository = new CategoryAssignmentRepository(context);

        var result = await repository.GetByNormalizedNameAsync("молоко 2.5%", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertAsync_inserts_and_returns_the_assignment_by_normalized_name()
    {
        await using var context = CreateContext();
        var repository = new CategoryAssignmentRepository(context);

        var name = "Молоко 2.5%";
        var normalized = "молоко 2.5%";
        await repository.UpsertAsync(name, normalized, CommodityCategory.Dairy, CancellationToken.None);

        var stored = await repository.GetByNormalizedNameAsync(normalized, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(name, stored!.Name);
        Assert.Equal(normalized, stored.NormalizedName);
        Assert.Equal((int)CommodityCategory.Dairy, stored.CategoryId);
        Assert.Equal(CommodityCategoryHelper.GetDisplayName(CommodityCategory.Dairy), stored.CategoryName);
    }

    [Fact]
    public async Task UpsertAsync_updates_existing_assignment_with_new_category()
    {
        await using var context = CreateContext();
        var repository = new CategoryAssignmentRepository(context);

        var normalized = "кола";
        await repository.UpsertAsync("Кола", normalized, CommodityCategory.Beverages, CancellationToken.None);
        var first = await repository.GetByNormalizedNameAsync(normalized, CancellationToken.None);

        await repository.UpsertAsync("Кола 0.5", normalized, CommodityCategory.FastFood, CancellationToken.None);
        var updated = await repository.GetByNormalizedNameAsync(normalized, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(updated);
        Assert.NotSame(first, updated);
        Assert.Equal((int)CommodityCategory.FastFood, updated!.CategoryId);
        Assert.Equal("Кола 0.5", updated.Name);
        Assert.Equal(1, await context.CommodityCategoryAssignments.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAsync_does_not_create_duplicates_for_same_normalized_name()
    {
        await using var context = CreateContext();
        var repository = new CategoryAssignmentRepository(context);

        await repository.UpsertAsync("Яблоко", "яблоко", CommodityCategory.Fruits, CancellationToken.None);
        await repository.UpsertAsync("Яблоко Голден", "яблоко", CommodityCategory.Fruits, CancellationToken.None);

        var count = await context.CommodityCategoryAssignments.CountAsync(CancellationToken.None);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task UpsertAsync_rejects_undefined_category()
    {
        await using var context = CreateContext();
        var repository = new CategoryAssignmentRepository(context);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.UpsertAsync("Товар", "товар", CommodityCategory.Undefined, CancellationToken.None));

        Assert.Contains("category", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}