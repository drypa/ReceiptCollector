using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Постраничное чтение магазинов с поиском (ADR-037, задача 01 плана merchants-search):
/// фильтр <c>name OR inn</c> применяется в запросе до <c>COUNT/ORDER BY/SKIP/TAKE</c>,
/// null-ИНН безопасен, порядок детерминирован. Реальный PostgreSQL — ILike не поддерживается EF InMemory.
/// </summary>
public sealed class MerchantRepositoryPageTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public MerchantRepositoryPageTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync().ConfigureAwait(false);
        _connectionString = _postgresContainer.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task GetPageAsync_WithoutSearch_ReturnsAllOrderedWithTotal()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Пятёрочка", "7701000001"),
            CreateMerchant("Магнит", "7702000002"),
            CreateMerchant("Лента", null));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, null, CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(new[] { "Лента", "Магнит", "Пятёрочка" }, items.Select(m => m.Name).ToArray());
    }

    [Fact]
    public async Task GetPageAsync_WhitespaceSearch_TreatedAsNoFilter()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Пятёрочка", "7701000001"),
            CreateMerchant("Магнит", "7702000002"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, "   ", CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByName_PartialCaseInsensitive()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Пятёрочка", "7701000001"),
            CreateMerchant("Магнит", "7702000002"),
            CreateMerchant("Лента", "7703000003"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, "мАг", CancellationToken.None);

        var merchant = Assert.Single(items);
        Assert.Equal("Магнит", merchant.Name);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByInn_PartialMatch()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Пятёрочка", "7701000001"),
            CreateMerchant("Магнит", "7702000002"),
            CreateMerchant("Лента", "9903000003"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, "7702", CancellationToken.None);

        var merchant = Assert.Single(items);
        Assert.Equal("Магнит", merchant.Name);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_FragmentMatchesNameOfOneAndInnOfAnother_ReturnsBothOnce()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Shop 123", "9900000001"),
            CreateMerchant("Другой", "7700123999"),
            CreateMerchant("Третий", "8800000000"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, "123", CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, m => m.Name == "Shop 123");
        Assert.Contains(items, m => m.Name == "Другой");
    }

    [Fact]
    public async Task GetPageAsync_FragmentMatchesNameAndInnOfSameMerchant_ReturnsOnce()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Store 555", "5550000000"),
            CreateMerchant("Другой", "8800000000"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, "555", CancellationToken.None);

        var merchant = Assert.Single(items);
        Assert.Equal("Store 555", merchant.Name);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetPageAsync_MerchantWithNullInn_FoundByNameAndInnFilterIsSafe()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("БезИНН Магазин", null),
            CreateMerchant("Пятёрочка", "7701000001"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (byName, totalByName) = await repository.GetPageAsync(50, 0, "безинн", CancellationToken.None);
        Assert.Equal("БезИНН Магазин", Assert.Single(byName).Name);
        Assert.Equal(1, totalByName);

        var (byInnFragment, totalByInn) = await repository.GetPageAsync(50, 0, "7701", CancellationToken.None);
        Assert.Equal("Пятёрочка", Assert.Single(byInnFragment).Name);
        Assert.Equal(1, totalByInn);
    }

    [Fact]
    public async Task GetPageAsync_EmptyResult_ReturnsEmptyPageAndZeroTotal()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Пятёрочка", "7701000001"),
            CreateMerchant("Магнит", "7702000002"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, "несуществует", CancellationToken.None);

        Assert.Empty(items);
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task GetPageAsync_AppliesFilterBeforeCountAndPagination()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Альфа", "1000000001"),
            CreateMerchant("Бета", "1000000002"),
            CreateMerchant("Гамма", "1000000003"),
            CreateMerchant("Дельта", "9999999999"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        // Совпадают по ИНН-фрагменту "1000" только первые три (в алфавитном порядке: Альфа, Бета, Гамма).
        var (items, total) = await repository.GetPageAsync(limit: 1, offset: 1, searchTerm: "1000", CancellationToken.None);

        Assert.Equal(3, total);
        var merchant = Assert.Single(items);
        Assert.Equal("Бета", merchant.Name);
    }

    [Fact]
    public async Task GetPageAsync_OrderIsDeterministic_WithTieBreakerOnId()
    {
        await ClearDatabaseAsync();
        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");

        await SeedMerchantsAsync(
            CreateMerchant("Одинаковое", "7701000001", id2),
            CreateMerchant("Одинаковое", "7701000002", id1));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, null, CancellationToken.None);

        Assert.Equal(2, total);
        Assert.Equal(new[] { id1, id2 }, items.Select(m => m.Id).ToArray());
    }

    [Fact]
    public async Task GetPageAsync_TrimsSearchTerm()
    {
        await ClearDatabaseAsync();
        await SeedMerchantsAsync(
            CreateMerchant("Пятёрочка", "7701000001"),
            CreateMerchant("Магнит", "7702000002"));

        await using var context = CreateContext();
        var repository = new MerchantRepository(context);

        var (items, total) = await repository.GetPageAsync(50, 0, "  маг  ", CancellationToken.None);

        Assert.Equal("Магнит", Assert.Single(items).Name);
        Assert.Equal(1, total);
    }

    private ReceiptDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ReceiptDbContext(options);
    }

    private async Task SeedMerchantsAsync(params MerchantEntity[] merchants)
    {
        await using var context = CreateContext();
        await context.Merchants.AddRangeAsync(merchants);
        await context.SaveChangesAsync();
    }

    private static MerchantEntity CreateMerchant(string name, string? inn, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Category = MerchantCategory.Undefined,
            Inn = inn
        };

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                "commodities",
                "receipts",
                "precategorized_commodities",
                "categorization_decisions",
                "merchants",
                "users"
            RESTART IDENTITY CASCADE;
            """);
    }
}
