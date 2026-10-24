using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CommodityCategorizationServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _receiptId = Guid.NewGuid();
    private readonly IReceiptRepository _receiptRepository = Substitute.For<IReceiptRepository>();
    private readonly ICommodityRepository _commodityRepository = Substitute.For<ICommodityRepository>();
    private readonly ICommodityCategoryCache _cache = Substitute.For<ICommodityCategoryCache>();
    private readonly IPrecategorizedCommodityRepository _precategorizedRepository = Substitute.For<IPrecategorizedCommodityRepository>();
    private readonly IAiClient _aiClient = Substitute.For<IAiClient>();

    public CommodityCategorizationServiceTests()
    {
        // ADR-027 (FR-9): по умолчанию очередь пуста — существующие сценарии не меняются.
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal));
    }

    private CommodityCategorizationService CreateService(AiOptions? options = null) =>
        new(
            _receiptRepository,
            _commodityRepository,
            _cache,
            _precategorizedRepository,
            _aiClient,
            Options.Create(options ?? new AiOptions { BaseUrl = "http://localhost", Concurrency = 3 }),
            NullLogger<CommodityCategorizationService>.Instance);

    private void ArrangeCacheHit(string normalized, CommodityCategory category)
    {
        _cache.TryGet(normalized, out Arg.Any<CommodityCategory>())
            .Returns(callInfo => { callInfo[1] = category; return true; });
    }

    private static Commodity CreateCommodity(Guid receiptId, string name, int? categoryId = null, string? categoryName = null)
    {
        var category = categoryId.HasValue ? new Category(categoryId.Value, categoryName ?? string.Empty) : null;
        return new Commodity(Guid.NewGuid(), receiptId, name, 1m, 10m, 0, 0m, category);
    }

    private void ArrangeReceipt(params Commodity[] items)
    {
        _receiptRepository.GetByIdAsync(_receiptId, _userId, Arg.Any<CancellationToken>())
            .Returns(new Receipt(_receiptId, _userId, Guid.NewGuid(), 100m, DateTime.UtcNow, "external", items));
    }

    [Fact]
    public async Task Suggest_uses_existing_category_without_ai_or_cache()
    {
        var item = CreateCommodity(_receiptId, "Молоко", (int)CommodityCategory.Dairy, "Молочные продукты");
        ArrangeReceipt(item);

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal((int)CommodityCategory.Dairy, suggestion.CategoryId);
        Assert.Equal(CategorizationSourceNames.Existing, suggestion.Source);
        Assert.Null(suggestion.Error);

        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
        // Приоритет «existing → cache → ai» (FR-2.1): кэш не опрашивается, если категория уже есть.
        _cache.DidNotReceiveWithAnyArgs().TryGet(default!, out _);
    }

    [Fact]
    public async Task Suggest_uses_cache_and_skips_ai_for_ai95k5()
    {
        // Сценарий «АИ-95-К5» (FR-2.2): позиция без категории, нормализованное название есть в кэше.
        var item = CreateCommodity(_receiptId, "АИ-95-К5");
        ArrangeReceipt(item);
        ArrangeCacheHit("аи-95-к5", CommodityCategory.Fuel);

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal(CategorizationSourceNames.Cache, suggestion.Source);
        Assert.Equal((int)CommodityCategory.Fuel, suggestion.CategoryId);
        Assert.Equal("Топливо", suggestion.CategoryName); // дисплейное имя из CommodityCategoryHelper (ADR 019)
        Assert.Null(suggestion.Error);

        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default); // ИИ не вызван
    }

    [Fact]
    public async Task Suggest_treats_undefined_category_as_missing_and_uses_cache()
    {
        var item = CreateCommodity(_receiptId, "Кефир", (int)CommodityCategory.Undefined, "Не указана");
        ArrangeReceipt(item);
        ArrangeCacheHit("кефир", CommodityCategory.Dairy);

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal((int)CommodityCategory.Dairy, suggestion.CategoryId);
        Assert.Equal("Молочные продукты", suggestion.CategoryName);
        Assert.Equal(CategorizationSourceNames.Cache, suggestion.Source);

        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
    }

    [Fact]
    public async Task Suggest_falls_back_to_ai_when_no_existing_category_and_no_cache()
    {
        var item = CreateCommodity(_receiptId, "Пельмени");
        ArrangeReceipt(item);

        _aiClient.SuggestCategoryAsync("Пельмени", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.ReadyMeals));

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal((int)CommodityCategory.ReadyMeals, suggestion.CategoryId);
        Assert.Equal(CategorizationSourceNames.Ai, suggestion.Source);

        await _aiClient.Received(1).SuggestCategoryAsync("Пельмени", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_marks_undefined_when_ai_fails()
    {
        var item = CreateCommodity(_receiptId, "Странный товар");
        ArrangeReceipt(item);

        _aiClient.SuggestCategoryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<AiSuggestionResult>>(Task.FromException<AiSuggestionResult>(new AiClientException("AI is down.")));

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal(CategorizationSourceNames.Undefined, suggestion.Source);
        Assert.Null(suggestion.CategoryId);
        Assert.NotNull(suggestion.Error);
    }

    [Fact]
    public async Task Suggest_preserves_original_item_order_and_deduplicates_ai_calls()
    {
        var milkA = CreateCommodity(_receiptId, "Молоко 2.5%");
        var milkB = CreateCommodity(_receiptId, "МОЛОКО  2.5%");
        var bread = CreateCommodity(_receiptId, "Хлеб");
        ArrangeReceipt(milkA, bread, milkB);

        _aiClient.SuggestCategoryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Food));

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);
        var itemResults = result.Items.ToList();

        Assert.Equal(3, itemResults.Count);
        Assert.Equal(milkA.Id, itemResults[0].CommodityId);
        Assert.Equal(bread.Id, itemResults[1].CommodityId);
        Assert.Equal(milkB.Id, itemResults[2].CommodityId);
        Assert.Equal(itemResults[0].CategoryId, itemResults[2].CategoryId);

        // Одинаковые нормализованные названия (молоко) не вызывают AI дважды.
        await _aiClient.Received(2).SuggestCategoryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_passes_catalog_of_categories_without_undefined_to_ai()
    {
        var item = CreateCommodity(_receiptId, "Бананы");
        ArrangeReceipt(item);

        _aiClient.SuggestCategoryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Fruits));

        var service = CreateService();
        await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        await _aiClient.Received(1).SuggestCategoryAsync(
            "Бананы",
            Arg.Is<IReadOnlyCollection<string>>(names =>
                names.Contains("Food") &&
                names.Contains("Dairy") &&
                !names.Contains("Undefined") &&
                // Новые категории автоматически попадают в реестр через GetAll() (FR-4)
                names.Contains("DriedFruits") &&
                names.Contains("Sauces") &&
                names.Contains("Spices") &&
                names.Contains("Sausages") &&
                names.Contains("Sweets") &&
                names.Contains("CannedFood") &&
                names.Contains("CraftSupplies") &&
                names.Contains("Furniture") &&
                names.Contains("HomeAppliances") &&
                names.Contains("Snacks") &&
                names.Contains("FitnessAndSports") &&
                names.Count == CommodityCategoryHelper.GetAll().Count(c => c.Id != CommodityCategory.Undefined)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_throws_ReceiptNotFoundException_for_unknown_receipt()
    {
        _receiptRepository.GetByIdAsync(_receiptId, _userId, Arg.Any<CancellationToken>()).Returns((Receipt?)null);

        var service = CreateService();

        await Assert.ThrowsAsync<ReceiptNotFoundException>(() =>
            service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None));
    }

    [Fact]
    public async Task Suggest_does_not_write_anything()
    {
        var item = CreateCommodity(_receiptId, "Товар без категории");
        ArrangeReceipt(item);

        _aiClient.SuggestCategoryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Other));

        var service = CreateService();
        await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        await _commodityRepository.DidNotReceiveWithAnyArgs().UpdateCategoryAsync(default, default, default);
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task ApplyConfirmedCategories_saves_categories_and_updates_cache()
    {
        var item = CreateCommodity(_receiptId, "Сыр");
        ArrangeReceipt(item);

        var assignments = new[]
        {
            new ConfirmedCategoryAssignment(item.Id, CommodityCategory.Dairy)
        };

        var service = CreateService();
        var updated = await service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, assignments, CancellationToken.None);

        Assert.Equal(1, updated);
        await _commodityRepository.Received(1).UpdateCategoryAsync(item.Id, CommodityCategory.Dairy, Arg.Any<CancellationToken>());
        _cache.Received(1).TryAdd("сыр", CommodityCategory.Dairy); // FR-1.3
    }

    [Fact]
    public async Task ApplyConfirmedCategories_null_category_resets_without_cache_update()
    {
        var item = CreateCommodity(_receiptId, "Старый товар", (int)CommodityCategory.Electronics, "Электроника");
        ArrangeReceipt(item);

        var assignments = new[] { new ConfirmedCategoryAssignment(item.Id, null) };

        var service = CreateService();
        var updated = await service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, assignments, CancellationToken.None);

        Assert.Equal(1, updated);
        await _commodityRepository.Received(1).UpdateCategoryAsync(item.Id, (CommodityCategory?)null, Arg.Any<CancellationToken>());
        // Сброс (null) в кэш не пишется и не удаляется (ADR 019, «Отрицательные последствия»).
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task ApplyConfirmedCategories_skips_commodities_not_belonging_to_receipt()
    {
        ArrangeReceipt(CreateCommodity(_receiptId, "Молоко"));
        var foreignId = Guid.NewGuid();

        var assignments = new[]
        {
            new ConfirmedCategoryAssignment(foreignId, CommodityCategory.Food),
            new ConfirmedCategoryAssignment(Guid.Empty, CommodityCategory.Food)
        };

        var service = CreateService();
        var updated = await service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, assignments, CancellationToken.None);

        Assert.Equal(0, updated);
        await _commodityRepository.DidNotReceiveWithAnyArgs().UpdateCategoryAsync(default, default, default);
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task ApplyConfirmedCategories_rejects_explicit_undefined_category()
    {
        ArrangeReceipt(CreateCommodity(_receiptId, "Молоко"));

        var assignments = new[] { new ConfirmedCategoryAssignment(Guid.NewGuid(), CommodityCategory.Undefined) };

        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, assignments, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyConfirmedCategories_throws_ReceiptNotFoundException_for_unknown_receipt()
    {
        _receiptRepository.GetByIdAsync(_receiptId, _userId, Arg.Any<CancellationToken>()).Returns((Receipt?)null);

        var service = CreateService();

        await Assert.ThrowsAsync<ReceiptNotFoundException>(() =>
            service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, new[] { new ConfirmedCategoryAssignment(Guid.NewGuid(), CommodityCategory.Food) }, CancellationToken.None));
    }

    [Fact]
    public async Task Suggest_uses_precategorized_queue_and_skips_ai()
    {
        // FR-9.1/9.4: наименование найдено в очереди → source = precategorized, ИИ не вызывается.
        var item = CreateCommodity(_receiptId, "Молоко 3,2% 1л");
        ArrangeReceipt(item);
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal)
            {
                ["Молоко 3,2% 1л"] = CommodityCategory.Dairy
            });

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal(CategorizationSourceNames.Precategorized, suggestion.Source);
        Assert.Equal((int)CommodityCategory.Dairy, suggestion.CategoryId);
        Assert.Equal("Молочные продукты", suggestion.CategoryName);
        Assert.Null(suggestion.Error);

        await _aiClient.DidNotReceiveWithAnyArgs().SuggestCategoryAsync(default!, default!, default);
    }

    [Fact]
    public async Task Suggest_keeps_priority_existing_then_cache_then_precategorized_then_ai()
    {
        // D13: existing → cache → precategorized → ai.
        var existing = CreateCommodity(_receiptId, "Существующая", (int)CommodityCategory.Electronics, "Электроника");
        var cached = CreateCommodity(_receiptId, "Из кэша");
        var queued = CreateCommodity(_receiptId, "Из очереди");
        var ai = CreateCommodity(_receiptId, "Только ИИ");
        ArrangeReceipt(existing, cached, queued, ai);

        ArrangeCacheHit("из кэша", CommodityCategory.Food);
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal)
            {
                ["Из очереди"] = CommodityCategory.Bakery,
                // Наименования, уже обслуженные existing/cache, в очередь не попадают (FR-9.3).
                ["Из кэша"] = CommodityCategory.Meat
            });
        _aiClient.SuggestCategoryAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Other));

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);
        var items = result.Items.ToList();

        Assert.Equal(CategorizationSourceNames.Existing, items[0].Source);
        Assert.Equal(CategorizationSourceNames.Cache, items[1].Source);
        Assert.Equal(CategorizationSourceNames.Precategorized, items[2].Source);
        Assert.Equal(CategorizationSourceNames.Ai, items[3].Source);
        Assert.Equal((int)CommodityCategory.Bakery, items[2].CategoryId);

        await _aiClient.Received(1).SuggestCategoryAsync("Только ИИ", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_queries_queue_once_with_distinct_pending_names()
    {
        // FR-9.3: один batch-запрос на чек по различным наименованиям без existing/cache.
        var first = CreateCommodity(_receiptId, "Хлеб");
        var second = CreateCommodity(_receiptId, "Молоко");
        var duplicate = CreateCommodity(_receiptId, "Хлеб");
        var withExisting = CreateCommodity(_receiptId, "Сыр", (int)CommodityCategory.Dairy, "Молочные продукты");
        ArrangeReceipt(first, second, duplicate, withExisting);

        var service = CreateService();
        await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        await _precategorizedRepository.Received(1).GetCategoriesByExactNamesAsync(
            Arg.Is<IReadOnlyCollection<string>>(names =>
                names.Count == 2 && names.Contains("Хлеб") && names.Contains("Молоко") && !names.Contains("Сыр")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_queue_lookup_is_read_only()
    {
        // FR-9.5: suggest не пишет ни в commodities, ни в очередь, ни в кэш.
        var item = CreateCommodity(_receiptId, "Кефир");
        ArrangeReceipt(item);
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, CommodityCategory>(StringComparer.Ordinal)
            {
                ["Кефир"] = CommodityCategory.Dairy
            });

        var service = CreateService();
        await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        await _precategorizedRepository.DidNotReceiveWithAnyArgs().TryAddProposalAsync(default!, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().ConfirmAsync(default, default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().DeleteProposedAsync(default, default);
        await _precategorizedRepository.DidNotReceiveWithAnyArgs().UpdateProposedCategoryAsync(default, default, default);
        await _commodityRepository.DidNotReceiveWithAnyArgs().UpdateCategoryAsync(default, default, default);
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task Suggest_falls_back_to_ai_when_queue_read_fails()
    {
        // FR-9.6: ошибка чтения очереди → Warning + деградация к ИИ, без исключения.
        var item = CreateCommodity(_receiptId, "Йогурт");
        ArrangeReceipt(item);
        _precategorizedRepository
            .GetCategoriesByExactNamesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyDictionary<string, CommodityCategory>>(_ => throw new InvalidOperationException("queue is down"));
        _aiClient.SuggestCategoryAsync("Йогурт", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Dairy));

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal(CategorizationSourceNames.Ai, suggestion.Source);
        Assert.Equal((int)CommodityCategory.Dairy, suggestion.CategoryId);
    }

    [Fact]
    public async Task Suggest_uses_ai_when_queue_has_no_matching_name()
    {
        var item = CreateCommodity(_receiptId, "Сок");
        ArrangeReceipt(item);
        _aiClient.SuggestCategoryAsync("Сок", Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AiSuggestionResult(CommodityCategory.Beverages));

        var service = CreateService();
        var result = await service.SuggestForReceiptAsync(_userId, _receiptId, CancellationToken.None);

        var suggestion = Assert.Single(result.Items);
        Assert.Equal(CategorizationSourceNames.Ai, suggestion.Source);
    }
}