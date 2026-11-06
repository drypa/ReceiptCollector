using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.Merchants;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Models;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты эндпоинтов товарной категории по умолчанию магазина (T-07, ADR-032 п. 6):
/// права по эталону (401 → явный 403 — не 500), 404, валидация кода справочника,
/// установка/сброс, маппинг исходов дожима (NotFound/DefaultNotConfigured/Applied)
/// и аддитивные поля <see cref="MerchantDto"/> в списке GET /api/merchants.
/// </summary>
public class MerchantDefaultCategoryEndpointsTests
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMerchantRepository _merchantRepository = Substitute.For<IMerchantRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IMerchantDefaultCategoryService _defaultCategoryService = Substitute.For<IMerchantDefaultCategoryService>();

    // ---------------------------------------------------------------- PUT

    [Fact]
    public async Task UpdateDefaultCategory_without_user_returns_unauthorized()
    {
        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            Guid.NewGuid(),
            new UpdateMerchantDefaultCategoryRequest((int)CommodityCategory.ReadyMeals),
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _merchantRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default);
        await _merchantRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task UpdateDefaultCategory_with_non_admin_returns_403_when_executed()
    {
        using var context = SetUser(isAdmin: false);

        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            Guid.NewGuid(),
            new UpdateMerchantDefaultCategoryRequest((int)CommodityCategory.ReadyMeals),
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        // Исполнение IResult: именно 403, а не 500 (дефект Results.Forbid() не воспроизведён).
        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _merchantRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default);
        await _merchantRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task UpdateDefaultCategory_with_unknown_merchant_returns_not_found()
    {
        using var context = SetUser(isAdmin: true);
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns((Merchant?)null);

        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            merchantId,
            new UpdateMerchantDefaultCategoryRequest((int)CommodityCategory.ReadyMeals),
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        var notFound = Assert.IsType<NotFound<string>>(result);
        Assert.Equal("Merchant not found.", notFound.Value);
        await _merchantRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Theory]
    [InlineData(0)]    // Undefined запрещён как дефолт (CHECK «<> 0», A1)
    [InlineData(-1)]   // вне справочника
    [InlineData(999)]  // вне справочника
    public async Task UpdateDefaultCategory_with_invalid_category_returns_bad_request(int categoryId)
    {
        using var context = SetUser(isAdmin: true);
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(new Merchant(merchantId, "Пятёрочка", MerchantCategory.GroceryStores));

        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            merchantId,
            new UpdateMerchantDefaultCategoryRequest(categoryId),
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("Invalid category.", badRequest.Value);
        await _merchantRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task UpdateDefaultCategory_with_valid_category_saves_and_returns_pair()
    {
        using var context = SetUser(isAdmin: true);
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(new Merchant(merchantId, "Столовая", MerchantCategory.GroceryStores));

        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            merchantId,
            new UpdateMerchantDefaultCategoryRequest((int)CommodityCategory.ReadyMeals),
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        // Сохранение — существующим repo-путём редактирования merchant (ADR 011: AddAsync).
        await _merchantRepository.Received(1).AddAsync(
            Arg.Is<Merchant>(m => m.Id == merchantId
                                  && m.DefaultCommodityCategory == CommodityCategory.ReadyMeals),
            Arg.Any<CancellationToken>());

        var ok = Assert.IsType<Ok<UpdateMerchantDefaultCategoryResponse>>(result);
        Assert.Equal((int)CommodityCategory.ReadyMeals, ok.Value!.CategoryId);
        Assert.Equal("Готовая еда и кулинария", ok.Value.CategoryName);
    }

    [Fact]
    public async Task UpdateDefaultCategory_with_null_resets_and_returns_nulls()
    {
        using var context = SetUser(isAdmin: true);
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(new Merchant(
                merchantId,
                "Столовая",
                MerchantCategory.GroceryStores,
                defaultCommodityCategory: CommodityCategory.ReadyMeals));

        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            merchantId,
            new UpdateMerchantDefaultCategoryRequest(null),
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        await _merchantRepository.Received(1).AddAsync(
            Arg.Is<Merchant>(m => m.DefaultCommodityCategory == null),
            Arg.Any<CancellationToken>());

        var ok = Assert.IsType<Ok<UpdateMerchantDefaultCategoryResponse>>(result);
        Assert.Null(ok.Value!.CategoryId);
        Assert.Null(ok.Value.CategoryName);
    }

    [Fact]
    public async Task UpdateDefaultCategory_with_null_body_returns_bad_request_not_500()
    {
        // JSON-literal null (а не объект) десериализуется в null-ссылку — guard даёт 400, не NRE/500.
        using var context = SetUser(isAdmin: true);
        var merchantId = Guid.NewGuid();
        _merchantRepository.GetByIdAsync(merchantId, Arg.Any<CancellationToken>())
            .Returns(new Merchant(merchantId, "Пятёрочка", MerchantCategory.GroceryStores));

        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            merchantId,
            null!,
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("Request body is required.", badRequest.Value);
        await _merchantRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task UpdateDefaultCategory_with_null_body_and_no_user_still_returns_unauthorized()
    {
        // Фиксация порядка: права проверяются раньше валидации тела (401, а не 400).
        var result = await MerchantEndpoints.UpdateDefaultCommodityCategory(
            Guid.NewGuid(),
            null!,
            _merchantRepository,
            _userRepository,
            CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _merchantRepository.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public void Empty_json_object_deserializes_to_reset_request()
    {
        // Зафиксированное решение: тело {} (отсутствующее поле categoryId) = тот же сброс,
        // что и {"categoryId": null}; JSON-literal null при этом — null-ссылка и 400 в хендлере.
        var request = JsonSerializer
            .Deserialize<UpdateMerchantDefaultCategoryRequest>("{}", WebJsonOptions);

        Assert.NotNull(request);
        Assert.Null(request.CategoryId);
    }

    // --------------------------------------------------------------- Apply

    [Fact]
    public async Task Apply_without_user_returns_unauthorized()
    {
        var result = await MerchantEndpoints.ApplyDefaultCommodityCategory(
            Guid.NewGuid(), _defaultCategoryService, _userRepository, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _defaultCategoryService.DidNotReceiveWithAnyArgs()
            .ApplyExistingAsync(default, default, default);
    }

    [Fact]
    public async Task Apply_with_non_admin_returns_403_when_executed()
    {
        using var context = SetUser(isAdmin: false);

        var result = await MerchantEndpoints.ApplyDefaultCommodityCategory(
            Guid.NewGuid(), _defaultCategoryService, _userRepository, CancellationToken.None);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _defaultCategoryService.DidNotReceiveWithAnyArgs()
            .ApplyExistingAsync(default, default, default);
    }

    [Fact]
    public async Task Apply_maps_not_found_outcome_to_404()
    {
        using var context = SetUser(isAdmin: true);
        var merchantId = Guid.NewGuid();
        _defaultCategoryService.ApplyExistingAsync(Arg.Any<Guid>(), merchantId, Arg.Any<CancellationToken>())
            .Returns(new MerchantDefaultApplyResult(MerchantDefaultApplyOutcome.NotFound, 0));

        var result = await MerchantEndpoints.ApplyDefaultCommodityCategory(
            merchantId, _defaultCategoryService, _userRepository, CancellationToken.None);

        var notFound = Assert.IsType<NotFound<string>>(result);
        Assert.Equal("Merchant not found.", notFound.Value);
    }

    [Fact]
    public async Task Apply_maps_default_not_configured_to_bad_request_text()
    {
        using var context = SetUser(isAdmin: true);
        var merchantId = Guid.NewGuid();
        _defaultCategoryService.ApplyExistingAsync(Arg.Any<Guid>(), merchantId, Arg.Any<CancellationToken>())
            .Returns(new MerchantDefaultApplyResult(MerchantDefaultApplyOutcome.DefaultNotConfigured, 0));

        var result = await MerchantEndpoints.ApplyDefaultCommodityCategory(
            merchantId, _defaultCategoryService, _userRepository, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("Default commodity category is not set for this merchant.", badRequest.Value);
    }

    [Theory]
    [InlineData(0)]  // повторное нажатие идемпотентно — всё ещё 200
    [InlineData(17)]
    public async Task Apply_maps_applied_outcome_to_ok_with_updated_count(int updated)
    {
        using var context = SetUser(isAdmin: true);
        var userId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "admin", "external", 1, isAdmin: true));
        _defaultCategoryService.ApplyExistingAsync(userId, merchantId, Arg.Any<CancellationToken>())
            .Returns(new MerchantDefaultApplyResult(MerchantDefaultApplyOutcome.Applied, updated));

        using (UserContext.SetUserId(userId))
        {
            var result = await MerchantEndpoints.ApplyDefaultCommodityCategory(
                merchantId, _defaultCategoryService, _userRepository, CancellationToken.None);

            var ok = Assert.IsType<Ok<ApplyMerchantDefaultCategoryResponse>>(result);
            Assert.Equal(updated, ok.Value!.Updated);
        }

        // adminUserId пробрасывается сервису (аудит операции, E1).
        await _defaultCategoryService.Received(1)
            .ApplyExistingAsync(userId, merchantId, Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------ MerchantDto list

    [Fact]
    public async Task GetAll_returns_default_category_fields_additively()
    {
        using var context = SetUser(isAdmin: true);
        var httpContext = new DefaultHttpContext();
        _merchantRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<Merchant>
            {
                new(Guid.NewGuid(), "Автотека", MerchantCategory.GroceryStores,
                    defaultCommodityCategory: CommodityCategory.AutomotiveGoods),
                new(Guid.NewGuid(), "Ромашка", MerchantCategory.GroceryStores),
            });

        var result = await MerchantEndpoints.GetAll(
            httpContext, _merchantRepository, _userRepository, 50, 0, null, CancellationToken.None);

        var ok = Assert.IsType<Ok<List<MerchantDto>>>(result);
        var page = ok.Value!;
        var withDefault = page.Single(m => m.Name == "Автотека");
        Assert.Equal((int)CommodityCategory.AutomotiveGoods, withDefault.DefaultCommodityCategoryId);
        Assert.Equal("Автотовары", withDefault.DefaultCommodityCategoryName);

        var withoutDefault = page.Single(m => m.Name == "Ромашка");
        Assert.Null(withoutDefault.DefaultCommodityCategoryId);
        Assert.Null(withoutDefault.DefaultCommodityCategoryName);

        // Wire-контракт: camelCase-имена новых полей присутствуют в JSON (аддитивно,
        // значения/имена совпадают).
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(page, WebJsonOptions));
        var withDefaultJson = doc.RootElement
            .EnumerateArray().Single(e => e.GetProperty("name").GetString() == "Автотека");
        Assert.Equal(
            (int)CommodityCategory.AutomotiveGoods,
            withDefaultJson.GetProperty("defaultCommodityCategoryId").GetInt32());
        Assert.Equal(
            "Автотовары",
            withDefaultJson.GetProperty("defaultCommodityCategoryName").GetString());

        var withoutDefaultJson = doc.RootElement
            .EnumerateArray().Single(e => e.GetProperty("name").GetString() == "Ромашка");
        Assert.Equal(JsonValueKind.Null, withoutDefaultJson.GetProperty("defaultCommodityCategoryId").ValueKind);
        Assert.Equal(JsonValueKind.Null, withoutDefaultJson.GetProperty("defaultCommodityCategoryName").ValueKind);
    }

    [Fact]
    public async Task GetAll_without_user_still_returns_unauthorized_after_dto_change()
    {
        var result = await MerchantEndpoints.GetAll(
            new DefaultHttpContext(), _merchantRepository, _userRepository, 50, 0, null, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _merchantRepository.DidNotReceiveWithAnyArgs().GetAllAsync(default);
    }

    // ------------------------------------------------------------ Routing

    [Fact]
    public void MapMerchantEndpoints_registers_default_category_routes_in_group()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        var app = builder.Build();

        app.MapMerchantEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText!.EndsWith("api/merchants/{merchantId:guid}/default-commodity-category", StringComparison.Ordinal)
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("PUT") == true);
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText!.EndsWith("api/merchants/{merchantId:guid}/default-commodity-category/apply", StringComparison.Ordinal)
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true);
    }

    // ------------------------------------------------------------ Helpers

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

    private IDisposable SetUser(bool isAdmin)
    {
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "user", "external", 1, isAdmin));
        return UserContext.SetUserId(userId);
    }
}
