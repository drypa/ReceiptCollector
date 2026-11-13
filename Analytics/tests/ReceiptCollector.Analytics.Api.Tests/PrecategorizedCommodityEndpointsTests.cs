using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
/// Массовые операции страницы (ADR-036 §1): порядок проверок 401 → 403 (до разбора тела) →
/// 400 (валидация ids) → сервис, дедлайн и различение «наш дедлайн»/«клиент ушёл», агрегаты
/// и порядок results, инвариант MaxBulkIds = MaxLimit GET.
/// </summary>
public class PrecategorizedCommodityEndpointsTests
{
    /// <summary>Зеркало константы эндпоинта <c>MaxBulkIds</c> (ADR-036 §1.4, инвариант с GET).</summary>
    private const int MaxBulkIds = 200;

    private const string BulkTimeoutText = "Bulk operation timed out and was rolled back; no rows were applied.";
    private const string BulkFailureText = "Bulk operation failed and was rolled back; no rows were applied.";

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

        _service.ConfirmAsync(id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedCommodityResult(
                PrecategorizedOperationOutcome.Applied, id, (int)CommodityCategory.Dairy, "Молочные продукты", 0));

        var result = await PrecategorizedCommodityEndpoints.Confirm(id, _service, _userRepository, CancellationToken.None);
        var ok = Assert.IsType<Ok<ConfirmPrecategorizedCommodityResponse>>(result);
        Assert.Equal(id, ok.Value!.Id);
        Assert.Equal((int)CommodityCategory.Dairy, ok.Value.CategoryId);
        Assert.Equal("Молочные продукты", ok.Value.CategoryName);
        Assert.Equal(0, ok.Value.UpdatedCommodities);

        _service.ConfirmAsync(id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedCommodityResult(PrecategorizedOperationOutcome.Conflict, id, null, null, 0));
        Assert.IsType<Conflict<string>>(await PrecategorizedCommodityEndpoints.Confirm(
            id, _service, _userRepository, CancellationToken.None));

        _service.ConfirmAsync(id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedCommodityResult(PrecategorizedOperationOutcome.NotFound, id, null, null, 0));
        Assert.IsType<NotFound<string>>(await PrecategorizedCommodityEndpoints.Confirm(
            id, _service, _userRepository, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_passes_current_user_from_user_context_as_accepted_by()
    {
        // ADR-033 п. 5 (F1): принявший — авторизованный администратор из UserContext, не клиент.
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "admin", "external", 1, isAdmin: true));
        using var context = UserContext.SetUserId(userId);

        var id = Guid.NewGuid();
        _service.ConfirmAsync(id, userId, Arg.Any<CancellationToken>())
            .Returns(new ConfirmPrecategorizedCommodityResult(
                PrecategorizedOperationOutcome.Applied, id, (int)CommodityCategory.Dairy, "Молочные продукты", 1));

        var result = await PrecategorizedCommodityEndpoints.Confirm(id, _service, _userRepository, CancellationToken.None);

        var ok = Assert.IsType<Ok<ConfirmPrecategorizedCommodityResponse>>(result);
        Assert.Equal(1, ok.Value!.UpdatedCommodities);
        await _service.Received(1).ConfirmAsync(id, userId, Arg.Any<CancellationToken>());
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
        await _service.DidNotReceiveWithAnyArgs().ConfirmAsync(default, default, default);
        await _service.DidNotReceiveWithAnyArgs().DeclineAsync(default, default);
    }

    // ---------------------------------------------------------------------------
    // Массовые операции страницы (ADR-036 §1.1–§1.5): 401 → 403 (до разбора тела) → 400 → сервис.
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_without_user_return_unauthorized(bool isConfirm)
    {
        var result = await RunBatch(
            isConfirm, new DefaultHttpContext(), new BulkPrecategorizedRequest([Guid.NewGuid()]));

        Assert.IsType<UnauthorizedHttpResult>(result);
        await AssertBulkServiceNotCalledAsync(isConfirm);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task BatchEndpoints_with_non_admin_return_forbidden_before_body_validation(
        bool isConfirm,
        bool invalidBody)
    {
        // Права проверяются ДО разбора тела: не-админ с некорректным телом получает 403, а не 400
        // (ADR-036 §1).
        using var context = SetUser(isAdmin: false);
        var request = invalidBody ? new BulkPrecategorizedRequest(null) : new BulkPrecategorizedRequest([Guid.NewGuid()]);

        var result = await RunBatch(isConfirm, new DefaultHttpContext(), request);

        await AssertStatusCodeAsync(result, StatusCodes.Status403Forbidden);
        await AssertBulkServiceNotCalledAsync(isConfirm);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_with_missing_or_empty_ids_return_bad_request(bool isConfirm)
    {
        using var context = SetUser(isAdmin: true);
        var requests = new[]
        {
            null,
            new BulkPrecategorizedRequest(null),
            new BulkPrecategorizedRequest([]),
        };

        foreach (var request in requests)
        {
            var result = await RunBatch(isConfirm, new DefaultHttpContext(), request);

            var badRequest = Assert.IsType<BadRequest<string>>(result);
            Assert.Equal("ids must not be empty.", badRequest.Value);
        }

        await AssertBulkServiceNotCalledAsync(isConfirm);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_with_too_many_ids_return_bad_request(bool isConfirm)
    {
        using var context = SetUser(isAdmin: true);
        var ids = Enumerable.Range(0, MaxBulkIds + 1).Select(_ => Guid.NewGuid()).ToList();

        var result = await RunBatch(isConfirm, new DefaultHttpContext(), new BulkPrecategorizedRequest(ids));

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal($"ids must contain at most {MaxBulkIds} items.", badRequest.Value);
        await AssertBulkServiceNotCalledAsync(isConfirm);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_with_empty_guid_return_bad_request(bool isConfirm)
    {
        using var context = SetUser(isAdmin: true);
        var ids = new[] { Guid.NewGuid(), Guid.Empty };

        var result = await RunBatch(isConfirm, new DefaultHttpContext(), new BulkPrecategorizedRequest(ids));

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal("ids[1] is not a valid GUID.", badRequest.Value);
        await AssertBulkServiceNotCalledAsync(isConfirm);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_with_duplicate_id_return_bad_request(bool isConfirm)
    {
        // Дубликат отклоняется, а не «схлопывается»: иначе один товар и одно событие журнала
        // обработались бы дважды (ADR-036 §1.3).
        using var context = SetUser(isAdmin: true);
        var first = Guid.NewGuid();
        var ids = new[] { first, Guid.NewGuid(), first };

        var result = await RunBatch(isConfirm, new DefaultHttpContext(), new BulkPrecategorizedRequest(ids));

        var badRequest = Assert.IsType<BadRequest<string>>(result);
        Assert.Equal($"ids contains duplicate id '{first}'.", badRequest.Value);
        await AssertBulkServiceNotCalledAsync(isConfirm);
    }

    [Fact]
    public async Task ConfirmBatch_returns_aggregate_and_results_in_request_order()
    {
        using var context = SetUser(isAdmin: true);
        var appliedId = Guid.NewGuid();
        var alreadyAppliedId = Guid.NewGuid();
        var notFoundId = Guid.NewGuid();
        var ids = new[] { appliedId, alreadyAppliedId, notFoundId };

        // Сервис отдаёт results в порядке запроса (ADR-036 §1.1) — эндпоинт обязан его сохранить.
        _service.ConfirmManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BulkConfirmResult(
                3,
                1,
                2,
                7,
                [
                    new BulkConfirmItemResult(
                        appliedId, BulkItemOutcomes.Applied, null, (int)CommodityCategory.Dairy, "Молочные продукты", 7),
                    new BulkConfirmItemResult(
                        alreadyAppliedId, BulkItemOutcomes.Skipped, BulkSkipReasons.AlreadyApplied, null, null, null),
                    new BulkConfirmItemResult(
                        notFoundId, BulkItemOutcomes.Skipped, BulkSkipReasons.NotFound, null, null, null),
                ]));

        var result = await RunBatch(
            isConfirm: true, new DefaultHttpContext(), new BulkPrecategorizedRequest(ids));

        var ok = Assert.IsType<Ok<BulkConfirmResponse>>(result);
        var response = ok.Value!;
        Assert.Equal(ids.Length, response.Requested);
        Assert.Equal(1, response.Applied);
        Assert.Equal(2, response.Skipped);
        Assert.Equal(7, response.UpdatedCommodities);
        Assert.Equal(response.Requested, response.Applied + response.Skipped);

        var results = response.Results.ToList();
        Assert.Equal(ids, results.Select(item => item.Id));
        Assert.Equal(BulkItemOutcomes.Applied, results[0].Outcome);
        Assert.Null(results[0].Reason);
        Assert.Equal((int)CommodityCategory.Dairy, results[0].CategoryId);
        Assert.Equal("Молочные продукты", results[0].CategoryName);
        Assert.Equal(7, results[0].UpdatedCommodities);

        Assert.Equal(BulkItemOutcomes.Skipped, results[1].Outcome);
        Assert.Equal(BulkSkipReasons.AlreadyApplied, results[1].Reason);
        Assert.Null(results[1].CategoryId);
        Assert.Null(results[1].CategoryName);
        Assert.Null(results[1].UpdatedCommodities);

        Assert.Equal(BulkSkipReasons.NotFound, results[2].Reason);
    }

    [Fact]
    public async Task DeclineBatch_returns_aggregate_and_results_in_request_order()
    {
        using var context = SetUser(isAdmin: true);
        var declinedId = Guid.NewGuid();
        var skippedId = Guid.NewGuid();
        var ids = new[] { declinedId, skippedId };

        _service.DeclineManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new BulkDeclineResult(
                2,
                1,
                1,
                [
                    new BulkDeclineItemResult(declinedId, BulkItemOutcomes.Declined, null),
                    new BulkDeclineItemResult(skippedId, BulkItemOutcomes.Skipped, BulkSkipReasons.AlreadyApplied),
                ]));

        var result = await RunBatch(
            isConfirm: false, new DefaultHttpContext(), new BulkPrecategorizedRequest(ids));

        var ok = Assert.IsType<Ok<BulkDeclineResponse>>(result);
        var response = ok.Value!;
        Assert.Equal(ids.Length, response.Requested);
        Assert.Equal(1, response.Declined);
        Assert.Equal(1, response.Skipped);
        Assert.Equal(response.Requested, response.Declined + response.Skipped);

        var results = response.Results.ToList();
        Assert.Equal(ids, results.Select(item => item.Id));
        Assert.Equal(BulkItemOutcomes.Declined, results[0].Outcome);
        Assert.Null(results[0].Reason);
        Assert.Equal(BulkSkipReasons.AlreadyApplied, results[1].Reason);

        // Асимметрия намеренная (ADR-036 §1.2): у отклонения нет updatedCommodities —
        // отклонение не меняет commodities.
        Assert.DoesNotContain(
            typeof(BulkDeclineResponse).GetProperties(),
            property => property.Name.Contains(nameof(BulkConfirmResponse.UpdatedCommodities), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfirmBatch_response_wire_contract_is_lowercase_and_decline_has_no_updated_commodities()
    {
        using var context = SetUser(isAdmin: true);
        var id = Guid.NewGuid();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        _service.ConfirmManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new BulkConfirmResult(
                1, 1, 0, 3,
                [new BulkConfirmItemResult(id, BulkItemOutcomes.Applied, null, (int)CommodityCategory.Dairy, "Молочные продукты", 3)]));
        var confirmJson = JsonSerializer.Serialize(
            ((Ok<BulkConfirmResponse>)await RunBatch(isConfirm: true, new DefaultHttpContext(), new BulkPrecategorizedRequest([id]))).Value,
            jsonOptions);

        Assert.Contains("\"requested\":1", confirmJson, StringComparison.Ordinal);
        Assert.Contains("\"applied\":1", confirmJson, StringComparison.Ordinal);
        Assert.Contains("\"skipped\":0", confirmJson, StringComparison.Ordinal);
        Assert.Contains("\"updatedCommodities\":3", confirmJson, StringComparison.Ordinal);
        Assert.Contains("\"results\"", confirmJson, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"applied\"", confirmJson, StringComparison.Ordinal);
        Assert.Contains("\"categoryId\"", confirmJson, StringComparison.Ordinal);
        Assert.Contains("\"categoryName\"", confirmJson, StringComparison.Ordinal);

        _service.DeclineManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new BulkDeclineResult(
                1, 0, 1,
                [new BulkDeclineItemResult(id, BulkItemOutcomes.Skipped, BulkSkipReasons.NotFound)]));
        var declineJson = JsonSerializer.Serialize(
            ((Ok<BulkDeclineResponse>)await RunBatch(isConfirm: false, new DefaultHttpContext(), new BulkPrecategorizedRequest([id]))).Value,
            jsonOptions);

        Assert.Contains("\"declined\":0", declineJson, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"skipped\"", declineJson, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"not-found\"", declineJson, StringComparison.Ordinal);
        Assert.DoesNotContain("updatedCommodities", declineJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmBatch_with_max_page_size_passes_validation_and_max_page_is_processable_by_one_request()
    {
        // Инвариант ADR-036 §1.4: MaxBulkIds = MaxLimit у GET — «любая страница, которую GET
        // способен вернуть, обрабатывается одним запросом». Проверка поведенческая: страница
        // максимального размера проходит и валидацию limit, и валидацию ids.
        using var context = SetUser(isAdmin: true);
        var ids = Enumerable.Range(0, MaxBulkIds).Select(_ => Guid.NewGuid()).ToList();

        _service.ConfirmManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(AllAppliedConfirmResult(call.Arg<IReadOnlyCollection<Guid>>())));
        _service.GetPageAsync(MaxBulkIds, 0, null, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyCollection<PrecategorizedCommodityDto>)[], MaxBulkIds));

        var page = await PrecategorizedCommodityEndpoints.GetAll(
            new DefaultHttpContext(), _service, _userRepository, MaxBulkIds, 0, null, CancellationToken.None);
        Assert.IsType<Ok<IReadOnlyCollection<PrecategorizedCommodityDto>>>(page);

        var result = await RunBatch(isConfirm: true, new DefaultHttpContext(), new BulkPrecategorizedRequest(ids));

        var ok = Assert.IsType<Ok<BulkConfirmResponse>>(result);
        Assert.Equal(MaxBulkIds, ok.Value!.Requested);
        Assert.Equal(MaxBulkIds, ok.Value.Applied);
        Assert.Equal(0, ok.Value.Skipped);
        Assert.Equal(MaxBulkIds, ok.Value.Applied + ok.Value.Skipped);
        Assert.Equal(MaxBulkIds, ok.Value.Results.Count);
    }

    [Fact]
    public void MaxBulkIds_equals_get_max_limit()
    {
        // Инвариант ADR-036 §1.4: MaxBulkIds = MaxLimit у <c>GET</c> — «любая страница, которую
        // GET способен вернуть, обрабатывается одним запросом». Константы приватные, значения
        // сравниваются рефлексией: расхождение ловится сразу, а не только на границе 200/201
        // (поведенческий тест выше остаётся — он проверяет применимость инварианта).
        var maxLimit = ReadEndpointIntConstant("MaxLimit");
        var maxBulkIds = ReadEndpointIntConstant("MaxBulkIds");

        Assert.Equal(maxLimit, maxBulkIds);
        Assert.Equal(MaxBulkIds, maxBulkIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_when_deadline_elapsed_return_500_with_timeout_text(bool isConfirm)
    {
        // RequestAborted НЕ отменён → отменён наш дедлайн (ADR-036 §1.5): 500 с текстом про таймаут.
        using var context = SetUser(isAdmin: true);
        SetupBulkThrows(isConfirm, new OperationCanceledException("bulk deadline"));

        var result = await RunBatch(
            isConfirm, new DefaultHttpContext(), new BulkPrecategorizedRequest([Guid.NewGuid()]));

        await AssertBulkFailureAsync(result, BulkTimeoutText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_when_service_fails_return_500_with_rollback_text(bool isConfirm)
    {
        // Технический сбой БД/сети (constraint, deadlock 40P01, CommandTimeout): полный откат
        // и 500 с текстом про откат (ADR-036 §1.5).
        using var context = SetUser(isAdmin: true);
        SetupBulkThrows(isConfirm, new InvalidOperationException("database unavailable"));

        var result = await RunBatch(
            isConfirm, new DefaultHttpContext(), new BulkPrecategorizedRequest([Guid.NewGuid()]));

        await AssertBulkFailureAsync(result, BulkFailureText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchEndpoints_when_client_disconnected_propagate_cancellation_without_response(bool isConfirm)
    {
        // «Клиент ушёл» (ADR-036 §1.5): ответ не пишется, отмена пробрасывается.
        using var context = SetUser(isAdmin: true);
        using var requestAborted = new CancellationTokenSource();
        requestAborted.Cancel();
        using var body = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = requestAborted.Token,
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = body },
        };
        SetupBulkThrows(isConfirm, new OperationCanceledException("client disconnected"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunBatch(isConfirm, httpContext, new BulkPrecategorizedRequest([Guid.NewGuid()])));

        // Тело не записано, код не тронут — отвечать некому.
        Assert.Empty(body.ToArray());
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task ConfirmBatch_passes_current_user_from_user_context_as_accepted_by()
    {
        // ADR-033 п. 5 (F1): принявший — авторизованный администратор из UserContext, не клиент.
        var userId = Guid.NewGuid();
        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new User(userId, "admin", "external", 1, isAdmin: true));
        using var context = UserContext.SetUserId(userId);
        var id = Guid.NewGuid();

        _service.ConfirmManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(AllAppliedConfirmResult(call.Arg<IReadOnlyCollection<Guid>>())));

        var result = await RunBatch(isConfirm: true, new DefaultHttpContext(), new BulkPrecategorizedRequest([id]));

        Assert.IsType<Ok<BulkConfirmResponse>>(result);
        await _service.Received(1)
            .ConfirmManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MapPrecategorizedCommodityEndpoints_registers_batch_routes_with_tag()
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

        AssertBatchRoute(endpoints, "batch/confirm");
        AssertBatchRoute(endpoints, "batch/decline");
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

    /// <summary>
    /// Исполняет результат на реальном HttpContext и возвращает код ответа, content-type и текст
    /// тела: ошибки 500 — текстовые (ADR-027 п. 7, без ProblemDetails), их содержимое — часть
    /// контракта (ADR-036 §1.5).
    /// </summary>
    private static async Task<(int StatusCode, string? ContentType, string Body)> ExecuteWithBodyAsync(IResult result)
    {
        using var body = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = body },
        };

        await result.ExecuteAsync(httpContext);

        return (httpContext.Response.StatusCode, httpContext.Response.ContentType, Encoding.UTF8.GetString(body.ToArray()));
    }

    /// <summary>
    /// Технический сбой массовой операции: <c>500</c> с текстовым телом (ADR-036 §1.5) —
    /// ни <c>503</c>, ни ProblemDetails.
    /// </summary>
    private static async Task AssertBulkFailureAsync(IResult result, string expectedText)
    {
        var (statusCode, contentType, body) = await ExecuteWithBodyAsync(result);

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        Assert.Equal("text/plain; charset=utf-8", contentType);
        Assert.Equal(expectedText, body);
    }

    private static void AssertBatchRoute(IEnumerable<RouteEndpoint> endpoints, string routeSuffix)
    {
        var endpoint = Assert.Single(
            endpoints,
            candidate => candidate.RoutePattern.RawText!.EndsWith(routeSuffix, StringComparison.Ordinal));

        Assert.Equal(HttpMethod.Post.Method, Assert.Single(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.True(endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags.Contains("PrecategorizedCommodities") == true);
    }

    /// <summary>
    /// Значение приватной константы эндпоинтов (для проверки инварианта «<c>MaxBulkIds</c> =
    /// <c>MaxLimit</c> у <c>GET</c>», ADR-036 §1.4). Публичного доступа у констант нет — они
    /// деталь реализации, а не контракт.
    /// </summary>
    private static int ReadEndpointIntConstant(string constantName)
    {
        var field = typeof(PrecategorizedCommodityEndpoints)
            .GetField(constantName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);

        return (int)field!.GetRawConstantValue()!;
    }

    /// <summary>Результат «все строки применены» с сохранением порядка запроса (ADR-036 §1.1).</summary>
    private static BulkConfirmResult AllAppliedConfirmResult(IReadOnlyCollection<Guid> ids) => new(
        ids.Count,
        ids.Count,
        0,
        ids.Count,
        ids.Select(id => new BulkConfirmItemResult(
            id, BulkItemOutcomes.Applied, null, (int)CommodityCategory.Dairy, "Молочные продукты", 1)).ToList());

    private Task<IResult> RunBatch(bool isConfirm, HttpContext httpContext, BulkPrecategorizedRequest? request) =>
        isConfirm
            ? PrecategorizedCommodityEndpoints.ConfirmBatch(
                httpContext, request, _service, _userRepository, NullLoggerFactory.Instance)
            : PrecategorizedCommodityEndpoints.DeclineBatch(
                httpContext, request, _service, _userRepository, NullLoggerFactory.Instance);

    private void SetupBulkThrows(bool isConfirm, Exception exception)
    {
        if (isConfirm)
        {
            _ = _service.ConfirmManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(exception);
        }
        else
        {
            _ = _service.DeclineManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(exception);
        }
    }

    private async Task AssertBulkServiceNotCalledAsync(bool isConfirm)
    {
        if (isConfirm)
        {
            await _service.DidNotReceiveWithAnyArgs()
                .ConfirmManyAsync(default!, default, default);
        }
        else
        {
            await _service.DidNotReceiveWithAnyArgs()
                .DeclineManyAsync(default!, default);
        }
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
