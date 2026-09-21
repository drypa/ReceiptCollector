using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Commodities;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

public class CommodityEndpointsTests
{
    private readonly ICommodityReadService _service = Substitute.For<ICommodityReadService>();

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
}