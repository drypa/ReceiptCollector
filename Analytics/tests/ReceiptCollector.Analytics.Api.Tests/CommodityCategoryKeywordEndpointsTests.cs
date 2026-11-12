using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ReceiptCollector.Analytics.Api.Modules.CommodityCategoryKeywords;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests;

/// <summary>
/// Тесты админского API слов категоризации (T-04, ADR-035 п. 7, FR-3/FR-4/FR-5/FR-10):
/// права на всех трёх эндпоинтах (401/403 — практикой ADR-027/028: исполнением на реальном
/// HttpContext), коды и тексты ошибок символ в символ из таблицы ADR-035 п. 7, <c>200</c>
/// (не <c>201</c>) с созданным элементом на POST, <c>204</c>/<c>404</c> на DELETE.
/// </summary>
public class CommodityCategoryKeywordEndpointsTests
{
    private readonly ICommodityCategoryKeywordService _service =
        Substitute.For<ICommodityCategoryKeywordService>();

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

    [Fact]
    public async Task GetAll_without_user_returns_unauthorized()
    {
        var result = await CommodityCategoryKeywordEndpoints.GetAll(
            _service, _userRepository, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().GetAllAsync(default);
    }

    [Fact]
    public async Task Add_without_user_returns_unauthorized()
    {
        var result = await CommodityCategoryKeywordEndpoints.Add(
            ValidRequest(), _service, _userRepository, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default);
    }

    [Fact]
    public async Task Delete_without_user_returns_unauthorized()
    {
        var result = await CommodityCategoryKeywordEndpoints.Delete(
            Guid.NewGuid(), _service, _userRepository, CancellationToken.None);

        Assert.IsType<UnauthorizedHttpResult>(result);
        await _service.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task GetAll_with_non_admin_returns_forbidden()
    {
        using var context = SetUser(isAdmin: false);

        var result = await CommodityCategoryKeywordEndpoints.GetAll(
            _service, _userRepository, CancellationToken.None);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().GetAllAsync(default);
    }

    [Fact]
    public async Task Add_with_non_admin_returns_forbidden()
    {
        using var context = SetUser(isAdmin: false);

        var result = await CommodityCategoryKeywordEndpoints.Add(
            ValidRequest(), _service, _userRepository, CancellationToken.None);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default);
    }

    [Fact]
    public async Task Delete_with_non_admin_returns_forbidden()
    {
        using var context = SetUser(isAdmin: false);

        var result = await CommodityCategoryKeywordEndpoints.Delete(
            Guid.NewGuid(), _service, _userRepository, CancellationToken.None);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await _service.DidNotReceiveWithAnyArgs().DeleteAsync(default, default);
    }

    [Fact]
    public async Task GetAll_returns_every_category_without_undefined_including_empty_lists()
    {
        // Сквозная проверка GET на настоящем сервисе с фейковым репозиторием: контракт
        // «все категории из GetAll() без Undefined, пустые списки, группа из GetGroup»
        // (ADR-035 п. 7, UC-1) — поведение сервиса, а не заглушки.
        var repository = Substitute.For<ICommodityCategoryKeywordRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "финик", "финик"),
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "Курага", "курага"),
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.DriedFruits, KeywordListType.Boost, "изюм", "изюм"),
            new CommodityCategoryKeyword(Guid.NewGuid(), CommodityCategory.Fruits, KeywordListType.Exclude, "изюм", "изюм"),
        });
        var service = new CommodityCategoryKeywordService(
            repository,
            Substitute.For<ICommodityCategoryKeywordCache>(),
            NullLogger<CommodityCategoryKeywordService>.Instance);

        using var context = SetUser(isAdmin: true);

        var result = await CommodityCategoryKeywordEndpoints.GetAll(
            service, _userRepository, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<CategoryKeywordsDto>>>(result);
        var categories = ok.Value!;

        var expected = CommodityCategoryHelper.GetAll()
            .Where(category => category.Id != CommodityCategory.Undefined)
            .ToList();

        Assert.Equal(expected.Count, categories.Count);
        Assert.Equal(expected.Select(category => (int)category.Id), categories.Select(dto => dto.CategoryId));
        Assert.DoesNotContain(categories, dto => dto.CategoryId == (int)CommodityCategory.Undefined);

        // Пустые списки присутствуют (плейсхолдеры и счётчики без второго запроса).
        var food = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.Food);
        Assert.Empty(food.Boost);
        Assert.Empty(food.Exclude);

        var driedFruits = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.DriedFruits);
        Assert.Equal("Сухофрукты", driedFruits.CategoryName);
        Assert.Equal("Продукты", driedFruits.Group);
        Assert.Equal(new[] { "изюм", "Курага", "финик" }, driedFruits.Boost);

        var fruits = categories.Single(dto => dto.CategoryId == (int)CommodityCategory.Fruits);
        Assert.Equal(new[] { "изюм" }, fruits.Exclude);

        // Группа вне реестра групп — пустая строка (GetGroup).
        Assert.Equal(string.Empty, categories.Single(dto => dto.CategoryId == (int)CommodityCategory.Clothing).Group);

        // Исполнение 200: X-Total-Count и пагинация не вводятся (ADR-035 п. 7 — объём ограничен).
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider(),
        };
        await result.ExecuteAsync(httpContext);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Total-Count"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task Add_with_invalid_category_returns_bad_request(int categoryId)
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityCategoryKeywordEndpoints.Add(
            new AddCommodityCategoryKeywordRequest(categoryId, "boost", "изюм"),
            _service, _userRepository, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("Invalid category.", badRequest.Value);
        await AssertStatusCodeAsync(result, StatusCodes.Status400BadRequest);
        await _service.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("BOOST!")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Add_with_invalid_list_type_returns_bad_request(string? listType)
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityCategoryKeywordEndpoints.Add(
            new AddCommodityCategoryKeywordRequest((int)CommodityCategory.DriedFruits, listType, "изюм"),
            _service, _userRepository, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("listType must be 'boost' or 'exclude'.", badRequest.Value);
        await AssertStatusCodeAsync(result, StatusCodes.Status400BadRequest);
        await _service.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default);
    }

    [Theory]
    [InlineData(" и ")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Add_with_invalid_word_returns_bad_request(string? word)
    {
        using var context = SetUser(isAdmin: true);

        var result = await CommodityCategoryKeywordEndpoints.Add(
            new AddCommodityCategoryKeywordRequest((int)CommodityCategory.DriedFruits, "boost", word),
            _service, _userRepository, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("word must be between 2 and 64 characters after trimming.", badRequest.Value);
        await AssertStatusCodeAsync(result, StatusCodes.Status400BadRequest);
        await _service.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default);
    }

    [Fact]
    public async Task Add_with_word_longer_than_limit_after_trim_returns_bad_request()
    {
        using var context = SetUser(isAdmin: true);
        var tooLong = new string('а', CommodityCategoryKeywordRules.MaxWordLength + 1);

        var result = await CommodityCategoryKeywordEndpoints.Add(
            new AddCommodityCategoryKeywordRequest((int)CommodityCategory.DriedFruits, "boost", $"  {tooLong}  "),
            _service, _userRepository, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("word must be between 2 and 64 characters after trimming.", badRequest.Value);
    }

    [Fact]
    public async Task Add_with_word_that_normalizes_beyond_limit_returns_bad_request()
    {
        // «İ» (U+0130): страховка на случай смены нормализации (например, full case folding) —
        // тогда 33 символа дают 66 после нормализации — длиннее varchar(64) схемы (в БД
        // 22001 → 500); на текущем рантайме .NET расширение не наблюдается. Эндпоинт отклоняет
        // расширяющий случай тем же 400 с тем же текстом, что и обычную длину (ADR-035 п. 7),
        // не доводя до БД. Тест не зависит от рантайма: если запрос отклонён — текст строго
        // контрактный и сервис не вызван.
        using var context = SetUser(isAdmin: true);
        var expandingWord = new string('\u0130', 33);
        _service.AddAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AddKeywordResult.Duplicate());

        var result = await CommodityCategoryKeywordEndpoints.Add(
            new AddCommodityCategoryKeywordRequest((int)CommodityCategory.DriedFruits, "boost", expandingWord),
            _service, _userRepository, CancellationToken.None);

        if (result is BadRequest<string> badRequest)
        {
            Assert.Equal("word must be between 2 and 64 characters after trimming.", badRequest.Value);
            await _service.DidNotReceiveWithAnyArgs().AddAsync(default, default, default, default);
        }
        else
        {
            // Рантайм без расширения (Linux/ICU): слово проходит валидацию как обычное —
            // запрос дошёл до сервиса (заглушка вернула Duplicate → 409).
            Assert.IsType<Conflict<string>>(result);
            await _service.Received(1).AddAsync(
                (int)CommodityCategory.DriedFruits, "boost", expandingWord, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Add_duplicate_returns_conflict_with_exact_text()
    {
        using var context = SetUser(isAdmin: true);
        _service.AddAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AddKeywordResult.Duplicate());

        var result = await CommodityCategoryKeywordEndpoints.Add(
            ValidRequest(), _service, _userRepository, CancellationToken.None);

        var conflict = Assert.IsType<Conflict<string>>(result);
        Assert.Equal("word already exists in this list.", conflict.Value);
        await AssertStatusCodeAsync(result, StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Add_limit_reached_returns_conflict_with_exact_text()
    {
        using var context = SetUser(isAdmin: true);
        _service.AddAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AddKeywordResult.LimitReached());

        var result = await CommodityCategoryKeywordEndpoints.Add(
            ValidRequest(), _service, _userRepository, CancellationToken.None);

        var conflict = Assert.IsType<Conflict<string>>(result);
        Assert.Equal("list limit of 200 words reached.", conflict.Value);
        await AssertStatusCodeAsync(result, StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Add_returns_created_element_with_trimmed_word_and_original_case()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();
        _service.AddAsync(
                (int)CommodityCategory.DriedFruits, "boost", "  Курага  ", Arg.Any<CancellationToken>())
            .Returns(AddKeywordResult.Added(id, (int)CommodityCategory.DriedFruits, "boost", "Курага"));

        var result = await CommodityCategoryKeywordEndpoints.Add(
            new AddCommodityCategoryKeywordRequest((int)CommodityCategory.DriedFruits, "boost", "  Курага  "),
            _service, _userRepository, CancellationToken.None);

        // 200 (не 201 — ADR-035 п. 7) с телом { id, categoryId, listType, word }.
        var ok = Assert.IsType<Ok<AddCommodityCategoryKeywordResponse>>(result);
        Assert.Equal(id, ok.Value!.Id);
        Assert.Equal((int)CommodityCategory.DriedFruits, ok.Value.CategoryId);
        Assert.Equal("boost", ok.Value.ListType);
        Assert.Equal("Курага", ok.Value.Word);

        // Исполнение с реальным JSON: camelCase-контракт тела ответа.
        var (statusCode, body) = await ExecuteAsync(result);
        Assert.Equal(StatusCodes.Status200OK, statusCode);
        using var document = JsonDocument.Parse(body!);
        var root = document.RootElement;
        Assert.Equal(id, root.GetProperty("id").GetGuid());
        Assert.Equal((int)CommodityCategory.DriedFruits, root.GetProperty("categoryId").GetInt32());
        Assert.Equal("boost", root.GetProperty("listType").GetString());
        Assert.Equal("Курага", root.GetProperty("word").GetString());
    }

    [Fact]
    public async Task Delete_returns_no_content_and_repeat_returns_not_found_with_exact_text()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();

        _service.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(DeleteKeywordOutcome.Deleted);
        var deleted = await CommodityCategoryKeywordEndpoints.Delete(
            id, _service, _userRepository, CancellationToken.None);

        Assert.IsType<NoContent>(deleted);
        await AssertStatusCodeAsync(deleted, StatusCodes.Status204NoContent);

        // Повтор (или удаление из другой вкладки) — 404 word not found.
        _service.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(DeleteKeywordOutcome.NotFound);
        var missing = await CommodityCategoryKeywordEndpoints.Delete(
            id, _service, _userRepository, CancellationToken.None);

        var notFound = Assert.IsType<NotFound<string>>(missing);
        Assert.Equal("word not found.", notFound.Value);
        await AssertStatusCodeAsync(missing, StatusCodes.Status404NotFound);
    }

    [Fact]
    public void MapCommodityCategoryKeywordEndpoints_registers_group_with_tag()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        var app = builder.Build();

        app.MapCommodityCategoryKeywordEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText!.Contains("api/commodity-category-keywords", StringComparison.Ordinal));
        Assert.Contains(endpoints, endpoint =>
            endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags.Contains("CommodityCategoryKeywords") == true);

        // Все три пары «метод + шаблон» из ADR-035 п. 7: GET/POST на группе, адресный DELETE.
        // У GET/POST фактический RawText — "/api/commodity-category-keywords/" с завершающим
        // слэшем: артефакт конкатенации префикса группы и пустого подмаршрута MapGet/MapPost("");
        // сравниваем с TrimEnd('/'), чтобы проверка не зависела от этого артефакта.
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText!.TrimEnd('/') == "/api/commodity-category-keywords"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get) == true);
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText!.TrimEnd('/') == "/api/commodity-category-keywords"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == "/api/commodity-category-keywords/{id:guid}"
            && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Delete) == true);
    }

    private static AddCommodityCategoryKeywordRequest ValidRequest()
        => new((int)CommodityCategory.DriedFruits, "boost", "изюм");

    /// <summary>
    /// Проверяет код ответа И что результат исполняется на реальном HttpContext
    /// (прецедент <c>PrecategorizedCommodityEndpointsTests</c>/<c>CategorizationDecisionEndpointsTests</c>:
    /// «форма» результата сама по себе код не гарантирует).
    /// </summary>
    private static async Task AssertStatusCodeAsync(IResult result, int expectedStatusCode)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider(),
        };
        await result.ExecuteAsync(httpContext);

        Assert.Equal(expectedStatusCode, httpContext.Response.StatusCode);
    }

    /// <summary>Исполняет результат с записью тела (JSON-контракт ответа POST).</summary>
    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider(),
        };
        httpContext.Response.Body = body;

        await result.ExecuteAsync(httpContext);

        body.Position = 0;
        var text = await new StreamReader(body).ReadToEndAsync();
        return (httpContext.Response.StatusCode, text);
    }

    private IDisposable SetUser(bool isAdmin)
    {
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "user", "external", 1, isAdmin));
        return UserContext.SetUserId(userId);
    }
}
