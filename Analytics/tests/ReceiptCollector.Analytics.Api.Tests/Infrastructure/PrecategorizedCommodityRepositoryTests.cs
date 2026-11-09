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
            // ADR-033 D1: фоновая вставка пишет исходное предложение = предложенной категории.
            Assert.Equal((int)CommodityCategory.Dairy, stored.OriginalProposedCategoryId);
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
    public async Task TryAddProposalAsync_returns_false_when_proposed_with_same_normalized_key_exists_at_another_merchant()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"), CreateMerchant(merchantB, "B"));
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.True(await repository.TryAddProposalAsync(
                new CandidateCursor("Молоко", merchantA), CommodityCategory.Dairy, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            // FR-3.4 ADR-027 по очищенному ключу: тот же ключ уже proposed у другого
            // магазина — unique-violation по частичному ux_..._normalized_proposed
            // (прежний индекс (name, merchant_id) такую пару не запрещает), no-op без
            // исключения (путь catch в TryAddProposalAsync).
            Assert.False(await repository.TryAddProposalAsync(
                new CandidateCursor("Молоко", merchantB), CommodityCategory.Food, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.SingleAsync();
            Assert.Equal(merchantA, stored.MerchantId);
            Assert.Equal((int)CommodityCategory.Dairy, stored.ProposedCategoryId);
        }
    }

    [Fact]
    public async Task TryAddProposalAsync_writes_normalized_name_and_keeps_raw_name()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        // Ведущий EAN-13: очищенный ключ — без кода (ADR-034 п. 1), сырое имя сохраняется (FR-4).
        const string rawName = "2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ";

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.True(await repository.TryAddProposalAsync(
                new CandidateCursor(rawName, merchantA), CommodityCategory.Food, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.SingleAsync();
            Assert.Equal(rawName, stored.Name);
            Assert.Equal("пирог с вишней по ре", stored.NormalizedName);
            Assert.Equal(CommodityNameNormalizer.NormalizeName(rawName), stored.NormalizedName);
        }
    }

    [Fact]
    public async Task TryAddProposalAsync_normalized_name_never_exceeds_raw_length_in_current_runtime()
    {
        await ClearDatabaseAsync();

        // Отклонение от плана T-02 (зафиксировано в сводке): пример «200 × İ → >256 после
        // ToLowerInvariant» опирается на полные регистропреобразования .NET Framework.
        // В .NET 10 (ICU) ToLowerInvariant использует простые соответствия и строку не
        // расширяет (проверено сканом BMP и дополнительной плоскости: расширяющих символов
        // нет), поэтому guard на длину очищенного имени — defense-in-depth (ADR-034
        // «Риски»: «Переполнение varchar(256) после lowercase — Низкая»; та же логика, что
        // в оговорке T-03 про preflight). Тест фиксирует факт: имя вставляется, ключ не
        // длиннее сырого имени.
        var name = new string('İ', 200);
        Assert.True(name.Length <= 256);
        Assert.Equal(200, CommodityNameNormalizer.NormalizeName(name).Length);

        var merchantA = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.True(await repository.TryAddProposalAsync(
                new CandidateCursor(name, merchantA), CommodityCategory.Food, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.SingleAsync();
            Assert.Equal(name, stored.Name);
            Assert.Equal(200, stored.NormalizedName.Length);
        }
    }

    [Fact]
    public async Task TryAddProposalAsync_accepts_name_exactly_at_schema_limit()
    {
        await ClearDatabaseAsync();

        // Граница guard'а включительно: 256 символов — допустимая длина (FR-4.7).
        var name = new string('x', 256);
        var merchantA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.True(await repository.TryAddProposalAsync(
                new CandidateCursor(name, merchantA), CommodityCategory.Food, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.SingleAsync();
            Assert.Equal(name, stored.NormalizedName);
        }
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
    public async Task GetCategoriesByNormalizedNamesAsync_returns_latest_row_and_skips_invalid_categories()
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
                    (id, name, merchant_id, proposed_category_id, original_proposed_category_id, status, created_at, normalized_name)
                VALUES
                    ({Guid.NewGuid()}, {"Кефир"}, {merchantA}, {9999}, {9999}, {(short)0}, {DateTimeOffset.UtcNow}, {CommodityNameNormalizer.NormalizeName("Кефир")})
                """);
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            // ADR-034 п. 3: параметр и ключ результата — очищенное имя (normalized_name).
            var categories = await repository.GetCategoriesByNormalizedNamesAsync(
                new[] { "молоко", "хлеб", "кефир", "неизвестно" },
                CancellationToken.None);

            Assert.Equal(2, categories.Count);
            // Самая свежая строка (proposed молоко) выигрывает.
            Assert.Equal(CommodityCategory.Dairy, categories["молоко"]);
            Assert.Equal(CommodityCategory.Bakery, categories["хлеб"]);
            Assert.False(categories.ContainsKey("кефир"));
            Assert.False(categories.ContainsKey("неизвестно"));
        }
    }

    [Fact]
    public async Task GetCategoriesByNormalizedNamesAsync_matches_row_by_cleaned_key()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        // ADR-034 п. 6: очередь находится по очищенному ключу — вариант с кодами находит
        // строку, сохранённую под ключом без кодов; поиск по сырому имени более не совпадает.
        const string storedName = "Туалетная бумага \"СВАЁ\" (3сл, 4р)";
        const string variantName = "2195738 Туалетная бумага \"СВАЁ\" (3сл, 4р) 4810981001530";

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
            context.PrecategorizedCommodities.Add(
                Proposal(storedName, merchantA, CommodityCategory.HouseholdChemicals,
                    PrecategorizedCommodityStatus.Applied, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var key = CommodityNameNormalizer.NormalizeName(variantName);

            var categories = await repository.GetCategoriesByNormalizedNamesAsync(
                [key], CancellationToken.None);

            var entry = Assert.Single(categories);
            Assert.Equal(key, entry.Key);
            Assert.Equal(CommodityCategory.HouseholdChemicals, entry.Value);

            // Регресс: сырое имя (с кодами) как параметр больше не матчится — ключ только очищенный.
            var byRawName = await repository.GetCategoriesByNormalizedNamesAsync(
                [variantName], CancellationToken.None);
            Assert.Empty(byRawName);
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
    public async Task HasBlockingQueueEntryAsync_blocks_applied_row_of_same_merchant()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Applied));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            // C1: строка с тем же ключом у этого магазина в ЛЮБОМ статусе блокирует кандидата.
            Assert.True(await repository.HasBlockingQueueEntryAsync("молоко", merchantA, CancellationToken.None));
        }
    }

    [Fact]
    public async Task HasBlockingQueueEntryAsync_blocks_proposed_row_of_same_merchant()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.True(await repository.HasBlockingQueueEntryAsync("молоко", merchantA, CancellationToken.None));
        }
    }

    [Fact]
    public async Task HasBlockingQueueEntryAsync_blocks_proposed_row_of_another_merchant()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"), CreateMerchant(merchantB, "B"));
            // FR-3.4 ADR-027 по очищенному ключу: proposed у другого магазина блокирует и здесь.
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.True(await repository.HasBlockingQueueEntryAsync("молоко", merchantA, CancellationToken.None));
        }
    }

    [Fact]
    public async Task HasBlockingQueueEntryAsync_does_not_block_applied_row_of_another_merchant()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var merchantB = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"), CreateMerchant(merchantB, "B"));
            // applied у другого магазина НЕ блокирует (тот же инвариант, что у NOT EXISTS в GetCandidatesAsync).
            context.PrecategorizedCommodities.Add(
                Proposal("Сыр", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Applied));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            Assert.False(await repository.HasBlockingQueueEntryAsync("сыр", merchantA, CancellationToken.None));
            // ...а у своего магазина тот же applied блокирует.
            Assert.True(await repository.HasBlockingQueueEntryAsync("сыр", merchantB, CancellationToken.None));
        }
    }

    [Fact]
    public async Task HasBlockingQueueEntryAsync_returns_false_for_other_key_and_empty_table()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            // Пустая таблица — ничего не блокирует.
            var repository = CreateRepository(context);
            Assert.False(await repository.HasBlockingQueueEntryAsync("молоко", merchantA, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
            context.PrecategorizedCommodities.Add(
                Proposal("Молоко", merchantA, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            // Другой ключ — не блокирует.
            Assert.False(await repository.HasBlockingQueueEntryAsync("хлеб", merchantA, CancellationToken.None));
        }
    }

    [Fact]
    public async Task HasBlockingQueueEntryAsync_matches_by_cleaned_key()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        const string storedName = "Туалетная бумага \"СВАЁ\" (3сл, 4р)";
        const string variantName = "2195738 Туалетная бумага \"СВАЁ\" (3сл, 4р) 4810981001530";

        await using (var context = CreateContext())
        {
            await SeedAsync(context, CreateMerchant(merchantA, "A"));
            context.PrecategorizedCommodities.Add(
                Proposal(storedName, merchantA, CommodityCategory.HouseholdChemicals, PrecategorizedCommodityStatus.Proposed));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var stored = await context.PrecategorizedCommodities.SingleAsync();
            Assert.Equal("туалетная бумага \"сваё\" (3сл, 4р)", stored.NormalizedName);

            var repository = CreateRepository(context);
            // Вариант с артикулом в начале и EAN в конце даёт тот же очищенный ключ → блокировка.
            var variantKey = CommodityNameNormalizer.NormalizeName(variantName);
            Assert.True(await repository.HasBlockingQueueEntryAsync(variantKey, merchantA, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ConfirmAsync_updates_commodities_of_all_users_and_touches_only_confirmed_row()
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
                // ADR-034 п. 2: частичный уникальный индекс ux_..._normalized_proposed
                // запрещает две proposed-строки с одним очищенным ключом глобально
                // (FR-3.4 ADR-027 по очищенному ключу), поэтому «сестринской» строке
                // задан иной ключ — сид с двумя proposed «молоко» новый индекс не
                // пропускает. В проде sibling-состояние недостижимо, и confirm его
                // не ищет и не удаляет (ADR-034 D1/D4): строка остаётся в таблице.
                Proposal("Молоко", merchantB, CommodityCategory.Dairy, PrecategorizedCommodityStatus.Proposed, id: siblingId, normalizedName: "sibling"));
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

            // Confirm переводит в applied только свою строку; «сестринская» proposed
            // не удаляется — confirm не ищет и не удаляет sibling-строки (ADR-034 D1/D4).
            var sibling = await context.PrecategorizedCommodities.FindAsync(siblingId);
            Assert.NotNull(sibling);
            Assert.Equal(PrecategorizedCommodityStatus.Proposed, sibling!.Status);
        }
    }

    [Fact]
    public async Task ConfirmAsync_updates_commodities_only_by_exact_raw_name()
    {
        await ClearDatabaseAsync();

        var merchantA = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        // Регресс ADR-034 D1/NFR-1: подтверждение строки с кодом НЕ проставляет категорию
        // варианту без кода (тот же очищенный ключ) — commodities обновляются только
        // по точному сырому имени, массового применения по ключу нет.
        const string rawName = "2417268000006 ПИРОГ С ВИШНЕЙ ПО РЕ";
        const string variantName = "ПИРОГ С ВИШНЕЙ ПО РЕ";

        await using (var context = CreateContext())
        {
            await SeedAsync(
                context,
                CreateReceipt(userA, merchantA, (rawName, null)),
                CreateReceipt(userB, merchantA, (variantName, null)));

            context.PrecategorizedCommodities.Add(
                Proposal(rawName, merchantA, CommodityCategory.Food, PrecategorizedCommodityStatus.Proposed, id: proposalId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var result = await repository.ConfirmAsync(proposalId, CommodityCategory.Food, CancellationToken.None);

            Assert.Equal(PrecategorizedOperationOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.UpdatedCommodities);
        }

        await using (var context = CreateContext())
        {
            var withCode = Assert.Single(await context.Commodities.Where(c => c.Name == rawName).ToListAsync());
            Assert.Equal((int)CommodityCategory.Food, withCode.CategoryId);

            var variant = Assert.Single(await context.Commodities.Where(c => c.Name == variantName).ToListAsync());
            Assert.Null(variant.CategoryId);
            Assert.Null(variant.CategoryName);
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
            // ADR-033 D1: PUT .../category меняет только текущее предложение, исходное неизменно.
            Assert.Equal((int)CommodityCategory.Food, stored.OriginalProposedCategoryId);
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
        Guid? id = null,
        string? normalizedName = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            // ADR-034 п. 2 (B1): у каждой строки очереди есть очищенный ключ.
            // normalizedName задаётся явно только там, где «честный» ключ нарушил бы
            // частичный уникальный индекс proposed (см. комментарий в тесте Confirm).
            NormalizedName = normalizedName ?? CommodityNameNormalizer.NormalizeName(name),
            MerchantId = merchantId,
            ProposedCategoryId = (int)category,
            // Исходное предложение пишется фоном при вставке (ADR-033 D1); PUT его не меняет.
            OriginalProposedCategoryId = (int)category,
            Status = status,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            AppliedAt = appliedAt
        };
}
