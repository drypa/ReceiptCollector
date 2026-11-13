using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты админского сервиса слов категоризации (T-04, ADR-035 п. 6–7, FR-4/FR-5):
/// сохранение слова «как введено» (кроме trim) с нормализацией для ключа дубликата,
/// дубликат (включая гонку 23505 → Duplicate, а не 500), лимит 200 по паре «категория +
/// список», удаление по id, инвалидация кэша строго после успешной мутации и устойчивость
/// мутации к сбою перечитывания (ADR-035 п. 6, NFR-4).
/// </summary>
public sealed class CommodityCategoryKeywordServiceTests
{
    private readonly ICommodityCategoryKeywordRepository _repository =
        Substitute.For<ICommodityCategoryKeywordRepository>();

    private readonly ICommodityCategoryKeywordCache _cache =
        Substitute.For<ICommodityCategoryKeywordCache>();

    [Fact]
    public async Task AddAsync_trims_word_keeps_case_and_stores_normalized_key()
    {
        CommodityCategoryKeyword? captured = null;
        ArrangeAddSucceeds(captured: k => captured = k);

        var result = await CreateService()
            .AddAsync((int)CommodityCategory.DriedFruits, "boost", "  Курага  ", CancellationToken.None);

        Assert.Equal(AddKeywordOutcome.Added, result.Outcome);
        // FR-5: слово хранится как введено, кроме обрамляющих пробелов; регистр сохранён.
        Assert.Equal("Курага", captured!.Word);
        // normalized_word — ключ case-insensitive уникальности (trim + lowercase + collapse).
        Assert.Equal("курага", captured.NormalizedWord);
        Assert.Equal(KeywordListType.Boost, captured.ListType);
        Assert.Equal(CommodityCategory.DriedFruits, captured.Category);
        Assert.NotEqual(Guid.Empty, captured.Id);

        // Результат несёт созданный элемент для тела ответа POST (ADR-035 п. 7).
        Assert.Equal(captured.Id, result.Id);
        Assert.Equal((int)CommodityCategory.DriedFruits, result.CategoryId);
        Assert.Equal("boost", result.ListType);
        Assert.Equal("Курага", result.Word);

        // Дубликат адресуется нормализованным словом по паре «категория + список».
        await _repository.Received(1).ExistsAsync(
            CommodityCategory.DriedFruits, KeywordListType.Boost, "курага", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddAsync_case_insensitive_duplicate_returns_duplicate_without_insert()
    {
        // «КУРАГА» и «курага» нормализуются одинаково → Exists даёт true до вставки.
        _repository.ExistsAsync(
                CommodityCategory.DriedFruits, KeywordListType.Boost, "курага", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateService()
            .AddAsync((int)CommodityCategory.DriedFruits, "boost", "КУРАГА", CancellationToken.None);

        Assert.Equal(AddKeywordOutcome.Duplicate, result.Outcome);
        Assert.Null(result.Id);
        await _repository.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    [Fact]
    public async Task AddAsync_race_unique_violation_maps_to_duplicate_not_error()
    {
        // Гонка двух вкладок: предпроверка дубликата прошла, но репозиторий поймал 23505 → false.
        // Это Duplicate (409), а не 500 (ADR-035 п. 7).
        ArrangeAddSucceeds();
        _repository.AddAsync(Arg.Any<CommodityCategoryKeyword>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateService()
            .AddAsync((int)CommodityCategory.DriedFruits, "exclude", "изюм", CancellationToken.None);

        Assert.Equal(AddKeywordOutcome.Duplicate, result.Outcome);
        // Мутации не было — кэш не инвалидируем.
        await _cache.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    [Theory]
    [InlineData(199, AddKeywordOutcome.Added)]
    [InlineData(200, AddKeywordOutcome.LimitReached)]
    public async Task AddAsync_limit_is_counted_per_category_and_list(int currentCount, AddKeywordOutcome expected)
    {
        ArrangeAddSucceeds();
        _repository.CountAsync(CommodityCategory.DriedFruits, KeywordListType.Boost, Arg.Any<CancellationToken>())
            .Returns(currentCount);

        var result = await CreateService()
            .AddAsync((int)CommodityCategory.DriedFruits, "boost", "чернослив", CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        // Лимит адресный: считается по паре «категория + список», а не по всем словам категории.
        await _repository.Received(1).CountAsync(
            CommodityCategory.DriedFruits, KeywordListType.Boost, Arg.Any<CancellationToken>());
        if (expected == AddKeywordOutcome.LimitReached)
        {
            await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        }
    }

    [Fact]
    public async Task DeleteAsync_existing_returns_deleted_and_missing_returns_not_found()
    {
        var id = Guid.NewGuid();
        _repository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        Assert.Equal(DeleteKeywordOutcome.Deleted,
            await CreateService().DeleteAsync(id, CancellationToken.None));

        _repository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(false);

        Assert.Equal(DeleteKeywordOutcome.NotFound,
            await CreateService().DeleteAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_called_after_committed_mutation_only()
    {
        // Успешное добавление → ровно одна инвалидация после commit.
        ArrangeAddSucceeds();
        await CreateService().AddAsync((int)CommodityCategory.DriedFruits, "boost", "изюм", CancellationToken.None);
        await _cache.Received(1).RefreshAsync(Arg.Any<CancellationToken>());

        // Успешное удаление → инвалидация.
        var id = Guid.NewGuid();
        _repository.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);
        await CreateService().DeleteAsync(id, CancellationToken.None);
        await _cache.Received(2).RefreshAsync(Arg.Any<CancellationToken>());

        // Duplicate / LimitReached / NotFound — мутации не было, кэш не трогаем.
        _repository.ClearReceivedCalls();
        _cache.ClearReceivedCalls();

        _repository.ExistsAsync(
                Arg.Any<CommodityCategory>(), Arg.Any<KeywordListType>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        await CreateService().AddAsync((int)CommodityCategory.DriedFruits, "boost", "изюм", CancellationToken.None);

        _repository.ClearReceivedCalls();
        _repository.ExistsAsync(
                Arg.Any<CommodityCategory>(), Arg.Any<KeywordListType>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
        _repository.CountAsync(
                Arg.Any<CommodityCategory>(), Arg.Any<KeywordListType>(), Arg.Any<CancellationToken>())
            .Returns(CommodityCategoryKeywordRules.MaxWordsPerList);
        await CreateService().AddAsync((int)CommodityCategory.DriedFruits, "boost", "изюм", CancellationToken.None);

        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        await CreateService().DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        await _cache.DidNotReceiveWithAnyArgs().RefreshAsync(default);
    }

    [Fact]
    public async Task Failed_cache_refresh_does_not_change_mutation_outcome_and_cache_stays_dirty()
    {
        // Фейк имитирует сбой перечитывания (в штатной реализации — «грязный» флаг без
        // исключения; защитный catch сервиса покрывает и нарушение контракта): мутация уже
        // зафиксирована в БД и обязана остаться успешной (ADR-035 п. 6, NFR-4).
        var cache = new ThrowingRefreshCache();
        var service = new CommodityCategoryKeywordService(
            _repository, cache, NullLogger<CommodityCategoryKeywordService>.Instance);
        ArrangeAddSucceeds();

        var added = await service.AddAsync(
            (int)CommodityCategory.DriedFruits, "boost", "изюм", CancellationToken.None);
        Assert.Equal(AddKeywordOutcome.Added, added.Outcome);
        Assert.True(cache.Dirty);

        _repository.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var deleted = await service.DeleteAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(DeleteKeywordOutcome.Deleted, deleted);
        Assert.True(cache.Dirty);
    }

    [Theory]
    [InlineData(0, "boost", "изюм", "categoryId")]
    [InlineData(999, "boost", "изюм", "categoryId")]
    [InlineData(42, "other", "изюм", "listType")]
    [InlineData(42, null, "изюм", "listType")]
    [InlineData(42, "boost", " и ", "word")]
    [InlineData(42, "boost", "   ", "word")]
    [InlineData(42, "boost", null, "word")]
    public async Task AddAsync_defensive_guards_reject_invalid_inputs_in_adr_order(
        int categoryId,
        string? listType,
        string? word,
        string expectedParamName)
    {
        // Эндпоинт отвечает на эти случаи 400 до вызова сервиса; сервис повторяет проверки
        // защитным ArgumentException — порядок и смысл по таблице ADR-035 п. 7.
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().AddAsync(categoryId, listType, word, CancellationToken.None));

        Assert.Equal(expectedParamName, exception.ParamName);
        await _repository.DidNotReceiveWithAnyArgs().ExistsAsync(default, default, default!, default);
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task AddAsync_word_with_expanding_lowercase_never_reaches_storage_with_long_normalized_word()
    {
        // «İ» (U+0130): страховка на случай смены нормализации (например, full case folding) —
        // тогда «İ» даёт два символа («i» + комбинирующая точка сверху), и 33 символа станут 66
        // после нормализации — длиннее varchar(64) схемы (в БД это 22001, не 23505, который
        // репозиторий не перехватывает — было бы 500 вместо 400). На текущем рантайме .NET
        // ToLowerInvariant расширение не наблюдается. Сервис такое слово отклоняет как
        // невалидное — тот же ArgumentException("word"), что у проверки длины. Тест не зависит
        // от рантайма: либо отклонение, либо в хранилище не уходит нормализация длиннее лимита.
        var expandingWord = new string('\u0130', 33);
        CommodityCategoryKeyword? captured = null;
        ArrangeAddSucceeds(k => captured = k);

        AddKeywordResult? result = null;
        ArgumentException? thrown = null;
        try
        {
            result = await CreateService()
                .AddAsync((int)CommodityCategory.DriedFruits, "boost", expandingWord, CancellationToken.None);
        }
        catch (ArgumentException exception)
        {
            thrown = exception;
        }

        if (thrown is not null)
        {
            Assert.Equal("word", thrown.ParamName);
            Assert.Null(captured);
        }
        else
        {
            Assert.Equal(AddKeywordOutcome.Added, result!.Outcome);
            Assert.True(
                captured!.NormalizedWord.EnumerateRunes().Count() <= CommodityCategoryKeywordRules.MaxWordLength,
                "В хранилище не должно оказаться normalized_word длиннее varchar(64) схемы.");
        }
    }

    [Fact]
    public void NormalizedWordFitsColumn_rejects_normalized_form_longer_than_schema_column()
    {
        // Границы предиката не зависят от поведения ToLowerInvariant на конкретном рантайме:
        // 64 символа помещаются, 65 — нет (иначе в БД 22001 вместо 400).
        Assert.True(CommodityCategoryKeywordService.NormalizedWordFitsColumn(new string('а', 64)));
        Assert.False(CommodityCategoryKeywordService.NormalizedWordFitsColumn(new string('а', 65)));

        // Суррогатная пара — один символ (char_length в PostgreSQL считает кодовые точки).
        Assert.True(CommodityCategoryKeywordService.NormalizedWordFitsColumn(
            string.Concat(Enumerable.Repeat("😀", 64))));
        Assert.False(CommodityCategoryKeywordService.NormalizedWordFitsColumn(
            string.Concat(Enumerable.Repeat("😀", 65))));
    }

    [Fact]
    public async Task GetAllAsync_returns_all_categories_without_undefined_in_declaration_order()
    {
        var dateId = Guid.NewGuid();
        var driedApricotId = Guid.NewGuid();
        var raisinId = Guid.NewGuid();
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CommodityCategoryKeyword(dateId, CommodityCategory.DriedFruits, KeywordListType.Boost, "финик", "финик"),
            new CommodityCategoryKeyword(driedApricotId, CommodityCategory.DriedFruits, KeywordListType.Boost, "Курага", "курага"),
            new CommodityCategoryKeyword(raisinId, CommodityCategory.DriedFruits, KeywordListType.Boost, "изюм", "изюм"),
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.Fruits, KeywordListType.Exclude, "изюм", "изюм"),
        });

        var categories = (await CreateService().GetAllAsync(CancellationToken.None)).ToList();

        var expected = CommodityCategoryHelper.GetAll()
            .Where(category => category.Id != CommodityCategory.Undefined)
            .ToList();

        // Все категории реестра (включая пустые списки), порядок — порядок объявления (ADR-035 п. 7).
        Assert.Equal(expected.Select(category => (int)category.Id), categories.Select(dto => dto.CategoryId));
        Assert.DoesNotContain(categories, dto => dto.CategoryId == (int)CommodityCategory.Undefined);

        var food = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.Food);
        Assert.Empty(food.Boost);
        Assert.Empty(food.Exclude);

        var driedFruits = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.DriedFruits);
        Assert.Equal("DriedFruits", driedFruits.CategoryKey);
        Assert.Equal("Сухофрукты", driedFruits.CategoryName);
        Assert.Equal("Продукты", driedFruits.Group);
        // Слова — по word OrdinalIgnoreCase (и < к < ф), регистр сохранён; каждое слово
        // несёт Id строки для адресного DELETE (ADR-035 G1: «id уже есть в выдаче GET»).
        Assert.Equal(new[] { "изюм", "Курага", "финик" }, driedFruits.Boost.Select(item => item.Word));
        Assert.Equal(new[] { raisinId, driedApricotId, dateId }, driedFruits.Boost.Select(item => item.Id));
        Assert.Empty(driedFruits.Exclude);

        var fruits = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.Fruits);
        Assert.Equal(new[] { "изюм" }, fruits.Exclude.Select(item => item.Word));
        Assert.Empty(fruits.Boost);

        // Категория вне групп (legacy-диапазон) — пустая строка группы (GetGroup).
        var clothing = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.Clothing);
        Assert.Equal(string.Empty, clothing.Group);
    }

    [Fact]
    public async Task GetAllAsync_word_sort_uses_ordinal_tiebreak_after_ignore_case()
    {
        // Ничья OrdinalIgnoreCase (разный регистр одного слова) разрешается Ordinal:
        // «Apple» (A=65) раньше «apple» (a=97). Детерминированность выдачи (ADR-035 п. 7).
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "apple", "apple"),
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "Apple", "apple"),
        });

        var categories = (await CreateService().GetAllAsync(CancellationToken.None)).ToList();

        var driedFruits = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.DriedFruits);
        Assert.Equal(new[] { "Apple", "apple" }, driedFruits.Boost.Select(item => item.Word));
    }

    private void ArrangeAddSucceeds(Action<CommodityCategoryKeyword>? captured = null)
    {
        _repository.ExistsAsync(
                Arg.Any<CommodityCategory>(), Arg.Any<KeywordListType>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
        _repository.CountAsync(
                Arg.Any<CommodityCategory>(), Arg.Any<KeywordListType>(), Arg.Any<CancellationToken>())
            .Returns(0);
        _repository.AddAsync(
                captured is null
                    ? Arg.Any<CommodityCategoryKeyword>()
                    : Arg.Do<CommodityCategoryKeyword>(captured),
                Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private CommodityCategoryKeywordService CreateService()
        => new(_repository, _cache, NullLogger<CommodityCategoryKeywordService>.Instance);

    /// <summary>
    /// Фейк кэша: <c>RefreshAsync</c> бросает и выставляет «грязный» флаг — имитация сбоя
    /// перечитывания (штатная реализация деградирует в тот же флаг без исключения, T-03).
    /// </summary>
    private sealed class ThrowingRefreshCache : ICommodityCategoryKeywordCache
    {
        public bool Dirty { get; private set; }

        public CommodityCategoryKeywordSnapshot GetSnapshot() => CommodityCategoryKeywordSnapshot.Empty;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Dirty = true;
            return Task.FromException(new InvalidOperationException("cache reload failed"));
        }
    }
}
