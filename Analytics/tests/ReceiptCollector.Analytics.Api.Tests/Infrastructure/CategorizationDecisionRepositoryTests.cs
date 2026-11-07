using Microsoft.EntityFrameworkCore;
using Npgsql;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты персистенции журнала категоризации (T-02, ADR-033 п. 1–2, решения B1/C1).
/// Схема создаётся из EF-модели (<c>EnsureCreatedAsync</c>) — как в остальных Postgres-тестах;
/// поэтому CHECK и индексы объявлены и в EF-конфигурации, и в SQL-миграции T-01.
/// </summary>
public sealed class CategorizationDecisionRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public CategorizationDecisionRepositoryTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task AddRangeAsync_inserts_batch_with_all_columns_and_created_at()
    {
        await ClearDatabaseAsync();

        var firstAcceptedBy = Guid.NewGuid();
        var firstCreatedAt = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

        var decisions = new[]
        {
            CreateDecision(
                commodityName: "Молоко 3,2%",
                normalizedName: "молоко 3,2%",
                source: "ai",
                suggested: CommodityCategory.Dairy,
                final: CommodityCategory.Beverages,
                acceptedBy: firstAcceptedBy,
                acceptedByAdmin: true,
                createdAt: firstCreatedAt),
            CreateDecision(
                commodityName: "Хлеб",
                normalizedName: "хлеб",
                source: "precategorized",
                suggested: CommodityCategory.Bakery,
                final: CommodityCategory.Bakery,
                acceptedByAdmin: false,
                createdAt: firstCreatedAt.AddMinutes(1)),
            CreateDecision(
                commodityName: "Ноутбук",
                normalizedName: "ноутбук",
                source: "merchant-default",
                suggested: CommodityCategory.Electronics,
                final: CommodityCategory.Undefined,
                createdAt: firstCreatedAt.AddMinutes(2))
        };

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            await repository.AddRangeAsync(decisions);
        }

        await using (var context = CreateContext())
        {
            var rows = await context.CategorizationDecisions
                .OrderBy(d => d.CreatedAt)
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            // Id — клиентский Guid события, round-trip без подмены хранилищем.
            Assert.Equal(decisions.Select(d => d.Id), rows.Select(r => r.Id));

            var first = rows[0];
            Assert.Equal("Молоко 3,2%", first.CommodityName);
            Assert.Equal("молоко 3,2%", first.NormalizedName);
            Assert.Equal("ai", first.SuggestionSource);
            Assert.Equal((int)CommodityCategory.Dairy, first.SuggestedCategoryId);
            Assert.Equal((int)CommodityCategory.Beverages, first.FinalCategoryId);
            Assert.Equal(firstAcceptedBy, first.AcceptedBy);
            Assert.True(first.AcceptedByAdmin);
            Assert.Equal(firstCreatedAt, first.CreatedAt);

            Assert.Equal("precategorized", rows[1].SuggestionSource);
            Assert.False(rows[1].AcceptedByAdmin);
            Assert.Equal((int)CommodityCategory.Bakery, rows[1].FinalCategoryId);

            Assert.Equal("merchant-default", rows[2].SuggestionSource);
            Assert.Equal((int)CommodityCategory.Undefined, rows[2].FinalCategoryId);
        }
    }

    [Fact]
    public async Task AddRangeAsync_does_not_deduplicate_repeated_combinations()
    {
        await ClearDatabaseAsync();

        var createdAt = new DateTimeOffset(2026, 11, 6, 13, 0, 0, TimeSpan.Zero);

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            // FR-8: хранение сырое, дедупликация — только на чтении. Одинаковое сочетание
            // двумя событиями остаётся двумя строками.
            await repository.AddRangeAsync(
            [
                CreateDecision(createdAt: createdAt),
                CreateDecision(createdAt: createdAt.AddSeconds(1))
            ]);
        }

        await using (var context = CreateContext())
        {
            Assert.Equal(2, await context.CategorizationDecisions.CountAsync());
        }
    }

    [Fact]
    public async Task Model_creates_check_constraints_and_all_three_indexes()
    {
        await ClearDatabaseAsync();

        await using var context = CreateContext();

        var constraints = await context.Database.SqlQueryRaw<ConstraintRow>(
            """
            SELECT conname AS constraint_name, pg_get_constraintdef(oid) AS definition
            FROM pg_constraint
            WHERE conrelid = 'categorization_decisions'::regclass
              AND contype = 'c'
            """).ToListAsync();

        var suggestedConstraint = Assert.Single(
            constraints,
            c => c.ConstraintName == "ck_categorization_decisions_suggested_not_undefined");
        Assert.Contains("suggested_category_id <> 0", suggestedConstraint.Definition);

        var finalConstraint = Assert.Single(
            constraints,
            c => c.ConstraintName == "ck_categorization_decisions_final_non_negative");
        Assert.Contains("final_category_id >= 0", finalConstraint.Definition);

        var indexes = await context.Database.SqlQueryRaw<IndexRow>(
            """
            SELECT indexname AS index_name, indexdef AS index_definition
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename = 'categorization_decisions'
            """).ToListAsync();

        var combination = Assert.Single(
            indexes,
            i => i.IndexName == "ix_categorization_decisions_combination");
        Assert.Contains("created_at DESC, id DESC", combination.IndexDefinition);

        var byCreatedAt = Assert.Single(
            indexes,
            i => i.IndexName == "ix_categorization_decisions_created_at");
        Assert.Contains("(created_at DESC)", byCreatedAt.IndexDefinition);

        var mismatch = Assert.Single(
            indexes,
            i => i.IndexName == "ix_categorization_decisions_mismatch");
        Assert.Contains("WHERE (suggested_category_id <> final_category_id)", mismatch.IndexDefinition);

        var originalProposed = Assert.Single(
            await context.Database.SqlQueryRaw<ColumnRow>(
                """
                SELECT is_nullable AS is_nullable
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'precategorized_commodities'
                  AND column_name = 'original_proposed_category_id'
                """).ToListAsync());
        Assert.Equal("NO", originalProposed.IsNullable);
    }

    [Fact]
    public async Task AddRangeAsync_rejects_undefined_suggested_category_by_check_constraint()
    {
        await ClearDatabaseAsync();

        await using var context = CreateContext();
        var repository = CreateRepository(context);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            repository.AddRangeAsync(
            [
                CreateDecision(suggested: CommodityCategory.Undefined, final: CommodityCategory.Dairy)
            ]));

        // PostgreSQL SQLSTATE 23514 = check_violation; проверяем именно целевой констрейнт.
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
        Assert.Equal("ck_categorization_decisions_suggested_not_undefined", postgresException.ConstraintName);
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

    private static CategorizationDecisionRepository CreateRepository(ReceiptDbContext context)
        => new(context);

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.CategorizationDecisions.ExecuteDeleteAsync();
    }

    private static CategorizationDecision CreateDecision(
        string commodityName = "Молоко",
        string normalizedName = "молоко",
        string source = "ai",
        CommodityCategory suggested = CommodityCategory.Dairy,
        CommodityCategory final = CommodityCategory.Bakery,
        Guid? acceptedBy = null,
        bool acceptedByAdmin = true,
        DateTimeOffset? createdAt = null)
        => new(
            Guid.NewGuid(),
            commodityName,
            normalizedName,
            source,
            suggested,
            final,
            acceptedBy ?? Guid.NewGuid(),
            acceptedByAdmin,
            createdAt ?? DateTimeOffset.UtcNow);

    private sealed class ConstraintRow
    {
        public string ConstraintName { get; set; } = string.Empty;

        public string Definition { get; set; } = string.Empty;
    }

    private sealed class IndexRow
    {
        public string IndexName { get; set; } = string.Empty;

        public string IndexDefinition { get; set; } = string.Empty;
    }

    private sealed class ColumnRow
    {
        public string IsNullable { get; set; } = string.Empty;
    }
}
