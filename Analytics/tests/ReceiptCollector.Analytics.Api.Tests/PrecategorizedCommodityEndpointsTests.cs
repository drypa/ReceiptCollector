using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.PrecategorizedCommodities;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты админского API очереди (шаг 8 плана, ADR-027 п.7, FR-5.1–5.9):
/// права по эталону MerchantEndpoints, текстовые ошибки, 400/404/409, X-Total-Count.
/// </summary>
public class PrecategorizedCommodityEndpointsTests
{
    private readonly IPrecategorizedCommodityService _service = Substitute.For<IPrecategorizedCommodityService>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

    [Fact]
    public async Task GetAll_without_user_returns_unauthorized()
    {
        var result = await PrecategorizedCommodityEndpoints.GetAll(
            new DefaultHttpContext(), _service, _userRepository, 50, 0, null, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default, default, default, default);
    }

    [Fact]
    public async Task GetAll_with_non_admin_returns_forbidden()
    {
        using var context = SetUser(isAdmin: false);

        var result = await PrecategorizedCommodityEndpoints.GetAll(
            new DefaultHttpContext(), _service, _userRepository, 50, 0, null, CancellationToken.None);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default, default, default, default);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(201, 0)]
    [InlineData(50, -1)]
    public async Task GetAll_with_invalid_paging_returns_bad_request(int limit, int offset)
    {
        using var context = SetUser(isAdmin: true);

        var result = await PrecategorizedCommodityEndpoints.GetAll(
            new DefaultHttpContext(), _service, _userRepository, limit, offset, null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(badRequest.Value));
    }

    [Fact]
    public async Task GetAll_returns_items_and_total_count_header()
    {
        using var context = SetUser(isAdmin: true);
        var httpContext = new DefaultHttpContext();
        var dto = new PrecategorizedCommodityDto(
            Guid.NewGuid(),
            "Молоко",
            Guid.NewGuid(),
            "Пятёрочка",
            (int)CommodityCategory.Dairy,
            "Молочные продукты",
            PrecategorizedCommodityStatusNames.Proposed,
            DateTimeOffset.UtcNow);

        _service.GetPageAsync(10, 5, "мол", Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<PrecategorizedCommodityDto>)new[] { dto }, 42));

        var result = await PrecategorizedCommodityEndpoints.GetAll(
            httpContext, _service, _userRepository, 10, 5, "мол", CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<PrecategorizedCommodityDto>>>(result);
        Assert.Same(dto, Assert.Single(ok.Value!));
        Assert.Equal("42", httpContext.Response.Headers["X-Total-Count"].ToString());
        Assert.Equal(42.ToString(CultureInfo.InvariantCulture), httpContext.Response.Headers["X-Total-Count"].ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public async Task UpdateCategory_with_invalid_category_returns_bad_request(int categoryId)
    {
        using var context = SetUser(isAdmin: true);

        var result = await PrecategorizedCommodityEndpoints.UpdateCategory(
            Guid.NewGuid(),
            new UpdatePrecategorizedCategoryRequest(categoryId),
            _service,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().UpdateCategoryAsync(default, default, default);
    }

    [Fact]
    public async Task UpdateCategory_maps_outcomes_and_returns_category_name()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        _service.UpdateCategoryAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Applied);
        var success = await PrecategorizedCommodityEndpoints.UpdateCategory(
            id, new UpdatePrecategorizedCategoryRequest((int)CommodityCategory.Dairy),
            _service, _userRepository, CancellationToken.None);
        var ok = Assert.IsType<Ok<UpdatePrecategorizedCategoryResponse>>(success);
        Assert.Equal(id, ok.Value!.Id);
        Assert.Equal((int)CommodityCategory.Dairy, ok.Value.CategoryId);
        Assert.Equal("Молочные продукты", ok.Value.CategoryName);

        _service.UpdateCategoryAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Conflict);
        var conflict = await PrecategorizedCommodityEndpoints.UpdateCategory(
            id, new UpdatePrecategorizedCategoryRequest((int)CommodityCategory.Dairy),
            _service, _userRepository, CancellationToken.None);
        Assert.IsType<Conflict<string>>(conflict);

        _service.UpdateCategoryAsync(id, CommodityCategory.Dairy, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.NotFound);
        var notFound = await PrecategorizedCommodityEndpoints.UpdateCategory(
            id, new UpdatePrecategorizedCategoryRequest((int)CommodityCategory.Dairy),
            _service, _userRepository, CancellationToken.None);
        Assert.IsType<NotFound<string>>(notFound);
    }

    [Fact]
    public async Task Confirm_maps_outcomes_and_returns_updated_count()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        _service.ConfirmAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedCommodityResult(
                PrecategorizedOperationOutcome.Applied, id, (int)CommodityCategory.Dairy, "Молочные продукты", 0));

        var result = await PrecategorizedCommodityEndpoints.Confirm(id, _service, _userRepository, CancellationToken.None);
        var ok = Assert.IsType<Ok<ConfirmPrecategorizedCommodityResponse>>(result);
        Assert.Equal(id, ok.Value!.Id);
        Assert.Equal((int)CommodityCategory.Dairy, ok.Value.CategoryId);
        Assert.Equal("Молочные продукты", ok.Value.CategoryName);
        Assert.Equal(0, ok.Value.UpdatedCommodities);

        _service.ConfirmAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedCommodityResult(PrecategorizedOperationOutcome.Conflict, id, null, null, 0));
        Assert.IsType<Conflict<string>>(await PrecategorizedCommodityEndpoints.Confirm(
            id, _service, _userRepository, CancellationToken.None));

        _service.ConfirmAsync(id, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedCommodityResult(PrecategorizedOperationOutcome.NotFound, id, null, null, 0));
        Assert.IsType<NotFound<string>>(await PrecategorizedCommodityEndpoints.Confirm(
            id, _service, _userRepository, CancellationToken.None));
    }

    [Fact]
    public async Task Decline_maps_outcomes()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        _service.DeclineAsync(id, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Applied);
        Assert.IsType<NoContent>(await PrecategorizedCommodityEndpoints.Decline(
            id, _service, _userRepository, CancellationToken.None));

        _service.DeclineAsync(id, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.Conflict);
        Assert.IsType<Conflict<string>>(await PrecategorizedCommodityEndpoints.Decline(
            id, _service, _userRepository, CancellationToken.None));

        _service.DeclineAsync(id, Arg.Any<CancellationToken>())
            .Returns(PrecategorizedOperationOutcome.NotFound);
        Assert.IsType<NotFound<string>>(await PrecategorizedCommodityEndpoints.Decline(
            id, _service, _userRepository, CancellationToken.None));
    }

    [Fact]
    public async Task Mutating_endpoints_require_admin()
    {
        var id = Guid.NewGuid();

        Assert.IsType<UnauthorizedHttpResult>(await PrecategorizedCommodityEndpoints.UpdateCategory(
            id, new UpdatePrecategorizedCategoryRequest((int)CommodityCategory.Dairy),
            _service, _userRepository, CancellationToken.None));
        Assert.IsType<UnauthorizedHttpResult>(await PrecategorizedCommodityEndpoints.Confirm(
            id, _service, _userRepository, CancellationToken.None));
        Assert.IsType<UnauthorizedHttpResult>(await PrecategorizedCommodityEndpoints.Decline(
            id, _service, _userRepository, CancellationToken.None));

        using var context = SetUser(isAdmin: false);
        await AssertStatusCodeAsync(
            await PrecategorizedCommodityEndpoints.Confirm(id, _service, _userRepository, CancellationToken.None),
            StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().ConfirmAsync(default, default);
        await _service.DidNotReceiveWithAnyArgs().DeclineAsync(default, default);
    }

    /// <summary>
    /// Проверяет код ответа И что результат исполняется на реальном HttpContext:
    /// в приложении нет схемы аутентификации, поэтому «форма» результата сама по себе
    /// не гарантирует код (Results.Forbid() даёт 500 вместо 403).
    /// </summary>
    private static async Task AssertStatusCodeAsync(IResult result, int expectedStatusCode)
    {
        // StatusCodeHttpResult при исполнении запрашивает сервисы из RequestServices
        // (в частности ILoggerFactory).
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        await result.ExecuteAsync(httpContext);

        Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);
    }

    [Fact]
    public void MapPrecategorizedCommodityEndpoints_registers_group_with_tag()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        var app = builder.Build();

        app.MapPrecategorizedCommodityEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText!.Contains("api/precategorized-commodities", StringComparison.Ordinal));
        Assert.Contains(endpoints, endpoint =>
            endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags.Contains("PrecategorizedCommodities") == true);
    }

    private IDisposable SetUser(bool isAdmin)
    {
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "user", "external", 1, isAdmin));
        return UserContext.SetUserId(userId);
    }
}
