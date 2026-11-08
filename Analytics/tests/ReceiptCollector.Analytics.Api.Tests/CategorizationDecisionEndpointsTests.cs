using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты админского API журнала решений категоризации (ADR-033 п. 7, FR-6/FR-7, T-08):
/// права (401/403), валидация параметров (400), 200 с X-Total-Count из Total, передача
/// фильтра read-сервису и регистрация единственного GET-маршрута (write-эндпоинтов нет —
/// ADR-033 п. 9). Стиль — прецедент <see cref="PrecategorizedCommodityEndpointsTests"/>:
/// исполнение <see cref="IResult"/> на <see cref="DefaultHttpContext"/>.
/// </summary>
public class CategorizationDecisionEndpointsTests
{
    private readonly ICategorizationDecisionReadService _service = Substitute.For<ICategorizationDecisionReadService>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

    [Fact]
    public async Task GetDecisions_without_user_returns_unauthorized()
    {
        var result = await InvokeAsync();

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Fact]
    public async Task GetDecisions_with_non_admin_returns_forbidden_and_data_never_loaded()
    {
        using var context = SetUser(isAdmin: false);

        // Даже с полностью валидными параметрами не-администратор не получает данных (критерий T-08).
        var result = await InvokeAsync(
            onlyMismatch: true,
            source: CategorizationDecisionSources.Ai,
            suggestedCategory: (int)CommodityCategory.Dairy,
            finalCategory: 0,
            search: "хлеб",
            from: "2026-11-01T00:00:00Z",
            to: "2026-11-02T00:00:00Z",
            limit: 10,
            offset: 5);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Theory]
    [InlineData("")]                // присутствует, но не является wire-именем
    [InlineData("AI")]              // регистр значим (Ordinal)
    [InlineData("existing")]        // источник категоризации, но не предложение журнала (FR-5b)
    [InlineData("undefined")]
    public async Task GetDecisions_with_unknown_source_returns_bad_request(string source)
    {
        using var context = SetUser(isAdmin: true);

        var result = await InvokeAsync(source: source);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(badRequest.Value));
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Theory]
    [InlineData(0)]     // «Не указана» — не предложение (ADR-033 п. 7)
    [InlineData(-1)]    // вне enum
    [InlineData(999)]   // код вне справочника
    public async Task GetDecisions_with_invalid_suggested_category_returns_bad_request(int suggestedCategory)
    {
        using var context = SetUser(isAdmin: true);

        var result = await InvokeAsync(suggestedCategory: suggestedCategory);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(badRequest.Value));
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task GetDecisions_with_unknown_final_category_returns_bad_request(int finalCategory)
    {
        using var context = SetUser(isAdmin: true);

        var result = await InvokeAsync(finalCategory: finalCategory);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Theory]
    [InlineData("")]           // присутствует и непарсимо — отдельного правила для пустого значения нет
    [InlineData("not-a-date")]
    [InlineData("2026-13-45T00:00:00Z")]
    public async Task GetDecisions_with_unparsable_from_returns_bad_request(string from)
    {
        using var context = SetUser(isAdmin: true);

        var result = await InvokeAsync(from: from);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026-13-45T00:00:00Z")]
    public async Task GetDecisions_with_unparsable_to_returns_bad_request(string to)
    {
        using var context = SetUser(isAdmin: true);

        var result = await InvokeAsync(to: to);

        Assert.IsType<BadRequest<string>>(result);
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task GetDecisions_with_invalid_limit_returns_bad_request(int limit)
    {
        using var context = SetUser(isAdmin: true);

        var result = await InvokeAsync(limit: limit);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(badRequest.Value));
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Fact]
    public async Task GetDecisions_with_negative_offset_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);

        var result = await InvokeAsync(offset: -1);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.False(string.IsNullOrWhiteSpace(badRequest.Value));
        await _service.DidNotReceiveWithAnyArgs().GetPageAsync(default!, default);
    }

    [Fact]
    public async Task GetDecisions_returns_rows_and_total_count_header()
    {
        using var context = SetUser(isAdmin: true);
        var httpContext = new DefaultHttpContext();
        var row = CreateRow();

        // Total — число уникальных сочетаний (не число строк страницы и не число событий).
        _service.GetPageAsync(Arg.Any<CategorizationDecisionQuery>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<CategorizationDecisionRow>)new[] { row }, 42));

        var result = await InvokeAsync(httpContext);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<CategorizationDecisionRow>>>(result);
        Assert.Same(row, Assert.Single(ok.Value!));
        Assert.Equal("42", httpContext.Response.Headers["X-Total-Count"].ToString());
    }

    [Fact]
    public async Task GetDecisions_with_empty_result_returns_ok_and_zero_total()
    {
        using var context = SetUser(isAdmin: true);
        var httpContext = new DefaultHttpContext();

        _service.GetPageAsync(Arg.Any<CategorizationDecisionQuery>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<CategorizationDecisionRow>)[], 0));

        var result = await InvokeAsync(httpContext);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<CategorizationDecisionRow>>>(result);
        Assert.Empty(ok.Value!);
        Assert.Equal("0", httpContext.Response.Headers["X-Total-Count"].ToString());
    }

    [Fact]
    public async Task GetDecisions_accepts_final_category_zero_as_not_specified_filter()
    {
        // Итоговая категория 0 («Не указана») допустима в отличие от suggestedCategory (ADR-033 п. 7).
        using var context = SetUser(isAdmin: true);

        _service.GetPageAsync(Arg.Any<CategorizationDecisionQuery>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<CategorizationDecisionRow>)[], 0));

        var result = await InvokeAsync(finalCategory: (int)CommodityCategory.Undefined);

        Assert.IsType<Ok<IReadOnlyCollection<CategorizationDecisionRow>>>(result);
        await _service.Received(1).GetPageAsync(
            Arg.Is<CategorizationDecisionQuery>(query => query.FinalCategoryId == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDecisions_passes_all_parameters_to_read_service_query()
    {
        using var context = SetUser(isAdmin: true);

        _service.GetPageAsync(Arg.Any<CategorizationDecisionQuery>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<CategorizationDecisionRow>)[], 0));

        await InvokeAsync(
            onlyMismatch: true,
            source: CategorizationDecisionSources.MerchantDefault,
            suggestedCategory: (int)CommodityCategory.Dairy,
            finalCategory: 0,
            search: "хлеб",
            from: "2026-11-01T00:00:00Z",
            // to с ненулевым смещением: RoundtripKind сохраняет смещение, сравнение — по моменту.
            to: "2026-11-02T03:00:00+03:00",
            limit: 10,
            offset: 5);

        await _service.Received(1).GetPageAsync(
            Arg.Is<CategorizationDecisionQuery>(query =>
                query.OnlyMismatch
                && query.Source == CategorizationDecisionSources.MerchantDefault
                && query.SuggestedCategoryId == (int)CommodityCategory.Dairy
                && query.FinalCategoryId == 0
                && query.Search == "хлеб"
                && query.From == new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)
                && query.To == new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)
                && query.Limit == 10
                && query.Offset == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDecisions_default_parameters_reach_read_service()
    {
        using var context = SetUser(isAdmin: true);

        _service.GetPageAsync(Arg.Any<CategorizationDecisionQuery>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<CategorizationDecisionRow>)[], 0));

        // Вызов без аргументов параметров — проверяются значения по умолчанию самого обработчика
        // (limit = 50, offset = 0, onlyMismatch = false, фильтры = null), те же использует binding.
        await CategorizationDecisionEndpoints.GetDecisions(
            new DefaultHttpContext(), _service, _userRepository);

        await _service.Received(1).GetPageAsync(
            Arg.Is<CategorizationDecisionQuery>(query =>
                !query.OnlyMismatch
                && query.Source == null
                && query.SuggestedCategoryId == null
                && query.FinalCategoryId == null
                && query.Search == null
                && query.From == null
                && query.To == null
                && query.Limit == 50
                && query.Offset == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDecisions_serializes_rows_with_adr_033_field_names()
    {
        // Имена полей ответа — строго список ADR-033 п. 7 (camelCase, без лишних полей):
        // исполнение 200 на реальном HttpContext с записью тела (JSON-конвейер приложения).
        using var context = SetUser(isAdmin: true);
        var row = CreateRow();

        var element = await SerializeSingleRowAsync(row);

        Assert.Equal(
            new[]
            {
                "acceptedByAdmin",
                "acceptedById",
                "acceptedByName",
                "commodityName",
                "decidedAt",
                "finalCategoryId",
                "finalCategoryName",
                "normalizedName",
                "suggestedCategoryId",
                "suggestedCategoryName",
                "suggestionSource",
            },
            element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal("Молоко 3.2%", element.GetProperty("commodityName").GetString());
        Assert.Equal("ai", element.GetProperty("suggestionSource").GetString());
        Assert.True(element.GetProperty("acceptedByAdmin").GetBoolean());

        // decidedAt — ISO 8601 со смещением: System.Text.Json отдаёт «+00:00», а не «Z»
        // (прецедент PrecategorizedCommodityDto) — тот же момент времени. Требование к T-11:
        // фронт парсит значение как ISO 8601 со смещением, а не ждёт строго «Z»
        // (в ADR-033 п. 7 пример приведён с «Z» — это тот же ISO 8601).
        var decidedAt = element.GetProperty("decidedAt").GetString();
        Assert.Equal(
            row.DecidedAt,
            DateTimeOffset.Parse(decidedAt!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public async Task GetDecisions_serializes_null_accepted_by_name_as_explicit_json_null()
    {
        // Требование T-11: «пользователь удалён / имя пустое» приходит как поле СО ЗНАЧЕНИЕМ null,
        // а не как отсутствующее поле (web-дефолты System.Text.Json null не игнорируют); id и
        // admin-флаг при этом заполнены (F1).
        using var context = SetUser(isAdmin: true);

        var element = await SerializeSingleRowAsync(CreateRow() with { AcceptedByName = null });

        Assert.True(element.TryGetProperty("acceptedByName", out var acceptedByName));
        Assert.Equal(JsonValueKind.Null, acceptedByName.ValueKind);
        Assert.False(string.IsNullOrEmpty(element.GetProperty("acceptedById").GetString()));
        Assert.True(element.GetProperty("acceptedByAdmin").GetBoolean());
    }

    [Fact]
    public void MapCategorizationDecisionEndpoints_registers_single_get_route_with_tag()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        var app = builder.Build();

        app.MapCategorizationDecisionEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        // Ровно один эндпоинт журнала и это GET: write-эндпоинт журнала запрещён ADR-033 п. 9.
        var decisionEndpoint = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText!.Contains("api/categorization-decisions", StringComparison.Ordinal));

        Assert.Equal(
            "GET",
            decisionEndpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single());
        Assert.True(decisionEndpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags.Contains("CategorizationDecisions"));
    }

    /// <summary>
    /// Проброс всех параметров обработчику явно (значения совпадают с дефолтами сигнатуры);
    /// тест значений по умолчанию вызывает обработчик напрямую, без этого помощника.
    /// </summary>
    private Task<IResult> InvokeAsync(
        HttpContext? httpContext = null,
        bool onlyMismatch = false,
        string? source = null,
        int? suggestedCategory = null,
        int? finalCategory = null,
        string? search = null,
        string? from = null,
        string? to = null,
        int limit = 50,
        int offset = 0)
        => CategorizationDecisionEndpoints.GetDecisions(
            httpContext ?? new DefaultHttpContext(),
            _service,
            _userRepository,
            onlyMismatch,
            source,
            suggestedCategory,
            finalCategory,
            search,
            from,
            to,
            limit,
            offset,
            CancellationToken.None);

    /// <summary>
    /// Проверяет код ответа И что результат исполняется на реальном HttpContext
    /// (прецедент PrecategorizedCommodityEndpointsTests: 403 обязан быть именно 403).
    /// </summary>
    private static async Task AssertStatusCodeAsync(IResult result, int expectedStatusCode)
    {
        // StatusCodeHttpResult при исполнении запрашивает сервисы из RequestServices.
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

    /// <summary>
    /// Исполняет 200 на реальном HttpContext с записью тела и возвращает единственный элемент
    /// массива (Clone — элемент переживает Dispose документа). Пользователь к этому моменту уже
    /// установлен через <see cref="SetUser"/>.
    /// </summary>
    private async Task<JsonElement> SerializeSingleRowAsync(CategorizationDecisionRow row)
    {
        _service.GetPageAsync(Arg.Any<CategorizationDecisionQuery>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<CategorizationDecisionRow>)new[] { row }, 1));

        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            // Ok<T> при исполнении резолвит JSON-сервис из RequestServices (минимальный набор —
            // AddRouting, как в прецедентном AssertStatusCodeAsync).
            RequestServices = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider(),
        };
        httpContext.Response.Body = body;

        var result = await InvokeAsync(httpContext);
        await result.ExecuteAsync(httpContext);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        return document.RootElement.EnumerateArray().Single().Clone();
    }

    private static CategorizationDecisionRow CreateRow() => new(
        "Молоко 3.2%",
        "молоко 3.2%",
        CategorizationDecisionSources.Ai,
        (int)CommodityCategory.Dairy,
        "Молочные продукты",
        (int)CommodityCategory.Bakery,
        "Хлеб и выпечка",
        Guid.NewGuid(),
        "Иван",
        true,
        new DateTimeOffset(2026, 11, 6, 3, 12, 44, TimeSpan.Zero));
}
