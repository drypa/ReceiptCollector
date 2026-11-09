using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Сквозные тесты админского workflow предкатегоризации (шаг 9 плана, D8/D10, FR-5.4, FR-6, NFR-4/NFR-5):
/// реальные <see cref="PrecategorizedCommodityRepository"/> + <see cref="PrecategorizedCommodityService"/>
/// + in-memory кэш на Postgres-контейнере. T-06 (ADR-033 п. 5): с реальным
/// <see cref="CategorizationDecisionRecorder"/> confirm пишет событие журнала
/// «предложено = исходное предложение строки, принято = применённая категория».
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
                // ADR-034 п. 2: частичный уникальный индекс ux_..._normalized_proposed
                // запрещает две proposed-строки с одним очищенным ключом глобально
                // (FR-3.4 ADR-027 по очищенному ключу), поэтому «сестринской» строке
                // задан иной ключ — сид с двумя proposed «молоко» новый индекс не
                // пропускает. Тест проверяет механизм удаления sibling'ов по сырому
                // имени (переключение на ключ — T-04), а не валидность исторического
                // сочетания двух proposed.
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, siblingId, normalizedName: "sibling"));
            await context.SaveChangesAsync();
        }

        var cache = new InMemoryCommodityCategoryCache(maxSize: 100);
        await using (var context = CreateContext())
        {
            var service = CreateService(context, cache);
            var result = await service.ConfirmAsync(proposalId, Guid.NewGuid(), CancellationToken.None);

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
            var service = CreateService(context, cache);
            var result = await service.ConfirmAsync(proposalId, Guid.NewGuid(), CancellationToken.None);

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
    public async Task Confirm_after_update_category_records_event_with_original_suggested_and_changed_final()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateReceipt(userA, merchantA, ("Молоко", null)));
            // Фон создал строку с исходным предложением Dairy (original = proposed, D1).
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, proposalId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var service = CreateService(context, new InMemoryCommodityCategoryCache(maxSize: 100));

            // PUT предложенной категории: админ исправил Dairy → Food (original не трогается).
            Assert.Equal(PrecategorizedOperationOutcome.Applied,
                await service.UpdateCategoryAsync(proposalId, CommodityCategory.Food, CancellationToken.None));

            var result = await service.ConfirmAsync(proposalId, adminId, CancellationToken.None);
            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.UpdatedCommodities);
        }

        await using (var context = CreateContext())
        {
            // ADR-033 п. 5 (D1): suggested = исходная, final = изменённая категория.
            var decision = Assert.Single(await context.CategorizationDecisions.ToListAsync());
            Assert.Equal("Молоко", decision.CommodityName);
            Assert.Equal("молоко", decision.NormalizedName);
            Assert.Equal(CategorizationDecisionSources.Precategorized, decision.SuggestionSource);
            Assert.Equal((int)CommodityCategory.Dairy, decision.SuggestedCategoryId);
            Assert.Equal((int)CommodityCategory.Food, decision.FinalCategoryId);
            Assert.Equal(adminId, decision.AcceptedBy);
            Assert.True(decision.AcceptedByAdmin);
        }
    }

    [Fact]
    public async Task Confirm_without_update_records_event_with_suggested_equal_final()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateReceipt(userA, merchantA, ("Молоко", null)));
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, proposalId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var service = CreateService(context, new InMemoryCommodityCategoryCache(maxSize: 100));
            var result = await service.ConfirmAsync(proposalId, adminId, CancellationToken.None);
            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
        }

        await using (var context = CreateContext())
        {
            // Согласие (FR-3): подтвердили предложение без правок — suggested == final.
            var decision = Assert.Single(await context.CategorizationDecisions.ToListAsync());
            Assert.Equal(CategorizationDecisionSources.Precategorized, decision.SuggestionSource);
            Assert.Equal((int)CommodityCategory.Dairy, decision.SuggestedCategoryId);
            Assert.Equal(decision.SuggestedCategoryId, decision.FinalCategoryId);
            Assert.Equal(adminId, decision.AcceptedBy);
            Assert.True(decision.AcceptedByAdmin);
        }
    }

    [Fact]
    public async Task Confirm_with_zero_updated_commodities_still_records_event()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            // Товар уже категоризован — confirm не обновит ни одной позиции...
            await SeedAsync(context, CreateReceipt(userA, merchantA, ("Молоко", (int)CommodityCategory.Food)));
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, proposalId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var service = CreateService(context, new InMemoryCommodityCategoryCache(maxSize: 100));
            var result = await service.ConfirmAsync(proposalId, adminId, CancellationToken.None);
            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal(0, result.UpdatedCommodities);
        }

        await using (var context = CreateContext())
        {
            // ...но решение состоялось — событие пишется (ADR-033 п. 5, «Явно зафиксированные семантики»).
            var decision = Assert.Single(await context.CategorizationDecisions.ToListAsync());
            Assert.Equal("Молоко", decision.CommodityName);
            Assert.Equal((int)CommodityCategory.Dairy, decision.SuggestedCategoryId);
            Assert.Equal((int)CommodityCategory.Dairy, decision.FinalCategoryId);
            Assert.Equal(adminId, decision.AcceptedBy);
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
            var service = CreateService(context, cache);

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
            // Отклонение — удаления proposal: предложение не подтверждено, событий журнала нет (ADR-033 п. 5).
            Assert.Empty(await context.CategorizationDecisions.ToListAsync());
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

    /// <summary>
    /// Реальный recorder поверх того же scope/DbContext (прецедент боевого DI): событие журнала
    /// пишется одним SaveChanges после commit транзакции confirm (NFR-3/C1).
    /// </summary>
    private static CategorizationDecisionRecorder CreateRecorder(ReceiptDbContext context)
        => new(
            new CategorizationDecisionRepository(context),
            TimeProvider.System,
            NullLogger<CategorizationDecisionRecorder>.Instance);

    private static PrecategorizedCommodityService CreateService(ReceiptDbContext context, ICommodityCategoryCache cache)
        => new(
            CreateRepository(context),
            cache,
            CreateRecorder(context),
            NullLogger<PrecategorizedCommodityService>.Instance);

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.CategorizationDecisions.ExecuteDeleteAsync();
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
        Guid id,
        CommodityCategory? original = null,
        string? normalizedName = null)
        => new()
        {
            Id = id,
            Name = name,
            // ADR-034 п. 2 (B1): у каждой строки очереди есть очищенный ключ.
            // normalizedName задаётся явно только там, где «честный» ключ нарушил бы
            // частичный уникальный индекс proposed (см. комментарий в тесте Confirm).
            NormalizedName = normalizedName ?? CommodityNameNormalizer.NormalizeName(name),
            MerchantId = merchantId,
            ProposedCategoryId = (int)category,
            // ADR-033 п. 1 (D1): фон создаёт строку с исходным предложением = предложению.
            OriginalProposedCategoryId = (int)(original ?? category),
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
}
