using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Read-сервис журнала категоризации (T-07, ADR-033 п. 7, FR-7): дедуп уникальных сочетаний,
/// «кто принял»/decidedAt от последнего события периода, фильтры, границы периода, Total и
/// отображение (имена категорий, имя принявшего). EF InMemory — LINQ-выражения anti-join
/// переносимы (ADR-033 п. 10); поиск ILike и поведение на реальном PostgreSQL — в контейнерных
/// <see cref="Infrastructure.CategorizationDecisionRepositoryTests"/>.
/// </summary>
public sealed class CategorizationDecisionReadServiceTests
{
    private static readonly DateTimeOffset T1 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPageAsync_collapses_identical_combinations_into_one_row_with_last_decision()
    {
        await using var context = CreateContext();
        var firstAcceptor = Guid.NewGuid();
        var lastAcceptor = Guid.NewGuid();

        // Одно и то же сочетание («Молоко», ai, Dairy → Bakery) подтверждено дважды; принявший
        // и decidedAt строки выдачи берутся от последнего события (FR-7).
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery,
                firstAcceptor, acceptedByAdmin: false, T1),
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery,
                lastAcceptor, acceptedByAdmin: true, T2));

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query());

        var row = Assert.Single(items);
        Assert.Equal("Молоко", row.CommodityName);
        Assert.Equal("ai", row.SuggestionSource);
        Assert.Equal(lastAcceptor, row.AcceptedById);
        Assert.True(row.AcceptedByAdmin);
        Assert.Equal(T2, row.DecidedAt);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_total_counts_unique_combinations_not_events()
    {
        await using var context = CreateContext();

        // 5 сырых событий (FR-8) → 4 уникальных сочетания: совпадают только первые два; различие
        // источника и итоговой категории дают отдельные сочетания (ключ — name + source +
        // suggested + final).
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T1),
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T2),
            Decision("Молоко", "молоко", "cache", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T3),
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Dairy, createdAt: T3),
            Decision("Хлеб", "хлеб", "ai", CommodityCategory.Bakery, CommodityCategory.Bakery, createdAt: T1));

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query(limit: 50));

        Assert.Equal(4, total);
        Assert.Equal(4, items.Count);
        Assert.Equal(5, await context.CategorizationDecisions.CountAsync());
    }

    [Fact]
    public async Task GetPageAsync_onlyMismatch_drops_agreements()
    {
        await using var context = CreateContext();
        await SeedAsync(context,
            // Согласие: предложено == принято.
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Dairy, createdAt: T1),
            // Исправление, в том числе со сбросом в Undefined (final = 0).
            Decision("Хлеб", "хлеб", "ai", CommodityCategory.Bakery, CommodityCategory.Groceries, createdAt: T2),
            Decision("Йогурт", "йогурт", "ai", CommodityCategory.Dairy, CommodityCategory.Undefined, createdAt: T3));

        var service = CreateService(context);

        var (all, allTotal) = await service.GetPageAsync(Query(onlyMismatch: false));
        var (mismatches, mismatchTotal) = await service.GetPageAsync(Query(onlyMismatch: true));

        Assert.Equal(3, allTotal);
        Assert.Equal(2, mismatchTotal);
        Assert.Equal(2, mismatches.Count);
        Assert.All(mismatches, row => Assert.NotEqual(row.SuggestedCategoryId, row.FinalCategoryId));

        // Сброс после предложения: final = 0 — канонический «нет категории», имя «Не указана».
        var reset = Assert.Single(mismatches, row => row.FinalCategoryId == (int)CommodityCategory.Undefined);
        Assert.Equal("Не указана", reset.FinalCategoryName);
    }

    [Fact]
    public async Task GetPageAsync_source_filter_keeps_only_matching_wire_name()
    {
        await using var context = CreateContext();
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T1),
            Decision("Кефир", "кефир", "cache", CommodityCategory.Dairy, CommodityCategory.Dairy, createdAt: T2),
            Decision("Творог", "творог", "merchant-default", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T3));

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query(source: "cache"));

        var row = Assert.Single(items);
        Assert.Equal("Кефир", row.CommodityName);
        Assert.Equal("cache", row.SuggestionSource);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_suggested_and_final_category_filters_work()
    {
        await using var context = CreateContext();
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T1),
            Decision("Хлеб", "хлеб", "ai", CommodityCategory.Bakery, CommodityCategory.Bakery, createdAt: T2),
            Decision("Йогурт", "йогурт", "ai", CommodityCategory.Dairy, CommodityCategory.Undefined, createdAt: T3));

        var service = CreateService(context);

        var (bySuggested, suggestedTotal) = await service.GetPageAsync(
            Query(suggested: (int)CommodityCategory.Dairy));
        var (byFinal, finalTotal) = await service.GetPageAsync(
            Query(final: (int)CommodityCategory.Bakery));
        // final = 0 («Не указана») — допустимый код фильтра.
        var (byUndefined, undefinedTotal) = await service.GetPageAsync(
            Query(final: (int)CommodityCategory.Undefined));

        Assert.Equal(2, suggestedTotal);
        Assert.All(bySuggested, row => Assert.Equal((int)CommodityCategory.Dairy, row.SuggestedCategoryId));

        Assert.Equal(2, finalTotal);
        Assert.All(byFinal, row => Assert.Equal((int)CommodityCategory.Bakery, row.FinalCategoryId));

        Assert.Equal(1, undefinedTotal);
        Assert.Equal("Йогурт", Assert.Single(byUndefined).CommodityName);
    }

    [Fact]
    public async Task GetPageAsync_period_bounds_are_inclusive_from_and_exclusive_to()
    {
        await using var context = CreateContext();

        // Одно сочетание с тремя событиями; из периода [T2, T3) остаётся только событие T2:
        // граница from включается (created_at >= from), граница to исключается (created_at < to).
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T1),
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T2),
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T3));

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query(from: T2, to: T3));

        var row = Assert.Single(items);
        Assert.Equal(T2, row.DecidedAt);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_period_narrows_the_last_event_of_combination()
    {
        await using var context = CreateContext();
        var earlyAcceptor = Guid.NewGuid();
        var lateAcceptor = Guid.NewGuid();

        // Вне периода есть более свежее событие (lateAcceptor, T2). Период применяется к событиям
        // до дедупа, поэтому «последнее» берётся внутри периода: принявший — earlyAcceptor
        // (ADR-033 «Период и дедуп», «Кто принял и decidedAt»).
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery,
                earlyAcceptor, acceptedByAdmin: false, T1),
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery,
                lateAcceptor, acceptedByAdmin: true, T2));

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query(to: T2));

        var row = Assert.Single(items);
        Assert.Equal(earlyAcceptor, row.AcceptedById);
        Assert.False(row.AcceptedByAdmin);
        Assert.Equal(T1, row.DecidedAt);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_missing_or_nameless_user_keeps_id_and_admin_flag_with_null_name()
    {
        await using var context = CreateContext();
        var deletedUserId = Guid.NewGuid();
        var namelessUserId = Guid.NewGuid();

        await context.Users.AddRangeAsync(new UserEntity
        {
            Id = namelessUserId,
            Name = "   ",
            ExternalId = "nameless",
            TelegramId = 101,
            IsAdmin = true
        });

        // deletedUserId намеренно без строки в users (журнал переживает удаление — FK нет, F1).
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery,
                deletedUserId, acceptedByAdmin: true, T1),
            Decision("Хлеб", "хлеб", "ai", CommodityCategory.Bakery, CommodityCategory.Groceries,
                namelessUserId, acceptedByAdmin: false, T2));

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query());

        Assert.Equal(2, total);

        var withoutUser = items.Single(r => r.CommodityName == "Молоко");
        Assert.Null(withoutUser.AcceptedByName);
        Assert.Equal(deletedUserId, withoutUser.AcceptedById);
        Assert.True(withoutUser.AcceptedByAdmin);

        var withoutName = items.Single(r => r.CommodityName == "Хлеб");
        Assert.Null(withoutName.AcceptedByName);
        Assert.Equal(namelessUserId, withoutName.AcceptedById);
        Assert.False(withoutName.AcceptedByAdmin);
    }

    [Fact]
    public async Task GetPageAsync_known_user_name_is_returned()
    {
        await using var context = CreateContext();
        var userId = Guid.NewGuid();

        await context.Users.AddRangeAsync(new UserEntity
        {
            Id = userId,
            Name = "Иван",
            ExternalId = "ivan",
            TelegramId = 202,
            IsAdmin = true
        });
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery,
                userId, acceptedByAdmin: true, T1));

        var service = CreateService(context);
        var (items, _) = await service.GetPageAsync(Query());

        Assert.Equal("Иван", Assert.Single(items).AcceptedByName);
    }

    [Fact]
    public async Task GetPageAsync_unknown_category_code_gets_dash_name_and_numeric_code_is_preserved()
    {
        await using var context = CreateContext();

        // Код вне справочника enum (например, удалённая категория) — безопасное чтение
        // (ADR-033 п. 1): имя «—», числовой код остаётся в строке.
        await SeedAsync(context,
            new CategorizationDecisionEntity
            {
                Id = Guid.NewGuid(),
                CommodityName = "Товар из прошлого",
                NormalizedName = "товар из прошлого",
                SuggestionSource = "ai",
                SuggestedCategoryId = 999,
                FinalCategoryId = (int)CommodityCategory.Dairy,
                AcceptedBy = Guid.NewGuid(),
                AcceptedByAdmin = false,
                CreatedAt = T1
            });

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query());

        var row = Assert.Single(items);
        Assert.Equal(999, row.SuggestedCategoryId);
        Assert.Equal("—", row.SuggestedCategoryName);
        Assert.Equal((int)CommodityCategory.Dairy, row.FinalCategoryId);
        Assert.Equal("Молочные продукты", row.FinalCategoryName);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_pages_and_total_follow_decided_at_desc_order()
    {
        await using var context = CreateContext();
        await SeedAsync(context,
            Decision("Ранний", "ранний", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T1),
            Decision("Средний", "средний", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T2),
            Decision("Поздний", "поздний", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T3));

        var service = CreateService(context);
        var (firstPage, total) = await service.GetPageAsync(Query(limit: 2, offset: 0));
        var (secondPage, _) = await service.GetPageAsync(Query(limit: 2, offset: 2));

        Assert.Equal(3, total);
        Assert.Equal(
            new[] { "Поздний", "Средний" },
            firstPage.Select(r => r.CommodityName).ToArray());
        Assert.Equal(
            new[] { "Ранний" },
            secondPage.Select(r => r.CommodityName).ToArray());
    }

    [Fact]
    public async Task GetPageAsync_zero_rows_still_returns_total_for_nonzero_offset()
    {
        await using var context = CreateContext();
        await SeedAsync(context,
            Decision("Молоко", "молоко", "ai", CommodityCategory.Dairy, CommodityCategory.Bakery, createdAt: T1));

        var service = CreateService(context);
        var (items, total) = await service.GetPageAsync(Query(limit: 10, offset: 5));

        Assert.Empty(items);
        Assert.Equal(1, total);
    }

    private static CategorizationDecisionReadService CreateService(ReceiptDbContext context)
        => new(new CategorizationDecisionRepository(context), context);

    private static CategorizationDecisionQuery Query(
        bool onlyMismatch = false,
        string? source = null,
        int? suggested = null,
        int? final = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 50,
        int offset = 0)
        => new(onlyMismatch, source, suggested, final, Search: null, from, to, limit, offset);

    private static CategorizationDecisionEntity Decision(
        string commodityName,
        string normalizedName,
        string source,
        CommodityCategory suggested,
        CommodityCategory final,
        Guid? acceptedBy = null,
        bool acceptedByAdmin = true,
        DateTimeOffset? createdAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            CommodityName = commodityName,
            NormalizedName = normalizedName,
            SuggestionSource = source,
            SuggestedCategoryId = (int)suggested,
            FinalCategoryId = (int)final,
            AcceptedBy = acceptedBy ?? Guid.NewGuid(),
            AcceptedByAdmin = acceptedByAdmin,
            CreatedAt = createdAt ?? T1
        };

    private static async Task SeedAsync(ReceiptDbContext context, params CategorizationDecisionEntity[] decisions)
    {
        await context.CategorizationDecisions.AddRangeAsync(decisions);
        await context.SaveChangesAsync();
    }

    private static ReceiptDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
