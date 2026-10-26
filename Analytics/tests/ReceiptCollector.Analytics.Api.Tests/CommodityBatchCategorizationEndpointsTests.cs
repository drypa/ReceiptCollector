using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ReceiptCollector.Analytics.Api.Modules.Commodities;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты эндпоинтов порционной категоризации (T-07, план 10.3, ADR-028 п. 1.1/1.2/10).
/// Коды проверяются <b>исполнением</b> <see cref="IResult"/>: в приложении нет схемы аутентификации,
/// поэтому форма результата не гарантирует код (403 должен быть 403, а не 500).
/// </summary>
public class CommodityBatchCategorizationEndpointsTests
{
    private readonly ICommodityBatchCategorizationService _service =
        Substitute.For<ICommodityBatchCategorizationService>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

    // ---------------- Suggest ----------------

    [Fact]
    public async Task Suggest_without_user_returns_unauthorized()
    {
        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([Guid.NewGuid()]),
            _service,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().SuggestAsync(default, default!, default);
    }

    [Fact]
    public async Task Suggest_with_non_admin_returns_forbidden_not_server_error()
    {
        using var context = SetUser(isAdmin: false);

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([Guid.NewGuid()]),
            _service,
            _userRepository,
            CancellationToken.None);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().SuggestAsync(default, default!, default);
    }

    [Fact]
    public async Task Suggest_with_empty_ids_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([]),
            _service,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().SuggestAsync(default, default!, default);
    }

    [Fact]
    public async Task Suggest_with_missing_ids_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest(null),
            _service,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Suggest_with_more_than_200_ids_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest(ids),
            _service,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().SuggestAsync(default, default!, default);
    }

    [Fact]
    public async Task Suggest_with_empty_guid_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([Guid.NewGuid(), Guid.Empty]),
            _service,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Suggest_with_duplicate_ids_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([id, id]),
            _service,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().SuggestAsync(default, default!, default);
    }

    [Fact]
    public async Task Suggest_returns_items_and_ai_available()
    {
        using var context = SetUser(isAdmin: true);
        var commodityId = Guid.NewGuid();
        var payload = new CommodityBatchCategorizationResult(
            [
                new ReceiptItemCategorizationResult(
                    commodityId,
                    "Молоко",
                    (int)CommodityCategory.Dairy,
                    "Молочные продукты",
                    CategorizationSourceNames.Cache,
                    null)
            ],
            AiAvailable: true);

        _service.SuggestAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(payload);

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([commodityId]),
            _service,
            _userRepository,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<CommodityBatchCategorizationResult>>(result);
        Assert.Same(payload, ok.Value);
        Assert.True(ok.Value!.AiAvailable);
        Assert.Single(ok.Value.Items);
    }

    [Fact]
    public async Task Suggest_without_ai_configuration_returns_200_not_503()
    {
        using var context = SetUser(isAdmin: true);
        var commodityId = Guid.NewGuid();

        _service.SuggestAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new CommodityBatchCategorizationResult(
                [
                    new ReceiptItemCategorizationResult(
                        commodityId,
                        "Молоко",
                        null,
                        null,
                        CategorizationSourceNames.Undefined,
                        "ИИ не настроен.")
                ],
                AiAvailable: false));

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([commodityId]),
            _service,
            _userRepository,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<CommodityBatchCategorizationResult>>(result);
        Assert.False(ok.Value!.AiAvailable);

        await AssertStatusCodeAsync(result, StatusCodes.Status200OK);
        await _service.Received(1)
            .SuggestAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suggest_maps_service_argument_exception_to_bad_request()
    {
        using var context = SetUser(isAdmin: true);

        _service.SuggestAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ArgumentException("At most 20 unique commodity names are allowed per request."));

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([Guid.NewGuid()]),
            _service,
            _userRepository,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Contains("20", badRequest.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suggest_with_unknown_id_still_returns_200_without_that_item()
    {
        using var context = SetUser(isAdmin: true);
        var ownedId = Guid.NewGuid();

        _service.SuggestAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new CommodityBatchCategorizationResult(
                [
                    new ReceiptItemCategorizationResult(
                        ownedId,
                        "Молоко",
                        (int)CommodityCategory.Dairy,
                        "Молочные продукты",
                        CategorizationSourceNames.Cache,
                        null)
                ],
                AiAvailable: true));

        var result = await CommodityBatchCategorizationEndpoints.Suggest(
            new SuggestCommodityCategoriesRequest([ownedId, Guid.NewGuid()]),
            _service,
            _userRepository,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<CommodityBatchCategorizationResult>>(result);
        Assert.Equal(ownedId, Assert.Single(ok.Value!.Items).CommodityId);
    }

    // ---------------- Save ----------------

    [Fact]
    public async Task Save_without_user_returns_unauthorized()
    {
        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([new SaveCommodityCategoryItemRequest(Guid.NewGuid(), "Dairy")]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().ApplyAsync(default, default!, default);
    }

    [Fact]
    public async Task Save_with_non_admin_returns_forbidden_not_server_error()
    {
        using var context = SetUser(isAdmin: false);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([new SaveCommodityCategoryItemRequest(Guid.NewGuid(), "Dairy")]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().ApplyAsync(default, default!, default);
    }

    [Fact]
    public async Task Save_with_empty_items_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().ApplyAsync(default, default!, default);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("NotACategory")]
    [InlineData("Undefined")]
    public async Task Save_with_invalid_category_returns_bad_request(string category)
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([new SaveCommodityCategoryItemRequest(Guid.NewGuid(), category)]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().ApplyAsync(default, default!, default);
    }

    [Fact]
    public async Task Save_with_empty_guid_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([new SaveCommodityCategoryItemRequest(Guid.Empty, "Dairy")]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Save_with_duplicate_ids_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest(
            [
                new SaveCommodityCategoryItemRequest(id, "Dairy"),
                new SaveCommodityCategoryItemRequest(id, "Food")
            ]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().ApplyAsync(default, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_skips_empty_category_as_do_not_touch(string? category)
    {
        using var context = SetUser(isAdmin: true);
        var skippedId = Guid.NewGuid();
        var changedId = Guid.NewGuid();

        _service.ApplyAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<CommodityCategoryChange>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest(
            [
                new SaveCommodityCategoryItemRequest(skippedId, category),
                new SaveCommodityCategoryItemRequest(changedId, "dairy")
            ]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<SaveCommodityCategoriesResponse>>(result);
        Assert.Equal(1, ok.Value!.Updated);

        await _service.Received(1).ApplyAsync(
            Arg.Any<Guid>(),
            Arg.Is<IReadOnlyCollection<CommodityCategoryChange>>(changes =>
                changes.Count == 1
                && changes.Single().CommodityId == changedId
                && changes.Single().Category == CommodityCategory.Dairy),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_with_only_empty_categories_returns_zero_without_calling_service()
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest(
            [
                new SaveCommodityCategoryItemRequest(Guid.NewGuid(), null),
                new SaveCommodityCategoryItemRequest(Guid.NewGuid(), "   ")
            ]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<SaveCommodityCategoriesResponse>>(result);
        Assert.Equal(0, ok.Value!.Updated);
        await _service.DidNotReceiveWithAnyArgs().ApplyAsync(default, default!, default);
    }

    [Fact]
    public async Task Save_returns_server_updated_count()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        _service.ApplyAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<CommodityCategoryChange>>(), Arg.Any<CancellationToken>())
            .Returns(3);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([new SaveCommodityCategoryItemRequest(id, "Dairy")]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<SaveCommodityCategoriesResponse>>(result);
        Assert.Equal(3, ok.Value!.Updated);
    }

    [Fact]
    public async Task Save_maps_not_accessible_exception_to_conflict()
    {
        using var context = SetUser(isAdmin: true);

        _service.ApplyAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<CommodityCategoryChange>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CommoditiesNotAccessibleException(1));

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([new SaveCommodityCategoryItemRequest(Guid.NewGuid(), "Dairy")]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var conflict = Assert.IsType<Conflict<string>>(result);
        Assert.Contains("no longer accessible", conflict.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_accepts_lowercase_category_name()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        _service.ApplyAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<CommodityCategoryChange>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await CommodityBatchCategorizationEndpoints.Save(
            new SaveCommodityCategoriesRequest([new SaveCommodityCategoryItemRequest(id, "dairy")]),
            _service,
            _userRepository,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        Assert.IsType<Ok<SaveCommodityCategoriesResponse>>(result);
        await _service.Received(1).ApplyAsync(
            Arg.Any<Guid>(),
            Arg.Is<IReadOnlyCollection<CommodityCategoryChange>>(changes =>
                changes.Single().Category == CommodityCategory.Dairy),
            Arg.Any<CancellationToken>());
    }

    private IDisposable SetUser(bool isAdmin)
    {
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "user", "external", 1, isAdmin));
        return UserContext.SetUserId(userId);
    }

    /// <summary>
    /// Проверяет код ответа И что результат исполняется на реальном HttpContext
    /// (403 должен быть 403, а не 500 из-за отсутствия схемы аутентификации).
    /// </summary>
    private static async Task AssertStatusCodeAsync(IResult result, int expectedStatusCode)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        await result.ExecuteAsync(httpContext);

        Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);
    }
}
