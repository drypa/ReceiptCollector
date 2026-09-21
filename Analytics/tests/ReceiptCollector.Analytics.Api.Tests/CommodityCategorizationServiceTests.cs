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
    private readonly ICategoryAssignmentRepository _categoryAssignmentRepository = Substitute.For<ICategoryAssignmentRepository>();
    private readonly IAiClient _aiClient = Substitute.For<IAiClient>();

    private CommodityCategorizationService CreateService(AiOptions? options = null) =>
        new(
            _receiptRepository,
            _commodityRepository,
            _categoryAssignmentRepository,
            _aiClient,
            Options.Create(options ?? new AiOptions { BaseUrl = "http://localhost", Concurrency = 3 }),
            NullLogger<CommodityCategorizationService>.Instance);

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
        await _categoryAssignmentRepository.DidNotReceiveWithAnyArgs().GetByNormalizedNameAsync(default!, default);
    }

    [Fact]
    public async Task Suggest_treats_undefined_category_as_missing_and_uses_cache()
    {
        var item = CreateCommodity(_receiptId, "Кефир", (int)CommodityCategory.Undefined, "Не указана");
        ArrangeReceipt(item);

        _categoryAssignmentRepository
            .GetByNormalizedNameAsync("кефир", Arg.Any<CancellationToken>())
            .Returns(new CommodityCategoryAssignment(Guid.NewGuid(), "кефир", "Кефир", (int)CommodityCategory.Dairy, "Молочные продукты", DateTime.UtcNow));

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
        await _categoryAssignmentRepository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default);
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
        await _categoryAssignmentRepository.Received(1).UpsertAsync("Сыр", "сыр", CommodityCategory.Dairy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyConfirmedCategories_null_category_resets_existing_one_without_cache_update()
    {
        var item = CreateCommodity(_receiptId, "Старый товар", (int)CommodityCategory.Electronics, "Электроника");
        ArrangeReceipt(item);

        var assignments = new[] { new ConfirmedCategoryAssignment(item.Id, null) };

        var service = CreateService();
        var updated = await service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, assignments, CancellationToken.None);

        Assert.Equal(1, updated);
        await _commodityRepository.Received(1).UpdateCategoryAsync(item.Id, (CommodityCategory?)null, Arg.Any<CancellationToken>());
        await _categoryAssignmentRepository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default);
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
        await _categoryAssignmentRepository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!, default, default);
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
}