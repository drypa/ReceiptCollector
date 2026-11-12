using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты EF-репозитория слов категоризации (T-03, ADR-035 п. 1, решение A1). Схема создаётся
/// из EF-модели (<c>EnsureCreatedAsync</c>) — как в остальных Postgres-тестах проекта, поэтому
/// уникальный индекс и CHECK-констрейнты объявлены и в конфигурации, и в SQL-миграции (T-01).
/// </summary>
public sealed class CommodityCategoryKeywordRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public CommodityCategoryKeywordRepositoryTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task GetAllAsync_returns_all_rows_including_same_word_in_other_list_or_category()
    {
        await using var context = CreateContext();
        context.CommodityCategoryKeywords.AddRange(
            Entity((int)CommodityCategory.Groceries, "boost", "Соль", "соль"),
            Entity((int)CommodityCategory.Groceries, "exclude", "соль", "соль"),
            Entity((int)CommodityCategory.Fruits, "boost", "соль", "соль"));
        await context.SaveChangesAsync();

        var repository = CreateRepository(context);
        var all = await repository.GetAllAsync();

        // Одно слово в разных списках и категориях — разные строки, все попадают в снимок
        // (адресность пар «категория + список», FR-1/FR-5).
        Assert.Equal(3, all.Count);
        Assert.Contains(all, k => k.Category == CommodityCategory.Groceries
                                  && k.ListType == KeywordListType.Boost
                                  && k.Word == "Соль" && k.NormalizedWord == "соль");
        Assert.Contains(all, k => k.Category == CommodityCategory.Groceries
                                  && k.ListType == KeywordListType.Exclude
                                  && k.Word == "соль" && k.NormalizedWord == "соль");
        Assert.Contains(all, k => k.Category == CommodityCategory.Fruits
                                  && k.ListType == KeywordListType.Boost
                                  && k.Word == "соль" && k.NormalizedWord == "соль");
    }

    [Fact]
    public async Task GetAllAsync_skips_rows_with_unknown_category_code()
    {
        await using var context = CreateContext();
        context.CommodityCategoryKeywords.AddRange(
            Entity((int)CommodityCategory.DriedFruits, "boost", "изюм", "изюм"),
            // CHECK запрещает только 0; код вне реестра категорий (например, удалённый из enum)
            // схемой пропускается — безопасное чтение ADR-032 обязана фильтровать приложение.
            Entity(9999, "boost", "дичь", "дичь"));
        await context.SaveChangesAsync();

        var repository = CreateRepository(context);
        var all = await repository.GetAllAsync();

        var single = Assert.Single(all);
        Assert.Equal(CommodityCategory.DriedFruits, single.Category);
        Assert.Equal("изюм", single.Word);
    }

    [Fact]
    public async Task GetAllAsync_skips_row_with_broken_list_type()
    {
        // CHECK ck_commodity_category_keywords_list_type штатно не пропускает «битый» list_type;
        // тест моделирует ручную правку схемы (констрейнт снят, строка вставлена напрямую).
        // Такое слово пропускается при чтении — молчаливое приведение к Boost запрещено
        // (решение по замечанию ревью T-02).
        await using var context = CreateContext();
        context.CommodityCategoryKeywords.Add(
            Entity((int)CommodityCategory.Groceries, "boost", "соль", "соль"));
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE commodity_category_keywords DROP CONSTRAINT ck_commodity_category_keywords_list_type");
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO commodity_category_keywords (id, category_id, list_type, word, normalized_word) " +
            "VALUES (gen_random_uuid(), {0}, {1}, {2}, {3})",
            [(int)CommodityCategory.Groceries, "other", "битое", "битое"]);

        var repository = CreateRepository(context);
        var all = await repository.GetAllAsync();

        var single = Assert.Single(all);
        Assert.Equal("соль", single.Word);
    }

    [Fact]
    public async Task ExistsAsync_and_CountAsync_are_addressable_by_category_and_list()
    {
        await using var context = CreateContext();
        context.CommodityCategoryKeywords.AddRange(
            Entity((int)CommodityCategory.Groceries, "boost", "соль", "соль"),
            Entity((int)CommodityCategory.Groceries, "exclude", "соль", "соль"),
            Entity((int)CommodityCategory.Fruits, "boost", "соль", "соль"));
        await context.SaveChangesAsync();

        var repository = CreateRepository(context);

        // Слово в другой категории/списке на адресную проверку не влияет (FR-5).
        Assert.True(await repository.ExistsAsync(CommodityCategory.Groceries, KeywordListType.Boost, "соль"));
        Assert.False(await repository.ExistsAsync(CommodityCategory.Groceries, KeywordListType.Boost, "сахар"));
        Assert.False(await repository.ExistsAsync(CommodityCategory.Fruits, KeywordListType.Exclude, "соль"));

        Assert.Equal(1, await repository.CountAsync(CommodityCategory.Groceries, KeywordListType.Boost));
        Assert.Equal(1, await repository.CountAsync(CommodityCategory.Groceries, KeywordListType.Exclude));
        Assert.Equal(1, await repository.CountAsync(CommodityCategory.Fruits, KeywordListType.Boost));
        Assert.Equal(0, await repository.CountAsync(CommodityCategory.Fruits, KeywordListType.Exclude));
    }

    [Fact]
    public async Task AddAsync_persists_word_and_normalized_word_as_passed()
    {
        await using var context = CreateContext();
        var repository = CreateRepository(context);

        var keyword = new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "Курага", "курага");

        Assert.True(await repository.AddAsync(keyword));

        // Слово хранится как передано (FR-5): регистр сохранён, normalized_word — как посчитан.
        var row = await context.CommodityCategoryKeywords.AsNoTracking().SingleAsync();
        Assert.Equal(keyword.Id, row.Id);
        Assert.Equal((int)CommodityCategory.DriedFruits, row.CategoryId);
        Assert.Equal("boost", row.ListType);
        Assert.Equal("Курага", row.Word);
        Assert.Equal("курага", row.NormalizedWord);
    }

    [Fact]
    public async Task AddAsync_generates_id_when_record_carries_empty_guid()
    {
        await using var context = CreateContext();
        var repository = CreateRepository(context);

        var added = await repository.AddAsync(new CommodityCategoryKeyword(
            Guid.Empty, CommodityCategory.DriedFruits, KeywordListType.Boost, "изюм", "изюм"));

        Assert.True(added);
        var row = await context.CommodityCategoryKeywords.AsNoTracking().SingleAsync();
        Assert.NotEqual(Guid.Empty, row.Id);
    }

    [Fact]
    public async Task AddAsync_returns_false_on_duplicate_in_same_pair_including_other_case()
    {
        await using var context = CreateContext();
        var repository = CreateRepository(context);

        Assert.True(await repository.AddAsync(new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.Groceries, KeywordListType.Boost, "Соль", "соль")));

        // Дубликат в той же паре (тот же normalized_word) — false, а не исключение:
        // гонка двух вкладок даёт 409, а не 500 (ADR-035 п. 7). Другой регистр слова не спасает.
        Assert.False(await repository.AddAsync(new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.Groceries, KeywordListType.Boost, "СОЛЬ", "соль")));
        Assert.False(await repository.AddAsync(new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.Groceries, KeywordListType.Boost, "соль", "соль")));

        // То же слово в другом списке / другой категории — легально (адресность пары).
        Assert.True(await repository.AddAsync(new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.Groceries, KeywordListType.Exclude, "соль", "соль")));
        Assert.True(await repository.AddAsync(new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.Fruits, KeywordListType.Boost, "соль", "соль")));

        Assert.Equal(3, await context.CommodityCategoryKeywords.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_removes_row_by_id_and_returns_false_on_repeat()
    {
        await using var context = CreateContext();
        var repository = CreateRepository(context);

        var keyword = new CommodityCategoryKeyword(
            Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "изюм", "изюм");
        Assert.True(await repository.AddAsync(keyword));

        Assert.True(await repository.DeleteAsync(keyword.Id));
        Assert.Equal(0, await context.CommodityCategoryKeywords.CountAsync());

        // Повтор (или удаление уже удалённого в другой вкладке) — false, без исключения:
        // сервис отвечает 404 (ADR-035 п. 7).
        Assert.False(await repository.DeleteAsync(keyword.Id));
        Assert.False(await repository.DeleteAsync(Guid.NewGuid()));
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

    private static CommodityCategoryKeywordRepository CreateRepository(ReceiptDbContext context)
        => new(context, NullLogger<CommodityCategoryKeywordRepository>.Instance);

    private static CommodityCategoryKeywordEntity Entity(int categoryId, string listType, string word, string normalizedWord)
        => new()
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            ListType = listType,
            Word = word,
            NormalizedWord = normalizedWord
        };
}
