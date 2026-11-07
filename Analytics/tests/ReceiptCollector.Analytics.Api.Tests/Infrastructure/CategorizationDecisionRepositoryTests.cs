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

    // ===== T-07: дедуп anti-join (E1), фильтры, порядок и Total на реальном PostgreSQL =====

    /// <summary>
    /// Известный риск T-07 (прецедент-предупреждение ADR-027): tie-break «свежести»
    /// <c>(created_at, id)</c> требует сравнения <c>Guid</c> в EF-трансляции Npgsql. Кейс
    /// выполняется на реальном PostgreSQL: anti-join обязан отработать, а при равном
    /// <c>created_at</c> «последним» признаётся событие с большим uuid — таким, каким его
    /// считает сама БД (anti-join и <c>ORDER BY</c> исполняются одним движком, внутри него
    /// порядок детерминирован). Id специально подобраны так, чтобы .NET и PostgreSQL сортировали
    /// их одинаково (<c>Data1 = 0</c>, различаются только завершающие байты node): ожидание
    /// читается на любом движке. Это удобство теста, а не свойство реализации — кросс-движковая
    /// эквивалентность порядков <c>Guid</c> не предполагается и не нужна.
    /// </summary>
    [Fact]
    public async Task GetCombinationsAsync_translates_guid_tie_break_and_keeps_greater_id_event()
    {
        await ClearDatabaseAsync();

        // Id порядка согласуют оба движка (.NET и PostgreSQL): Data1 = 0, различие в node —
        // см. док теста.
        var smallerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var greaterId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var firstAcceptor = Guid.NewGuid();
        var lastAcceptor = Guid.NewGuid();
        var sameInstant = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            // Одно сочетание, два события в один created_at: «последнее» — с большим id.
            await repository.AddRangeAsync(
            [
                CreateDecision(
                    id: smallerId, acceptedBy: firstAcceptor, acceptedByAdmin: false, createdAt: sameInstant),
                CreateDecision(
                    id: greaterId, acceptedBy: lastAcceptor, acceptedByAdmin: true, createdAt: sameInstant)
            ]);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            var rows = await repository.GetCombinationsAsync(Query());

            var row = Assert.Single(rows);
            Assert.Equal(greaterId, row.Id);
            Assert.Equal(lastAcceptor, row.AcceptedBy);
            Assert.True(row.AcceptedByAdmin);
            Assert.Equal(sameInstant, row.CreatedAt);
            Assert.Equal(1, await repository.CountCombinationsAsync(Query()));
        }
    }

    [Fact]
    public async Task GetCombinationsAsync_search_matches_either_name_case_insensitively()
    {
        await ClearDatabaseAsync();

        var baseTime = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            await repository.AddRangeAsync(
            [
                // Термин («КЕФИР») отличается по регистру и попадает в commodity_name.
                CreateDecision("Кефир Ферма", "ферма продукт", createdAt: baseTime),
                // Термин попадает только в normalized_name.
                CreateDecision("Простоквашино", "кефир детский", createdAt: baseTime.AddMinutes(1)),
                // Не попадает ни туда, ни туда.
                CreateDecision("Молоко", "молоко", createdAt: baseTime.AddMinutes(2))
            ]);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            // Trim пробелов вокруг термина + ILike '%term%' без учёта регистра по обоим именам.
            var rows = await repository.GetCombinationsAsync(Query(search: "  КЕФИР  "));
            var total = await repository.CountCombinationsAsync(Query(search: "  КЕФИР  "));

            Assert.Equal(
                new[] { "Простоквашино", "Кефир Ферма" },
                rows.Select(r => r.CommodityName).ToArray());
            Assert.Equal(2, total);
        }
    }

    [Fact]
    public async Task GetCombinationsAsync_orders_by_created_at_desc_then_id_desc()
    {
        await ClearDatabaseAsync();

        // Id порядка согласуют оба движка (.NET и PostgreSQL) — см. док tie-break-теста.
        var smallerId = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var greaterId = Guid.Parse("00000000-0000-0000-0000-00000000000c");
        var baseTime = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            await repository.AddRangeAsync(
            [
                // Два разных сочетания в один instant — порядок по id DESC.
                CreateDecision(id: smallerId, commodityName: "Меньший id", normalizedName: "меньший id", createdAt: baseTime),
                CreateDecision(id: greaterId, commodityName: "Больший id", normalizedName: "больший id", createdAt: baseTime),
                CreateDecision(commodityName: "Свежее", normalizedName: "свежее", createdAt: baseTime.AddMinutes(2)),
                CreateDecision(commodityName: "Старее", normalizedName: "старее", createdAt: baseTime.AddMinutes(-2))
            ]);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            var rows = await repository.GetCombinationsAsync(Query());

            Assert.Equal(
                new[] { "Свежее", "Больший id", "Меньший id", "Старее" },
                rows.Select(r => r.CommodityName).ToArray());
        }
    }

    [Fact]
    public async Task GetCombinationsAsync_skip_take_and_total_apply_to_dedup_rows_not_raw_events()
    {
        await ClearDatabaseAsync();

        var baseTime = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            // 6 сырых событий, 3 сочетания: «Молоко» подтверждалось трижды (t-2, t, t+2),
            // «Хлеб» — дважды (t-1, t+1), «Йогурт» — один раз (t-3).
            // Дедуп-порядок: Молоко(t+2), Хлеб(t+1), Йогурт(t-1).
            // Если бы Skip/Take применялись к сырым событиям, первая страница (limit 2) схлопнулась
            // бы в одну строку, а на второй оказалась копия «Молока» — форма выдачи это ловит.
            await repository.AddRangeAsync(
            [
                CreateDecision(createdAt: baseTime.AddMinutes(-2)),
                CreateDecision(createdAt: baseTime),
                CreateDecision(createdAt: baseTime.AddMinutes(2)),
                CreateDecision("Хлеб", "хлеб", createdAt: baseTime.AddMinutes(1)),
                CreateDecision("Хлеб", "хлеб", createdAt: baseTime.AddMinutes(-1)),
                CreateDecision("Йогурт", "йогурт", createdAt: baseTime.AddMinutes(-3))
            ]);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            var firstPage = await repository.GetCombinationsAsync(Query(limit: 2, offset: 0));
            var secondPage = await repository.GetCombinationsAsync(Query(limit: 2, offset: 2));
            var everything = await repository.GetCombinationsAsync(Query(limit: 100, offset: 0));
            var total = await repository.CountCombinationsAsync(Query(limit: 2, offset: 0));

            Assert.Equal(["Молоко", "Хлеб"], firstPage.Select(r => r.CommodityName).ToArray());
            Assert.Equal(["Йогурт"], secondPage.Select(r => r.CommodityName).ToArray());

            // Total = 3 уникальных сочетания, а не 6 событий; при limit ≥ общего числа
            // совпадает с фактическим числом строк выдачи.
            Assert.Equal(3, total);
            Assert.Equal(total, everything.Count);

            // «Молоко» представлено самым свежим событием.
            var moloko = everything.Single(r => r.CommodityName == "Молоко");
            Assert.Equal(baseTime.AddMinutes(2), moloko.CreatedAt);
        }
    }

    [Fact]
    public async Task GetCombinationsAsync_period_and_onlyMismatch_apply_before_dedup_on_postgres()
    {
        await ClearDatabaseAsync();

        var baseTime = new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);
        var earlyAcceptor = Guid.NewGuid();
        var inWindowAcceptor = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            await repository.AddRangeAsync(
            [
                // Расхождение: два события, раннее вне периода (accepted_by различаются —
                // по нему видно, какое событие осталось «последним»).
                CreateDecision("Молоко", "молоко", acceptedBy: earlyAcceptor, createdAt: baseTime),
                CreateDecision("Молоко", "молоко", acceptedBy: inWindowAcceptor, createdAt: baseTime.AddMinutes(2)),
                // Согласие (suggested == final) — отсекается onlyMismatch.
                CreateDecision(
                    "Хлеб", "хлеб",
                    suggested: CommodityCategory.Bakery, final: CommodityCategory.Bakery,
                    createdAt: baseTime.AddMinutes(1))
            ]);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            // Период [t+2, t+3) оставляет только второе «Молоко»; «Хлеб» вне выдачи по onlyMismatch.
            var query = new CategorizationDecisionQuery(
                OnlyMismatch: true,
                Source: null,
                SuggestedCategoryId: null,
                FinalCategoryId: null,
                Search: null,
                From: baseTime.AddMinutes(2),
                To: baseTime.AddMinutes(3),
                Limit: 10,
                Offset: 0);

            var rows = await repository.GetCombinationsAsync(query);
            var total = await repository.CountCombinationsAsync(query);

            var row = Assert.Single(rows);
            Assert.Equal("Молоко", row.CommodityName);
            Assert.Equal(inWindowAcceptor, row.AcceptedBy);
            Assert.Equal(baseTime.AddMinutes(2), row.CreatedAt);
            Assert.Equal(1, total);
        }
    }

    /// <summary>
    /// Критерий приёмки T-07: дедуп выполняется на уровне EF-запроса, а не в памяти приложения
    /// после выборки. Проверка — фактическая SQL-выдача Npgsql: anti-join как NOT EXISTS,
    /// Skip/Take применены к дедуп-запросу; Total считается по тому же anti-join.
    /// </summary>
    [Fact]
    public void BuildPageQuery_and_dedup_query_are_translated_to_anti_join_sql()
    {
        // Трансляция LINQ в SQL подключения не требует; исполнение на реальном PostgreSQL
        // подтверждено тестами выше (иначе они падали бы TranslationNotSupportedException).
        using var context = CreateContext();
        var repository = CreateRepository(context);

        var query = new CategorizationDecisionQuery(
            OnlyMismatch: true,
            Source: "ai",
            SuggestedCategoryId: null,
            FinalCategoryId: null,
            Search: "хлеб",
            From: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero),
            Limit: 25,
            Offset: 50);

        var pageSql = repository.BuildPageQuery(query).ToQueryString();

        Assert.Contains("NOT EXISTS", pageSql);
        Assert.Contains("ILIKE", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", pageSql);
        Assert.Contains("OFFSET", pageSql);
        Assert.Contains("DESC", pageSql);

        // Тот же anti-join без пагинации — основа Total = Count().
        var dedupSql = repository.BuildDeduplicatedQuery(query).ToQueryString();

        Assert.Contains("NOT EXISTS", dedupSql);
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
        DateTimeOffset? createdAt = null,
        Guid? id = null)
        => new(
            id ?? Guid.NewGuid(),
            commodityName,
            normalizedName,
            source,
            suggested,
            final,
            acceptedBy ?? Guid.NewGuid(),
            acceptedByAdmin,
            createdAt ?? DateTimeOffset.UtcNow);

    private static CategorizationDecisionQuery Query(
        bool onlyMismatch = false,
        string? source = null,
        int? suggested = null,
        int? final = null,
        string? search = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 100,
        int offset = 0)
        => new(onlyMismatch, source, suggested, final, search, from, to, limit, offset);

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
