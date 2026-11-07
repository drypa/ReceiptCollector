using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Receipts;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Tests;

public class ReceiptCategorizationEndpointsTests
{
    private readonly Guid _receiptId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ICommodityCategorizationService _service = Substitute.For<ICommodityCategorizationService>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

    private static readonly AiOptions ConfiguredAi = new() { BaseUrl = "http://ai.local/v1", Concurrency = 3 };

    [Fact]
    public async Task Suggest_returns_unauthorized_when_user_is_not_authenticated()
    {
        // UserContext не установлен — пользователь не аутентифицирован.
        var result = await ReceiptCategorizationEndpoints.Suggest(
            _receiptId,
            _service,
            Options.Create(ConfiguredAi),
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().SuggestForReceiptAsync(default, default, default);
    }

    [Fact]
    public async Task Suggest_returns_503_when_ai_is_not_configured()
    {
        using var _ = UserContext.SetUserId(_userId);

        var result = await ReceiptCategorizationEndpoints.Suggest(
            _receiptId,
            _service,
            Options.Create(new AiOptions()),
            CancellationToken.None);

        var statusCodeResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCodeResult.StatusCode);
        await _service.DidNotReceiveWithAnyArgs().SuggestForReceiptAsync(default, default, default);
    }

    [Fact]
    public async Task Suggest_returns_not_found_for_unknown_receipt()
    {
        using var _ = UserContext.SetUserId(_userId);

        _service.SuggestForReceiptAsync(_userId, _receiptId, Arg.Any<CancellationToken>())
            .Returns<Task<ReceiptCategorizationResult>>(Task.FromException<ReceiptCategorizationResult>(new ReceiptNotFoundException(_receiptId)));

        var result = await ReceiptCategorizationEndpoints.Suggest(
            _receiptId,
            _service,
            Options.Create(ConfiguredAi),
            CancellationToken.None);

        var notFound = Assert.IsType<NotFound<string>>(result);
        Assert.Equal("Receipt not found.", notFound.Value);
    }

    [Fact]
    public async Task Suggest_returns_ok_with_suggestions()
    {
        using var _ = UserContext.SetUserId(_userId);

        var expected = new ReceiptCategorizationResult(
            _receiptId,
            new[]
            {
                new ReceiptItemCategorizationResult(Guid.NewGuid(), "Молоко", (int)CommodityCategory.Dairy, "Молочные продукты", "cache", null)
            });

        _service.SuggestForReceiptAsync(_userId, _receiptId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await ReceiptCategorizationEndpoints.Suggest(
            _receiptId,
            _service,
            Options.Create(ConfiguredAi),
            CancellationToken.None);

        var ok = Assert.IsType<Ok<ReceiptCategorizationResult>>(result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task Save_returns_unauthorized_when_user_is_not_authenticated()
    {
        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(Guid.NewGuid(), "Food")]);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs()
            .ApplyConfirmedCategoriesAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task Save_returns_bad_request_for_empty_items()
    {
        using var _ = UserContext.SetUserId(_userId);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            new SaveCategoriesRequest([]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("items must not be empty.", badRequest.Value);
    }

    [Theory]
    [InlineData("142")]
    [InlineData("23.5")]
    public async Task Save_returns_bad_request_for_numeric_category_strings(string numericValue)
    {
        using var _ = UserContext.SetUserId(_userId);

        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(Guid.NewGuid(), numericValue)]);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Contains("category", badRequest.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_returns_bad_request_for_undefined_category_name()
    {
        using var _ = UserContext.SetUserId(_userId);

        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(Guid.NewGuid(), "Undefined")]);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Contains("category", badRequest.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_returns_bad_request_for_unknown_category_name()
    {
        using var _ = UserContext.SetUserId(_userId);

        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(Guid.NewGuid(), "NotACategory")]);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Contains("category", badRequest.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_returns_bad_request_for_empty_commodity_id()
    {
        using var _ = UserContext.SetUserId(_userId);

        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(Guid.Empty, "Food")]);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Save_returns_not_found_for_unknown_receipt()
    {
        using var _ = UserContext.SetUserId(_userId);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(Task.FromException<int>(new ReceiptNotFoundException(_receiptId)));

        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(Guid.NewGuid(), "Food")]);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var notFound = Assert.IsType<NotFound<string>>(result);
        Assert.Equal("Receipt not found.", notFound.Value);
    }

    [Fact]
    public async Task Save_passes_null_category_when_category_is_blank_whitespace()
    {
        using var _ = UserContext.SetUserId(_userId);

        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(commodityId, "  ")]);

        _service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Is<IReadOnlyCollection<ConfirmedCategoryAssignment>>(items =>
                items.Count == 1 && items.Single().CommodityId == commodityId && items.Single().Category == null),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_returns_ok_with_updated_count()
    {
        using var _ = UserContext.SetUserId(_userId);

        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(commodityId, "dairy")]);

        _service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        Assert.Equal(_receiptId, ok.Value!.ReceiptId);
        Assert.Equal(1, ok.Value.Updated);

        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Is<IReadOnlyCollection<ConfirmedCategoryAssignment>>(items =>
                items.Count == 1
                && items.Single().CommodityId == commodityId
                && items.Single().Category == CommodityCategory.Dairy),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    // ---- T-04: аддитивный снимок предложения и admin-флаг (ADR-033 п. 3) ----

    [Fact]
    public async Task Save_passes_valid_snapshot_and_admin_flag_to_service()
    {
        using var _ = UserContext.SetUserId(_userId);

        _userRepository.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new User(_userId, "Админ", "external", 0, isAdmin: true));

        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest(
            [new SaveCategoryItemRequest(commodityId, "dairy", "Food", "ai")]);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Is<IReadOnlyCollection<ConfirmedCategoryAssignment>>(items =>
                items.Count == 1
                && items.Single().CommodityId == commodityId
                && items.Single().Category == CommodityCategory.Dairy
                && items.Single().SuggestedCategory == CommodityCategory.Food
                && items.Single().SuggestionSource == CategorizationDecisionSources.Ai),
            true,
            Arg.Any<CancellationToken>());
        await _userRepository.Received(1).GetByIdAsync(_userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_treats_missing_user_as_non_admin_and_still_saves()
    {
        using var _ = UserContext.SetUserId(_userId);

        // GetByIdAsync не настроен — NSubstitute возвращает null (пользователь удалён).
        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest(
            [new SaveCategoryItemRequest(commodityId, "dairy", "Food", "ai")]);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Food", "AI")] // источник вне множества: регистр значим (Ordinal)
    [InlineData("Food", "existing")] // не предложение (FR-5b)
    [InlineData("Undefined", "ai")] // предложенная категория не может быть Undefined
    [InlineData("142", "ai")] // число вместо имени категории
    [InlineData("NotACategory", "ai")]
    [InlineData("Food", null)] // частичный снимок
    [InlineData(null, "ai")] // частичный снимок
    public async Task Save_ignores_invalid_snapshot_without_bad_request(string? suggestedCategory, string? suggestionSource)
    {
        using var _ = UserContext.SetUserId(_userId);

        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest(
            [new SaveCategoryItemRequest(commodityId, "dairy", suggestedCategory, suggestionSource)]);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Is<IReadOnlyCollection<ConfirmedCategoryAssignment>>(items =>
                items.Count == 1
                && items.Single().Category == CommodityCategory.Dairy
                && items.Single().SuggestedCategory == null
                && items.Single().SuggestionSource == null),
            false,
            Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Save_does_not_query_users_when_no_snapshot_is_present()
    {
        using var _ = UserContext.SetUserId(_userId);

        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(commodityId, "dairy")]);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _userRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Is<IReadOnlyCollection<ConfirmedCategoryAssignment>>(items => items.Single().SuggestedCategory == null),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_queries_users_once_for_multiple_valid_snapshots()
    {
        using var _ = UserContext.SetUserId(_userId);

        _userRepository.GetByIdAsync(_userId, Arg.Any<CancellationToken>())
            .Returns(new User(_userId, "Пользователь", "external"));

        var request = new SaveCategoriesRequest(
        [
            new SaveCategoryItemRequest(Guid.NewGuid(), "dairy", "Food", "ai"),
            new SaveCategoryItemRequest(Guid.NewGuid(), "bakery", "Bakery", "cache")
        ]);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(2);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _userRepository.Received(1).GetByIdAsync(_userId, Arg.Any<CancellationToken>());
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(),
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_passes_reset_with_snapshot_to_service()
    {
        using var _ = UserContext.SetUserId(_userId);

        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest(
            [new SaveCategoryItemRequest(commodityId, "  ", "Dairy", "precategorized")]);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Is<IReadOnlyCollection<ConfirmedCategoryAssignment>>(items =>
                items.Single().Category == null
                && items.Single().SuggestedCategory == CommodityCategory.Dairy
                && items.Single().SuggestionSource == CategorizationDecisionSources.Precategorized),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }
}