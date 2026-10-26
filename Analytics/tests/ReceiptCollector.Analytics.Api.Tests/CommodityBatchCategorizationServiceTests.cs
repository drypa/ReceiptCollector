using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты порционного suggest и пакетного сохранения категорий (T-05, план 10.2, ADR-028 п. 1–4, 8, 11).
/// Дедлайн проверяется через <see cref="FakeTimeProvider"/> — без реальных задержек.
/// </summary>
public class CommodityBatchCategorizationServiceTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(25);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly ICommodityRepository _commodityRepository = Substitute.For<ICommodityRepository>();
    private readonly ICommodityCategoryCache _cache = Substitute.For<ICommodityCategoryCache>();
    private readonly IPrecategorizedCommodityRepository _precategorizedRepository =
        Substitute.For<IPrecategorizedCommodityRepository>();
    private readonly IAiClient _aiClient = Substitute.For<IAiClient>();

    public CommodityBatchCategorizationServiceTests()
    {
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal));
    }

    private CommodityBatchCategorizationService CreateService(
        AiOptions? aiOptions = null,
        TimeSpan? suggestTimeout = null,
        TimeProvider? timeProvider = null) =>
        new(
            _commodityRepository,
            _cache,
            new CommodityCategoryResolver(
                _cache,
                _precategorizedRepository,
                _aiClient,
                Options.Create(aiOptions ?? CreateAiOptions()),
                NullLogger<CommodityCategoryResolver>.Instance),
            Options.Create(aiOptions ?? CreateAiOptions()),
            Options.Create(new CommodityBatchCategorizationOptions
            {
                SuggestTimeout = suggestTimeout ?? DefaultTimeout
            }),
            timeProvider ?? TimeProvider.System,
            NullLogger<CommodityBatchCategorizationService>.Instance);

    private static AiOptions CreateAiOptions(string? baseUrl = "http://localhost") => new()
    {
        BaseUrl = baseUrl,
        Concurrency = 3
    };

    private static Commodity CreateCommodity(string name, CommodityCategory? category = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            1m,
            10m,
            0,
            0m,
            category is { } value ? new Category((int)value, CommodityCategoryHelper.GetDisplayName(value)) : null);

    private void ArrangeOwner(params Commodity[] items)
    {
        _commodityRepository
            .GetByOwnerAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(items.ToList());
    }

    private void ArrangeCacheHit(string normalizedName, CommodityCategory category)
    {
        _cache.TryGet(normalizedName, out Arg.Any<CommodityCategory>())
            .Returns(callInfo =>
            {
                callInfo[1] = category;
                return true;
            });
    }

    private void ArrangeAiSuccess(string name, CommodityCategory category)
    {
        _aiClient
            .SuggestCategoryAsync(name, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(category));
    }

    private void ArrangeAiHangs()
    {
        _aiClient
            .SuggestCategoryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, callInfo.Arg<CancellationToken>());
                return null!;
            });
    }

    private static ReceiptItemCategorizationResult SingleFor(
        CommodityBatchCategorizationResult result,
        Commodity commodity) =>
        result.Items.Single(item => item.CommodityId == commodity.Id);

    // ---------------- Suggest ----------------

    [Fact]
    public async Task Suggest_uses_existing_category_without_cache_precategorized_or_ai()
    {
        var commodity = CreateCommodity("Молоко", CommodityCategory.Dairy);
        ArrangeOwner(commodity);

        var result = await CreateService().SuggestAsync(_userId, [commodity.Id], CancellationToken.None);

        var item = SingleFor(result, commodity);
        Assert.Equal((int)CommodityCategory.Dairy, item.CategoryId);
        Assert.Equal(CategorizationSourceNames.Existing, item.Source);
        Assert.Null(item.Error);

        _cache.DidNotReceiveWithAnyArgs().TryGet(default!, out _);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs()
            .GetCategoriesByExactNamesAsync(default!, default);
        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
    }

    [Fact]
    public async Task Suggest_uses_cache_with_display_name_from_helper()
    {
        var commodity = CreateCommodity("АИ-95-К5");
        ArrangeOwner(commodity);
        ArrangeCacheHit("аи-95-к5", CommodityCategory.Fuel);

        var result = await CreateService().SuggestAsync(_userId, [commodity.Id], CancellationToken.None);

        var item = SingleFor(result, commodity);
        Assert.Equal((int)CommodityCategory.Fuel, item.CategoryId);
        Assert.Equal(CommodityCategoryHelper.GetDisplayName(CommodityCategory.Fuel), item.CategoryName);
        Assert.Equal(CategorizationSourceNames.Cache, item.Source);
        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
    }

    [Fact]
    public async Task Suggest_reads_precategorized_queue_once_per_request()
    {
        var first = CreateCommodity("Чай");
        var second = CreateCommodity("Кофе");
        ArrangeOwner(first, second);
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal)
            {
                ["Чай"] = CommodityCategory.Beverages
            });
        ArrangeAiSuccess("Кофе", CommodityCategory.Beverages);

        var result = await CreateService().SuggestAsync(
            _userId,
            [first.Id, second.Id],
            CancellationToken.None);

        Assert.Equal(CategorizationSourceNames.Precategorized, SingleFor(result, first).Source);
        Assert.Equal((int)CommodityCategory.Beverages, SingleFor(result, first).CategoryId);
        Assert.Equal(CategorizationSourceNames.Ai, SingleFor(result, second).Source);

        await _precategorizedRepository.Received(1)
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_deduplicates_same_name_into_one_ai_call()
    {
        var first = CreateCommodity("Молоко");
        var second = CreateCommodity("Молоко");
        var third = CreateCommodity("Молоко");
        ArrangeOwner(first, second, third);
        ArrangeAiSuccess("Молоко", CommodityCategory.Dairy);

        var result = await CreateService().SuggestAsync(
            _userId,
            [first.Id, second.Id, third.Id],
            CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item =>
        {
            Assert.Equal((int)CommodityCategory.Dairy, item.CategoryId);
            Assert.Equal(CategorizationSourceNames.Ai, item.Source);
        });

        await _aiClient.Received(1)
            .SuggestCategoryAsync("Молоко", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_without_ai_base_url_returns_200_compatible_partial_result_and_ai_available_false()
    {
        var cached = CreateCommodity("Молоко");
        var unresolved = CreateCommodity("Чайник электрический");
        ArrangeOwner(cached, unresolved);
        ArrangeCacheHit("молоко", CommodityCategory.Dairy);

        var result = await CreateService(CreateAiOptions(baseUrl: " ")).SuggestAsync(
            _userId,
            [cached.Id, unresolved.Id],
            CancellationToken.None);

        Assert.False(result.AiAvailable);
        Assert.Equal(CategorizationSourceNames.Cache, SingleFor(result, cached).Source);

        var item = SingleFor(result, unresolved);
        Assert.Null(item.CategoryId);
        Assert.Equal(CategorizationSourceNames.Undefined, item.Source);
        Assert.Equal("ИИ не настроен.", item.Error);

        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
    }

    [Fact]
    public async Task Suggest_marks_only_failed_name_undefined_on_ai_client_exception()
    {
        var failing = CreateCommodity("Сломанный товар");
        var ok = CreateCommodity("Молоко");
        ArrangeOwner(failing, ok);

        _aiClient
            .SuggestCategoryAsync("Сломанный товар", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AiClientException("boom"));
        ArrangeAiSuccess("Молоко", CommodityCategory.Dairy);

        var result = await CreateService().SuggestAsync(
            _userId,
            [failing.Id, ok.Id],
            CancellationToken.None);

        var failedItem = SingleFor(result, failing);
        Assert.Equal(CategorizationSourceNames.Undefined, failedItem.Source);
        Assert.Equal("AI категория не определена.", failedItem.Error);

        Assert.Equal((int)CommodityCategory.Dairy, SingleFor(result, ok).CategoryId);
        Assert.Equal(CategorizationSourceNames.Ai, SingleFor(result, ok).Source);
    }

    [Fact]
    public async Task Suggest_returns_partial_result_when_request_budget_expires()
    {
        var cached = CreateCommodity("Молоко");
        var hanging = CreateCommodity("Медленный товар");
        ArrangeOwner(cached, hanging);
        ArrangeCacheHit("молоко", CommodityCategory.Dairy);
        ArrangeAiHangs();

        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(
            suggestTimeout: TimeSpan.FromMilliseconds(50),
            timeProvider: fakeTime);

        var suggestTask = service.SuggestAsync(_userId, [cached.Id, hanging.Id], CancellationToken.None);

        // Бюджет запроса истекает — отменяются только вызовы ИИ.
        fakeTime.Advance(TimeSpan.FromSeconds(1));
        var result = await suggestTask;

        Assert.Equal(CategorizationSourceNames.Cache, SingleFor(result, cached).Source);

        var item = SingleFor(result, hanging);
        Assert.Null(item.CategoryId);
        Assert.Equal(CategorizationSourceNames.Undefined, item.Source);
        Assert.Equal("ИИ не ответил за отведённое время.", item.Error);
        Assert.True(result.AiAvailable);
    }

    [Fact]
    public async Task Suggest_propagates_client_cancellation_instead_of_partial_result()
    {
        var commodity = CreateCommodity("Медленный товар");
        ArrangeOwner(commodity);
        ArrangeAiHangs();

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        var suggestTask = service.SuggestAsync(_userId, [commodity.Id], cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => suggestTask);
    }

    [Fact]
    public async Task Suggest_marks_blank_name_undefined_without_calling_ai()
    {
        var blank = CreateCommodity("   ");
        var normal = CreateCommodity("Молоко");
        ArrangeOwner(blank, normal);
        ArrangeAiSuccess("Молоко", CommodityCategory.Dairy);

        var result = await CreateService().SuggestAsync(_userId, [blank.Id, normal.Id], CancellationToken.None);

        var item = SingleFor(result, blank);
        Assert.Equal(CategorizationSourceNames.Undefined, item.Source);
        Assert.Equal("Название товара не заполнено.", item.Error);

        await _aiClient.Received(1)
            .SuggestCategoryAsync("Молоко", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        await _aiClient.DidNotReceive()
            .SuggestCategoryAsync("   ", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_with_more_than_20_unique_names_throws_argument_exception()
    {
        var commodities = Enumerable.Range(0, 21)
            .Select(index => CreateCommodity($"Товар {index}"))
            .ToArray();
        ArrangeOwner(commodities);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().SuggestAsync(_userId, commodities.Select(c => c.Id).ToList(), CancellationToken.None));

        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
    }

    [Fact]
    public async Task Suggest_is_read_only()
    {
        var commodity = CreateCommodity("Молоко");
        ArrangeOwner(commodity);
        ArrangeAiSuccess("Молоко", CommodityCategory.Dairy);

        await CreateService().SuggestAsync(_userId, [commodity.Id], CancellationToken.None);

        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .UpdateCategoriesAsync(default, default!, default);
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().TryAddProposalAsync(default!, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().UpdateProposedCategoryAsync(default, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().ConfirmAsync(default, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().DeleteProposedAsync(default, default);
    }

    [Fact]
    public async Task Suggest_omits_unknown_and_foreign_ids()
    {
        var owned = CreateCommodity("Молоко");
        ArrangeOwner(owned);
        ArrangeAiSuccess("Молоко", CommodityCategory.Dairy);

        var result = await CreateService().SuggestAsync(
            _userId,
            [owned.Id, Guid.NewGuid()],
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(owned.Id, item.CommodityId);
    }

    // ---------------- Apply ----------------

    [Fact]
    public async Task Apply_updates_row_and_adds_normalized_name_to_cache()
    {
        var commodity = CreateCommodity("Молоко");
        ArrangeOwner(commodity);
        _commodityRepository
            .UpdateCategoriesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Guid, CommodityCategory>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var updated = await CreateService().ApplyAsync(
            _userId,
            [new CommodityCategoryChange(commodity.Id, CommodityCategory.Dairy)],
            CancellationToken.None);

        Assert.Equal(1, updated);
        _cache.Received(1).TryAdd("молоко", CommodityCategory.Dairy);

        await _commodityRepository.Received(1).UpdateCategoriesAsync(
            _userId,
            Arg.Is<IReadOnlyDictionary<Guid, CommodityCategory>>(changes =>
                changes.Count == 1 && changes[commodity.Id] == CommodityCategory.Dairy),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_same_value_returns_zero_and_does_not_touch_cache()
    {
        var commodity = CreateCommodity("Молоко", CommodityCategory.Dairy);
        ArrangeOwner(commodity);
        _commodityRepository
            .UpdateCategoriesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Guid, CommodityCategory>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var updated = await CreateService().ApplyAsync(
            _userId,
            [new CommodityCategoryChange(commodity.Id, CommodityCategory.Dairy)],
            CancellationToken.None);

        Assert.Equal(0, updated);
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task Apply_propagates_not_accessible_exception_without_cache_update()
    {
        var commodity = CreateCommodity("Молоко");
        ArrangeOwner(commodity);
        _commodityRepository
            .UpdateCategoriesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Guid, CommodityCategory>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CommoditiesNotAccessibleException(1));

        await Assert.ThrowsAsync<CommoditiesNotAccessibleException>(() =>
            CreateService().ApplyAsync(
                _userId,
                [new CommodityCategoryChange(commodity.Id, CommodityCategory.Dairy)],
                CancellationToken.None));

        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task Apply_throws_when_change_contains_undefined_category_and_does_not_touch_repository()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().ApplyAsync(
                _userId,
                [new CommodityCategoryChange(Guid.NewGuid(), CommodityCategory.Undefined)],
                CancellationToken.None));

        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .GetByOwnerAsync(default, default!, default);
        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .UpdateCategoriesAsync(default, default!, default);
    }

    [Fact]
    public async Task Apply_throws_when_pre_read_finds_inaccessible_rows_and_writes_nothing()
    {
        var commodity = CreateCommodity("Молоко");
        // Владелец вернул только одну строку из двух запрошенных.
        ArrangeOwner(commodity);

        await Assert.ThrowsAsync<CommoditiesNotAccessibleException>(() =>
            CreateService().ApplyAsync(
                _userId,
                [
                    new CommodityCategoryChange(commodity.Id, CommodityCategory.Dairy),
                    new CommodityCategoryChange(Guid.NewGuid(), CommodityCategory.Dairy)
                ],
                CancellationToken.None));

        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .UpdateCategoriesAsync(default, default!, default);
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task Apply_does_not_write_to_precategorized_queue()
    {
        var commodity = CreateCommodity("Молоко");
        ArrangeOwner(commodity);
        _commodityRepository
            .UpdateCategoriesAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<Guid, CommodityCategory>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        await CreateService().ApplyAsync(
            _userId,
            [new CommodityCategoryChange(commodity.Id, CommodityCategory.Dairy)],
            CancellationToken.None);

        await _precategorizedRepository.DidNotReceiveWithAnyArgs().TryAddProposalAsync(default!, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().UpdateProposedCategoryAsync(default, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().ConfirmAsync(default, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().DeleteProposedAsync(default, default);
    }

    [Fact]
    public async Task Apply_with_no_changes_returns_zero_without_repository_calls()
    {
        var updated = await CreateService().ApplyAsync(_userId, [], CancellationToken.None);

        Assert.Equal(0, updated);
        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .GetByOwnerAsync(default, default!, default);
        await _commodityRepository.DidNotReceiveWithAnyArgs()
            .UpdateCategoriesAsync(default, default!, default);
    }
}
