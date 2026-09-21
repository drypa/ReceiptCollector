using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Receipts;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Tests;

public class ReceiptCategorizationEndpointsTests
{
    private readonly Guid _receiptId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ICommodityCategorizationService _service = Substitute.For<ICommodityCategorizationService>();

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
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().ApplyConfirmedCategoriesAsync(default, default, default, default);
    }

    [Fact]
    public async Task Save_returns_bad_request_for_empty_items()
    {
        using var _ = UserContext.SetUserId(_userId);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            new SaveCategoriesRequest([]),
            _service,
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
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Save_returns_not_found_for_unknown_receipt()
    {
        using var _ = UserContext.SetUserId(_userId);

        _service.ApplyConfirmedCategoriesAsync(
                _userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(Task.FromException<int>(new ReceiptNotFoundException(_receiptId)));

        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(Guid.NewGuid(), "Food")]);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
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

        _service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCategoriesResponse>>(result);
        await _service.Received(1).ApplyConfirmedCategoriesAsync(
            _userId,
            _receiptId,
            Arg.Is<IReadOnlyCollection<ConfirmedCategoryAssignment>>(items =>
                items.Count == 1 && items.Single().CommodityId == commodityId && items.Single().Category == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_returns_ok_with_updated_count()
    {
        using var _ = UserContext.SetUserId(_userId);

        var commodityId = Guid.NewGuid();
        var request = new SaveCategoriesRequest([new SaveCategoryItemRequest(commodityId, "dairy")]);

        _service.ApplyConfirmedCategoriesAsync(_userId, _receiptId, Arg.Any<IReadOnlyCollection<ConfirmedCategoryAssignment>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await ReceiptCategorizationEndpoints.Save(
            _receiptId,
            request,
            _service,
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
            Arg.Any<CancellationToken>());
    }
}