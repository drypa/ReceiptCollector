using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Сквозные тесты админского workflow предкатегоризации (шаг 9 плана, D8/D10, FR-5.4, FR-6, NFR-4/NFR-5):
/// реальные <see cref="PrecategorizedCommodityRepository"/> + <see cref="PrecategorizedCommodityService"/>
/// + in-memory кэш на Postgres-контейнере.
/// </summary>
public sealed class PrecategorizedCommodityWorkflowTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public PrecategorizedCommodityWorkflowTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task Confirm_updates_two_users_marks_applied_removes_siblings_and_fills_cache()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var merchantC = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var userC = Guid.NewGuid();
        var userD = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantA, ("Молоко", null)),
                CreateReceipt(userB, merchantB, ("Молоко", 0)),
                CreateReceipt(userC, merchantC, ("Молоко", (int)CommodityCategory.Food)),
                CreateReceipt(userD, merchantC, ("Молоко 1%", null)));

            context.PrecategorizedCommodities.AddRange(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, proposalId),
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, siblingId));
            await context.SaveChangesAsync();
        }

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        await using (var context = CreateContext())
        {
            var service = new PrecategorizedCommodityService(CreateRepository(context), cache);
            var result = await service.ConfirmAsync(proposalId, CancellationToken.None);

            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal(2, result.UpdatedCommodities);
            Assert.Equal((int)CommodityCategory.Dairy, result.CategoryId);
            Assert.Equal("Молочные продукты", result.CategoryName);
        }

        // FR-6.4: категория попала в кэш по нормализованному имени после commit.
        Assert.True(cache.TryGet("молоко", out var cached));
        Assert.Equal(CommodityCategory.Dairy, cached);

        await using (var context = CreateContext())
        {
            var milk = await context.Commodities.Where(c => c.Name == "Молоко").ToListAsync();
            Assert.Equal(2, milk.Count(c => c.CategoryId == (int)CommodityCategory.Dairy));
            Assert.Single(milk, c => c.CategoryId == (int)CommodityCategory.Food);

            var similar = Assert.Single(await context.Commodities.Where(c => c.Name == "Молоко 1%").ToListAsync());
            Assert.Null(similar.CategoryId);

            var stored = await context.PrecategorizedCommodities.FindAsync(proposalId);
            Assert.Equal(PrecategorizedCommodityStatus.Applied, stored!.Status);
            Assert.Null(await context.PrecategorizedCommodities.FindAsync(siblingId));
        }
    }

    [Fact]
    public async Task Confirm_marks_applied_with_zero_updated_commodities()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateReceipt(userA, merchantA, ("Молоко", (int)CommodityCategory.Food)));
            context.Merchants.Add(new MerchantEntity
            {
                Id = merchantB,
                Name = "Store B",
                Category = MerchantCategory.Undefined
            });
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, proposalId));
            await context.SaveChangesAsync();
        }

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        await using (var context = CreateContext())
        {
            var service = new PrecategorizedCommodityService(CreateRepository(context), cache);
            var result = await service.ConfirmAsync(proposalId, CancellationToken.None);

            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal(0, result.UpdatedCommodities);
        }

        await using (var context = CreateContext())
        {
            Assert.Equal(PrecategorizedCommodityStatus.Applied,
                (await context.PrecategorizedCommodities.FindAsync(proposalId))!.Status);
        }
    }

    [Fact]
    public async Task Decline_removes_row_keeps_commodities_and_candidate_becomes_available_again()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateReceipt(userA, merchantA, ("Молоко", null)));
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, proposalId));
            await context.SaveChangesAsync();
        }

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var service = new PrecategorizedCommodityService(repository, cache);

            Assert.Equal(PrecategorizedOperationOutcome.Applied, await service.DeclineAsync(proposalId, CancellationToken.None));
            Assert.Empty(await repository.GetPageAsync(10, 0, null, CancellationToken.None));
            // C4/FR-5.4: после отклонения кандидат снова попадает в выборку фона.
            Assert.Contains(await repository.GetCandidatesAsync(null, 10, CancellationToken.None),
                c => c == new CandidateCursor("Молоко", merchantA));
            Assert.Equal(0, cache.Count);
        }

        await using (var context = CreateContext())
        {
            var commodity = Assert.Single(await context.Commodities.ToListAsync());
            Assert.Null(commodity.CategoryId); // commodities не изменились
            Assert.Empty(await context.PrecategorizedCommodities.ToListAsync());
        }
    }

    [Fact]
    public async Task Repository_sees_all_users_when_owner_scope_is_set()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantA, ("Молоко", null), ("Хлеб", null)),
                CreateReceipt(userB, merchantB, ("Молоко", (int)CommodityCategory.Dairy), ("Кефир", null)));
        }

        await using (var context = CreateContext())
        {
            context.SetCurrentUser(userA);
            var repository = CreateRepository(context);

            var candidates = await repository.GetCandidatesAsync(null, 50, CancellationToken.None);
            Assert.Contains(candidates, c => c == new CandidateCursor("Молоко", merchantA));
            Assert.Contains(candidates, c => c == new CandidateCursor("Кефир", merchantB));

            // Глобальный existing-шаг видит категорию товара другого пользователя.
            Assert.Equal(CommodityCategory.Dairy,
                await repository.GetKnownCategoryByNameAsync("Молоко", CancellationToken.None));
        }
    }

    [Fact]
    public async Task Repeated_try_add_after_restart_does_not_create_duplicate()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var candidate = new CandidateCursor("Молоко", merchantA);

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateReceipt(userA, merchantA, ("Молоко", null)));
        }

        await using (var context = CreateContext())
        {
            Assert.True(await CreateRepository(context)
                .TryAddProposalAsync(candidate, CommodityCategory.Dairy, CancellationToken.None));
        }

        // «Рестарт» — новый scope/DbContext: unique (name, merchant_id) защищает от дубля (NFR-4).
        await using (var context = CreateContext())
        {
            Assert.False(await CreateRepository(context)
                .TryAddProposalAsync(candidate, CommodityCategory.Food, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var stored = Assert.Single(await context.PrecategorizedCommodities.ToListAsync());
            Assert.Equal((int)CommodityCategory.Dairy, stored.ProposedCategoryId);
        }
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    private ReceiptDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention();

        return new ReceiptDbContext(builder.Options);
    }

    private static PrecategorizedCommodityRepository CreateRepository(ReceiptDbContext context)
        => new(context, TimeProvider.System, NullLogger<PrecategorizedCommodityRepository>.Instance);

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.PrecategorizedCommodities.ExecuteDeleteAsync();
        await context.Commodities.ExecuteDeleteAsync();
        await context.Receipts.ExecuteDeleteAsync();
        await context.Merchants.ExecuteDeleteAsync();
    }

    private static async Task SeedAsync(ReceiptDbContext context, params ReceiptEntity[] receipts)
    {
        foreach (var merchantId in receipts.Select(r => r.MerchantId).Distinct())
        {
            context.Merchants.Add(new MerchantEntity
            {
                Id = merchantId,
                Name = $"Store {merchantId:N}",
                Category = MerchantCategory.Undefined
            });
        }

        context.Receipts.AddRange(receipts);
        await context.SaveChangesAsync();
    }

    private static ReceiptEntity CreateReceipt(
        Guid userId,
        Guid merchantId,
        params (string Name, int? CategoryId)[] items)
    {
        var receiptId = Guid.NewGuid();
        return new ReceiptEntity
        {
            Id = receiptId,
            UserId = userId,
            MerchantId = merchantId,
            ExternalId = $"external-{receiptId:N}",
            TotalAmount = 100m,
            PurchasedAt = DateTime.UtcNow,
            Items = items.Select(item => new CommodityEntity
            {
                Id = Guid.NewGuid(),
                ReceiptId = receiptId,
                Name = item.Name,
                Quantity = 1m,
                UnitPrice = 10m,
                Nds = 0,
                NdsSum = 0m,
                CategoryId = item.CategoryId,
                CategoryName = item.CategoryId is null or 0
                    ? null
                    : CommodityCategoryHelper.GetDisplayName((CommodityCategory)item.CategoryId.Value)
            }).ToList()
        };
    }

    private static PrecategorizedCommodityEntity Proposal(
        string name,
        Guid merchantId,
        CommodityCategory category,
        PrecategorizedCommodityStatus status,
        Guid id)
        => new()
        {
            Id = id,
            Name = name,
            MerchantId = merchantId,
            ProposedCategoryId = (int)category,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
