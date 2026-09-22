using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Commodities;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CommodityEndpointsTests
{
    private readonly ICommodityReadService _service = Substitute.For<ICommodityReadService>();
    private readonly ICommodityRepository _commodityRepository = Substitute.For<ICommodityRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ICommodityCategoryCache _cache = Substitute.For<ICommodityCategoryCache>();

    private static IReadOnlyCollection<CommodityItemDto> EmptyCommodities() => [];

    [Fact]
    public async Task GetAll_returns_bad_request_when_user_is_not_authenticated()
    {
        var httpContext = new DefaultHttpContext();

        var result = await CommodityEndpoints.GetAll(httpContext, _service, 10, 0, null, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default, default);
    }

    [Theory]
    [InlineData("uncategorized", CommodityCategoryFilter.Uncategorized)]
    [InlineData("undefined", CommodityCategoryFilter.Undefined)]
    [InlineData("any", CommodityCategoryFilter.Any)]
    [InlineData("UNCATEGORIZED", CommodityCategoryFilter.Uncategorized)]
    [InlineData(null, CommodityCategoryFilter.Any)]
    [InlineData("", CommodityCategoryFilter.Any)]
    public async Task GetAll_parses_category_filter_and_passes_it_to_service(string? rawFilter, CommodityCategoryFilter expected)
    {
        using var _ = UserContext.SetUserId(Guid.NewGuid());
        var httpContext = new DefaultHttpContext();

        _service.GetAsync(Arg.Any<Guid>(), 10, 0, expected, Arg.Any<CancellationToken>())
            .Returns(EmptyCommodities());
        _service.GetTotalCountAsync(Arg.Any<Guid>(), expected, Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await CommodityEndpoints.GetAll(httpContext, _service, 10, 0, rawFilter, CancellationToken.None);

        Assert.IsType<Ok<IReadOnlyCollection<CommodityItemDto>>>(result);
        await _service.Received(1).GetAsync(
            Arg.Any<Guid>(), 10, 0, expected, Arg.Any<CancellationToken>());
        await _service.Received(1).GetTotalCountAsync(
            Arg.Any<Guid>(), expected, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("42")]
    [InlineData("food")]
    public async Task GetAll_returns_bad_request_for_invalid_category_filter(string rawFilter)
    {
        using var _ = UserContext.SetUserId(Guid.NewGuid());
        var httpContext = new DefaultHttpContext();

        var result = await CommodityEndpoints.GetAll(httpContext, _service, 10, 0, rawFilter, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Contains("categoryFilter", badRequest.Value, StringComparison.OrdinalIgnoreCase);
        await _service.DidNotReceiveWithAnyArgs().GetAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task GetAll_sets_x_total_count_header()
    {
        using var _ = UserContext.SetUserId(Guid.NewGuid());
        var httpContext = new DefaultHttpContext();

        _service.GetAsync(Arg.Any<Guid>(), 10, 0, CommodityCategoryFilter.Any, Arg.Any<CancellationToken>())
            .Returns(EmptyCommodities());
        _service.GetTotalCountAsync(Arg.Any<Guid>(), CommodityCategoryFilter.Any, Arg.Any<CancellationToken>())
            .Returns(7);

        await CommodityEndpoints.GetAll(httpContext, _service, 10, 0, null, CancellationToken.None);

        Assert.Equal("7", httpContext.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public async Task GetAll_returns_bad_request_for_negative_offset()
    {
        using var _ = UserContext.SetUserId(Guid.NewGuid());
        var httpContext = new DefaultHttpContext();

        var result = await CommodityEndpoints.GetAll(httpContext, _service, 10, -1, null, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public void ListCategories_returns_key_equal_to_enum_name()
    {
        var result = CommodityEndpoints.ListCategories();

        var ok = Assert.IsType<Ok<List<CategoryDto>>>(result);
        var categories = ok.Value!;

        Assert.Contains(categories, c => c.Key == "Food" && c.Id == (int)CommodityCategory.Food);
        Assert.Contains(categories, c => c.Key == "Dairy" && c.Group == "Продукты");
        Assert.Contains(categories, c => c.Key == "Undefined" && c.Id == 0);
        Assert.All(categories, c => Assert.NotNull(c.Name));
        Assert.DoesNotContain(categories, c => string.IsNullOrWhiteSpace(c.Key));
    }

    [Fact]
    public async Task UpdateCategory_writes_category_to_cache()
    {
        var userId = Guid.NewGuid();
        var commodityId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        using var _ = UserContext.SetUserId(userId);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "admin", "ext", isAdmin: true));
        _commodityRepository.GetByIdAsync(commodityId, Arg.Any<CancellationToken>())
            .Returns(new Commodity(commodityId, receiptId, "АИ-95-К5", 1m, 100m, 0, 0m));

        var result = await CommodityEndpoints.UpdateCategory(
            commodityId,
            new UpdateCategoryRequest((int)CommodityCategory.Fuel),
            _commodityRepository,
            _userRepository,
            _cache,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        await _commodityRepository.Received(1).UpdateCategoryAsync(commodityId, CommodityCategory.Fuel, Arg.Any<CancellationToken>());
        _cache.Received(1).TryAdd("аи-95-к5", CommodityCategory.Fuel); // FR-1.3, причина 1 из ADR 019
    }

    [Fact]
    public async Task UpdateCategory_does_not_write_undefined_to_cache()
    {
        var userId = Guid.NewGuid();
        var commodityId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        using var _ = UserContext.SetUserId(userId);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "admin", "ext", isAdmin: true));
        _commodityRepository.GetByIdAsync(commodityId, Arg.Any<CancellationToken>())
            .Returns(new Commodity(commodityId, receiptId, "АИ-95-К5", 1m, 100m, 0, 0m));

        var result = await CommodityEndpoints.UpdateCategory(
            commodityId,
            new UpdateCategoryRequest((int)CommodityCategory.Undefined),
            _commodityRepository,
            _userRepository,
            _cache,
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        await _commodityRepository.Received(1).UpdateCategoryAsync(commodityId, CommodityCategory.Undefined, Arg.Any<CancellationToken>());
        // Сброс в Undefined в кэш не пишется (инвариант 8).
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task UpdateCategory_returns_forbidden_for_non_admin()
    {
        var userId = Guid.NewGuid();
        var commodityId = Guid.NewGuid();
        using var _ = UserContext.SetUserId(userId);

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "user", "ext", isAdmin: false));

        var result = await CommodityEndpoints.UpdateCategory(
            commodityId,
            new UpdateCategoryRequest((int)CommodityCategory.Fuel),
            _commodityRepository,
            _userRepository,
            _cache,
            CancellationToken.None);

        Assert.IsType<ForbidHttpResult>(result);
        await _commodityRepository.DidNotReceiveWithAnyArgs().UpdateCategoryAsync(default, default, default);
        _cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }
}