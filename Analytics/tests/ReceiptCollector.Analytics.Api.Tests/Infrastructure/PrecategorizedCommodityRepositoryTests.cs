using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты EF-репозитория очереди предкатегоризации (шаг 2 плана, ADR-027 п.2–п.4, D1/D8/D10/D14).
/// Схема создаётся из EF-модели (<c>EnsureCreatedAsync</c>) — как в остальных Postgres-тестах,
/// поэтому unique-индекс и FK объявлены и в конфигурации, и в SQL-миграции.
/// </summary>
public sealed class PrecategorizedCommodityRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public PrecategorizedCommodityRepositoryTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task GetCandidatesAsync_returns_pairs_of_all_users_with_null_and_zero_categories()
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
                CreateReceipt(userA, merchantA, ("Молоко", null), ("Хлеб", 0), ("Сыр", (int)CommodityCategory.Dairy)),
                CreateReceipt(userB, merchantB, ("Молоко", null), ("Кефир", (int)CommodityCategory.Undefined)));
        }

        await using (var context = CreateContext())
        {
            // D8/NFR-5: owner-scope выставлен, но фон обязан видеть товары всех пользователей.
            context.SetCurrentUser(userA);

            var repository = CreateRepository(context);
            var candidates = await repository.GetCandidatesAsync(null, 50, CancellationToken.None);

            var expected = new[]
            {
                new CandidateCursor("Кефир", merchantB),
                new CandidateCursor("Молоко", merchantA),
                new CandidateCursor("Молоко", merchantB),
                new CandidateCursor("Хлеб", merchantA)
            };

            Assert.Equal(expected.Length, candidates.Count);
            Assert.All(expected, candidate => Assert.Contains(candidate, candidates));
        }
    }

    [Fact]
    public async Task GetCandidatesAsync_excludes_pairs_present_in_queue_in_any_status()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var userA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantA, ("Молоко", null), ("Хлеб", null), ("Кефир", null)));
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantB, ("Молоко", (int)CommodityCategory.Dairy)));

            // FR-3.2 приоритетнее FR-3.3: пара уже в очереди — исключена, несмотря на известную категорию.
            context.PrecategorizedCommodities.AddRange(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed),
                Proposal("Хлеб", merchantA, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Applied));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var candidates = await repository.GetCandidatesAsync(null, 50, CancellationToken.None);

            var single = Assert.Single(candidates);
            Assert.Equal(new CandidateCursor("Кефир", merchantA), single);
        }
    }

    [Fact]
    public async Task GetCandidatesAsync_excludes_name_with_proposed_row_at_another_merchant()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var merchantC = Guid.NewGuid();
        var userA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantA, ("Молоко", null), ("Хлеб", null), ("Сыр", null)));
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantB, ("Молоко", null), ("Сыр", null)));

            context.Merchants.Add(CreateMerchant(merchantC, "Store C"));

            // FR-3.4: «Молоко» уже предложено другому магазину → пара (Молоко, merchantA) исключена.
            // «Сыр» применён у другого магазина (applied) → это НЕ исключает кандидата (только proposed).
            context.PrecategorizedCommodities.AddRange(
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed),
                Proposal("Сыр", merchantC, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Applied));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var candidates = await repository.GetCandidatesAsync(null, 50, CancellationToken.None);

            Assert.DoesNotContain(candidates, c => c.Name == "Молоко");
            Assert.Contains(candidates, c => c == new CandidateCursor("Сыр", merchantA));
            Assert.Contains(candidates, c => c == new CandidateCursor("Хлеб", merchantA));
        }
    }

    [Fact]
    public async Task GetCandidatesAsync_orders_deterministically_and_continues_after_cursor()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var userA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantA, ("Апельсин", null), ("Банан", null), ("Вишня", null)),
                CreateReceipt(userA, merchantB, ("Банан", null), ("Вишня", null)));
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            var first = (await repository.GetCandidatesAsync(null, 2, CancellationToken.None)).ToList();
            Assert.Equal(2, first.Count);
            Assert.Equal(new CandidateCursor("Апельсин", merchantA), first[0]);
            Assert.Equal("Банан", first[1].Name);

            var second = (await repository.GetCandidatesAsync(first[^1], 2, CancellationToken.None)).ToList();
            Assert.Equal(2, second.Count);
            Assert.Equal("Банан", second[0].Name);
            Assert.True(second[0].MerchantId != first[1].MerchantId || second[0].Name != first[1].Name);
            Assert.Equal("Вишня", second[1].Name);

            var third = (await repository.GetCandidatesAsync(second[^1], 2, CancellationToken.None)).ToList();
            Assert.Single(third);
            Assert.Equal("Вишня", third[0].Name);
            Assert.NotEqual(second[1].MerchantId, third[0].MerchantId);
        }
    }

    [Fact]
    public async Task TryAddProposalAsync_inserts_once_and_returns_false_on_unique_conflict()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var userA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateReceipt(userA, merchantA, ("Молоко", null)));
        }

        var candidate = new CandidateCursor("Молоко", merchantA);

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.True(await repository.TryAddProposalAsync(candidate, CommodityCategory.Dairy, CancellationToken.None));
            // FR-4.5: повторная вставка — no-op без исключения (unique (name, merchant_id)).
            Assert.False(await repository.TryAddProposalAsync(candidate, CommodityCategory.Food, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.SingleAsync();
            Assert.Equal(CommodityCategory.Dairy, (CommodityCategory)stored.ProposedCategoryId);
            Assert.Equal(PrecategorizedCommodityStatus.Proposed, stored.Status);
        }
    }

    [Fact]
    public async Task TryAddProposalAsync_skips_name_longer_than_schema_limit()
    {
        await ClearDatabaseAsync();

        await using var context = CreateContext();
        var repository = CreateRepository(context);

        var tooLong = new CandidateCursor(new string('x', 257), Guid.NewGuid());
        Assert.False(await repository.TryAddProposalAsync(tooLong, CommodityCategory.Food, CancellationToken.None));
        Assert.Empty(await context.PrecategorizedCommodities.ToListAsync());
    }

    [Fact]
    public async Task GetPageAsync_returns_only_proposed_with_search_paging_and_count()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var merchantC = Guid.NewGuid();
        var merchantD = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateMerchant(merchantA, "Пятёрочка"),
                CreateMerchant(merchantB, "Магнит"),
                CreateMerchant(merchantC, "Лента"),
                CreateMerchant(merchantD, "Ашан"));

            context.PrecategorizedCommodities.AddRange(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, DateTimeOffset.UtcNow.AddMinutes(-1)),
                Proposal("Хлеб", merchantB, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, DateTimeOffset.UtcNow.AddMinutes(-2)),
                Proposal("Кефир", merchantC, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, DateTimeOffset.UtcNow.AddMinutes(-3)),
                Proposal("Молоко", merchantD, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Applied, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            var all = (await repository.GetPageAsync(2, 0, null, CancellationToken.None)).ToList();
            Assert.Equal(2, all.Count);
            Assert.Equal(3, await repository.GetPageCountAsync(null, CancellationToken.None));
            // Сортировка created_at DESC: «Молоко» свежее «Хлеба».
            Assert.Equal("Молоко", all[0].Name);
            Assert.Equal("Пятёрочка", all[0].MerchantName);
            Assert.Equal("Хлеб", all[1].Name);

            var secondPage = (await repository.GetPageAsync(2, 2, null, CancellationToken.None)).ToList();
            Assert.Single(secondPage);
            Assert.Equal("Кефир", secondPage[0].Name);

            var byName = (await repository.GetPageAsync(10, 0, "мол", CancellationToken.None)).ToList();
            Assert.Single(byName);
            Assert.Equal("Молоко", byName[0].Name);
            Assert.Equal(1, await repository.GetPageCountAsync("мол", CancellationToken.None));

            var byMerchant = (await repository.GetPageAsync(10, 0, "магн", CancellationToken.None)).ToList();
            Assert.Single(byMerchant);
            Assert.Equal("Хлеб", byMerchant[0].Name);
            Assert.Equal(1, await repository.GetPageCountAsync("магн", CancellationToken.None));
        }
    }

    [Fact]
    public async Task GetCategoriesByExactNamesAsync_returns_latest_row_and_skips_invalid_categories()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"), CreateMerchant(merchantB, "B"));

            context.PrecategorizedCommodities.AddRange(
                Proposal("Молоко", merchantA, CommodityCategory.Food, PrecategorizedCommodityStatus.Applied, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)),
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, DateTimeOffset.UtcNow.AddHours(-1)),
                Proposal("Хлеб", merchantA, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Proposed, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();

            // Невалидный код категории (вне реестра) — должен быть отброшен (FR-9.2).
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO precategorized_commodities
                    (id, name, merchant_id, proposed_category_id, status, created_at)
                VALUES
                    ({Guid.NewGuid()}, {"Кефир"}, {merchantA}, {9999}, {(short)0}, {DateTimeOffset.UtcNow})
                """);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var categories = await repository.GetCategoriesByExactNamesAsync(
                new[] { "Молоко", "Хлеб", "Кефир", "Неизвестно" },
                CancellationToken.None);

            Assert.Equal(2, categories.Count);
            // Самая свежая строка (proposed молоко) выигрывает.
            Assert.Equal(CommodityCategory.Dairy, categories["Молоко"]);
            Assert.Equal(CommodityCategory.Bakery, categories["Хлеб"]);
            Assert.False(categories.ContainsKey("Кефир"));
            Assert.False(categories.ContainsKey("Неизвестно"));
        }
    }

    [Fact]
    public async Task DeleteProposedAsync_removes_proposed_and_conflicts_on_applied()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var proposedId = Guid.NewGuid();
        var appliedId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"), CreateMerchant(merchantB, "B"));
            context.PrecategorizedCommodities.AddRange(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: proposedId),
                Proposal("Хлеб", merchantB, CommodityCategory.Bakery, PrecategorizedCommodityStatus.Applied, id: appliedId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            Assert.Equal(PrecategorizedOperationOutcome.Applied, await repository.DeleteProposedAsync(proposedId, CancellationToken.None));
            Assert.Equal(PrecategorizedOperationOutcome.Conflict, await repository.DeleteProposedAsync(appliedId, CancellationToken.None));
            Assert.Equal(PrecategorizedOperationOutcome.NotFound, await repository.DeleteProposedAsync(Guid.NewGuid(), CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            Assert.Null(await context.PrecategorizedCommodities.FindAsync(proposedId));
            Assert.NotNull(await context.PrecategorizedCommodities.FindAsync(appliedId));
        }
    }

    [Fact]
    public async Task GetKnownCategoryByNameAsync_uses_global_exact_name_and_ignores_empty_categories()
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
                CreateReceipt(userA, merchantA, ("Молоко", null), ("Хлеб", 0)),
                CreateReceipt(userB, merchantB, ("Молоко", (int)CommodityCategory.Dairy), ("Хлеб", null)));
        }

        await using (var context = CreateContext())
        {
            context.SetCurrentUser(userA);
            var repository = CreateRepository(context);

            Assert.Equal(CommodityCategory.Dairy, await repository.GetKnownCategoryByNameAsync("Молоко", CancellationToken.None));
            Assert.Null(await repository.GetKnownCategoryByNameAsync("Хлеб", CancellationToken.None));
            Assert.Null(await repository.GetKnownCategoryByNameAsync("Молоко 1%", CancellationToken.None));
        }
    }

    [Fact]
    public async Task ConfirmAsync_updates_commodities_of_all_users_and_removes_sibling_proposals()
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
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: proposalId),
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: siblingId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var result = await repository.ConfirmAsync(proposalId, CommodityCategory.Dairy, CancellationToken.None);

            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal("Молоко", result.Name);
            Assert.Equal(CommodityCategory.Dairy, result.Category);
            Assert.Equal(2, result.UpdatedCommodities);

            // D10: повторное подтверждение — Conflict.
            var repeated = await repository.ConfirmAsync(proposalId, CommodityCategory.Dairy, CancellationToken.None);
            Assert.Equal(PrecategorizedOperationOutcome.Conflict, repeated.Outcome);
            Assert.Equal(PrecategorizedOperationOutcome.NotFound,
                (await repository.ConfirmAsync(Guid.NewGuid(), CommodityCategory.Dairy, CancellationToken.None)).Outcome);
        }

        await using (var context = CreateContext())
        {
            var commodities = await context.Commodities.OrderBy(c => c.Name).ToListAsync();
            var milk = commodities.Where(c => c.Name == "Молоко").ToList();

            Assert.Equal(3, milk.Count);
            Assert.Equal(2, milk.Count(c => c.CategoryId == (int)CommodityCategory.Dairy));
            Assert.All(milk.Where(c => c.CategoryId == (int)CommodityCategory.Dairy),
                c => Assert.Equal("Молочные продукты", c.CategoryName));
            Assert.Single(milk, c => c.CategoryId == (int)CommodityCategory.Food); // уже категоризированный не тронут

            var similar = Assert.Single(commodities, c => c.Name == "Молоко 1%");
            Assert.Null(similar.CategoryId); // другое наименование не тронуто

            var storedProposal = await context.PrecategorizedCommodities.FindAsync(proposalId);
            Assert.NotNull(storedProposal);
            Assert.Equal(PrecategorizedCommodityStatus.Applied, storedProposal!.Status);
            Assert.NotNull(storedProposal.AppliedAt);

            // «Сестринская» proposed-строка удалена (D10).
            Assert.Null(await context.PrecategorizedCommodities.FindAsync(siblingId));
        }
    }

    [Fact]
    public async Task ConfirmAsync_marks_applied_with_zero_updated_commodities()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantA, ("Молоко", (int)CommodityCategory.Food)));

            context.Merchants.Add(CreateMerchant(merchantB, "Store B"));
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: proposalId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var result = await repository.ConfirmAsync(proposalId, CommodityCategory.Dairy, CancellationToken.None);

            // FR-6.6/D10: строка становится applied даже при 0 обновлённых товаров.
            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal(0, result.UpdatedCommodities);
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.FindAsync(proposalId);
            Assert.Equal(PrecategorizedCommodityStatus.Applied, stored!.Status);
        }
    }

    [Fact]
    public async Task UpdateProposedCategoryAsync_updates_proposed_and_conflicts_on_applied()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();
        var proposedId = Guid.NewGuid();
        var appliedId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"), CreateMerchant(merchantB, "B"));
            context.PrecategorizedCommodities.AddRange(
                Proposal("Молоко", merchantA, CommodityCategory.Food, PrecategorizedCommodityStatus.Proposed, id: proposedId),
                Proposal("Хлеб", merchantB, CommodityCategory.Food, PrecategorizedCommodityStatus.Applied, id: appliedId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            Assert.Equal(PrecategorizedOperationOutcome.Applied,
                await repository.UpdateProposedCategoryAsync(proposedId, CommodityCategory.Dairy, CancellationToken.None));
            Assert.Equal(PrecategorizedOperationOutcome.Conflict,
                await repository.UpdateProposedCategoryAsync(appliedId, CommodityCategory.Dairy, CancellationToken.None));
            Assert.Equal(PrecategorizedOperationOutcome.NotFound,
                await repository.UpdateProposedCategoryAsync(Guid.NewGuid(), CommodityCategory.Dairy, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                repository.UpdateProposedCategoryAsync(proposedId, CommodityCategory.Undefined, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.FindAsync(proposedId);
            Assert.Equal((int)CommodityCategory.Dairy, stored!.ProposedCategoryId);
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
        foreach (var receipt in receipts)
        {
            if (await context.Merchants.FindAsync(receipt.MerchantId) is null)
            {
                context.Merchants.Add(CreateMerchant(receipt.MerchantId, $"Store {receipt.MerchantId:N}"));
            }
        }

        context.Receipts.AddRange(receipts);
        await context.SaveChangesAsync();
    }

    private static async Task SeedAsync(ReceiptDbContext context, params MerchantEntity[] merchants)
    {
        context.Merchants.AddRange(merchants);
        await context.SaveChangesAsync();
    }

    private static MerchantEntity CreateMerchant(Guid id, string name)
        => new() { Id = id, Name = name, Category = MerchantCategory.Undefined };

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
        DateTimeOffset? createdAt = null,
        DateTimeOffset? appliedAt = null,
        Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            MerchantId = merchantId,
            ProposedCategoryId = (int)category,
            Status = status,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            AppliedAt = appliedAt
        };
}
