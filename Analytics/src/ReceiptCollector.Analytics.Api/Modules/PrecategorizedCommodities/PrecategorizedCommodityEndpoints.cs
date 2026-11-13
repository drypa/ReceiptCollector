using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.PrecategorizedCommodities;

/// <summary>
/// Админский API очереди предкатегоризации (ADR-027 п.7, FR-5).
/// Права и стиль ошибок — по эталону <c>MerchantEndpoints</c>: текстовые тела,
/// без ProblemDetails (ADR-027 п.7, FR-5.9).
/// <para>
/// Массовые операции страницы — <c>POST /batch/confirm</c> и <c>POST /batch/decline</c>
/// (ADR-036 §1): stateless, весь список id обрабатывается одной транзакцией на уровне
/// репозитория (решения A3/B2). Порядок проверок — <c>401</c> → <c>403</c> (до разбора тела)
/// → валидация <c>400</c> (§1.3) → сервис. <c>404</c>/<c>409</c> на уровне батча
/// <b>не используются</b>: уже подтверждённая и отсутствующая строки — это <c>skipped</c>
/// с причиной при <c>200</c> (C1). Технический сбой — полный откат и <c>500</c> с текстом
/// (§1.5); при уходе клиента ответ не пишется.
/// </para>
/// </summary>
public static class PrecategorizedCommodityEndpoints
{
    /// <summary>
    /// Верхняя граница <c>limit</c> у <c>GET</c> (ADR-027 п.7). Связана инвариантом с
    /// <see cref="MaxBulkIds"/> (ADR-036 §1.4) — менять только вместе.
    /// </summary>
    private const int MaxLimit = 200;

    /// <summary>
    /// Максимум id в одной массовой операции (ADR-036 F1, §1.4). Равен <see cref="MaxLimit"/>
    /// у <c>GET</c> — это <b>инвариант</b>: «любая страница, которую <c>GET</c> способен вернуть,
    /// обрабатывается одним запросом» (решение заказчика 4). Меньший предел сломал бы его для
    /// API-клиентов с <c>limit &gt; 100</c>; при будущем изменении <c>MaxLimit</c> значения
    /// меняются <b>только вместе</b> (зафиксировано тестом «страница максимального размера
    /// проходит валидацию»).
    /// </summary>
    private const int MaxBulkIds = 200;

    /// <summary>
    /// Серверный дедлайн массовой операции (ADR-036 F1). Значение ниже <c>proxy_read_timeout
    /// 120 s</c> у nginx: запас ~30 с на авторизацию, ответ, сериализацию и сеть. Дедлайн
    /// меньше прокси-таймаута, поэтому при исчерпании бюджета сервер успевает откатить
    /// транзакцию и вернуть понятный <c>500</c> вместо обрыва соединения прокси без тела
    /// (§1.5). <c>CommandTimeout</c> Npgsql (30 с на команду) остаётся вторым, более ранним
    /// предохранителем. Новых настроек нет (F1) — только константа.
    /// </summary>
    private static readonly TimeSpan BulkOperationTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Имя действия в логах массового подтверждения (ADR-036 §7). Значение совпадает с
    /// действием в логах сервиса («Bulk confirm …»), чтобы строки слоя и слоя приложения
    /// коррелировались между собой.
    /// </summary>
    private const string BulkConfirmAction = "confirm";

    /// <summary>
    /// Имя действия в логах массового отклонения (ADR-036 §7). Значение совпадает с действием
    /// в логах сервиса («Bulk decline …»).
    /// </summary>
    private const string BulkDeclineAction = "decline";

    /// <summary>Текст <c>500</c>, когда дедлайн исчерпан, а запрос клиента жив (ADR-036 §1.5).</summary>
    private const string BulkTimeoutMessage = "Bulk operation timed out and was rolled back; no rows were applied.";

    /// <summary>Текст <c>500</c> при техническом сбое БД/сети (ADR-036 §1.5).</summary>
    private const string BulkFailureMessage = "Bulk operation failed and was rolled back; no rows were applied.";

    public static IEndpointRouteBuilder MapPrecategorizedCommodityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/precategorized-commodities");
        group.WithTags("PrecategorizedCommodities");

        group.MapGet("", GetAll);
        group.MapPut("/{id:guid}/category", UpdateCategory);
        group.MapPost("/{id:guid}/confirm", Confirm);
        group.MapDelete("/{id:guid}", Decline);

        // Массовые операции страницы (ADR-036 §1): литеральный сегмент "batch" не конфликтует
        // с {id:guid} (статический сегмент приоритетнее параметрического), порядок маппинга
        // значения не имеет.
        group.MapPost("/batch/confirm", ConfirmBatch);
        group.MapPost("/batch/decline", DeclineBatch);

        return app;
    }

    public static async Task<IResult> GetAll(
        HttpContext httpContext,
        [FromServices] IPrecategorizedCommodityService service,
        [FromServices] IUserRepository userRepository,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        // ADR-027 п.7: верхняя граница limit проверяется явно (в MerchantEndpoints её нет).
        if (limit < 1 || limit > MaxLimit)
        {
            return Results.BadRequest($"limit must be between 1 and {MaxLimit}.");
        }

        if (offset < 0)
        {
            return Results.BadRequest("offset cannot be negative.");
        }

        var (items, total) = await service.GetPageAsync(limit, offset, search, cancellationToken);

        httpContext.Response.Headers["X-Total-Count"] = total.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(items);
    }

    public static async Task<IResult> UpdateCategory(
        Guid id,
        [FromBody] UpdatePrecategorizedCategoryRequest request,
        [FromServices] IPrecategorizedCommodityService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        if (request.CategoryId == (int)CommodityCategory.Undefined
            || !Enum.IsDefined(typeof(CommodityCategory), request.CategoryId))
        {
            return Results.BadRequest("Invalid category.");
        }

        var category = (CommodityCategory)request.CategoryId;
        var outcome = await service.UpdateCategoryAsync(id, category, cancellationToken);

        return outcome switch
        {
            PrecategorizedOperationOutcome.NotFound => Results.NotFound("Precategorized commodity not found."),
            PrecategorizedOperationOutcome.Conflict => Results.Conflict("Precategorized commodity is already applied."),
            _ => Results.Ok(new UpdatePrecategorizedCategoryResponse(
                id,
                request.CategoryId,
                CommodityCategoryHelper.GetDisplayName(category)))
        };
    }

    public static async Task<IResult> Confirm(
        Guid id,
        [FromServices] IPrecategorizedCommodityService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        // Принявший — авторизованный администратор (ADR-033 п. 5, F1): после AdminAuthorization
        // пользователь гарантированно есть (401/403 возвращены выше), поэтому !.Value безопасен.
        var result = await service.ConfirmAsync(id, UserContext.UserId!.Value, cancellationToken);

        return result.Outcome switch
        {
            PrecategorizedOperationOutcome.NotFound => Results.NotFound("Precategorized commodity not found."),
            PrecategorizedOperationOutcome.Conflict => Results.Conflict("Precategorized commodity is already applied."),
            _ => Results.Ok(new ConfirmPrecategorizedCommodityResponse(
                result.Id,
                result.CategoryId!.Value,
                result.CategoryName!,
                result.UpdatedCommodities))
        };
    }

    public static async Task<IResult> Decline(
        Guid id,
        [FromServices] IPrecategorizedCommodityService service,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        var outcome = await service.DeclineAsync(id, cancellationToken);

        return outcome switch
        {
            PrecategorizedOperationOutcome.NotFound => Results.NotFound("Precategorized commodity not found."),
            PrecategorizedOperationOutcome.Conflict => Results.Conflict("Precategorized commodity is already applied."),
            _ => Results.NoContent()
        };
    }

    /// <summary>
    /// Массовое подтверждение строк текущей страницы (ADR-036 §1.1): весь список id — одной
    /// транзакцией сервиса, логические конфликты становятся <c>skipped</c> с причиной при
    /// <c>200</c> (C1). Порядок проверок: <c>401</c> → <c>403</c> (права проверяются <b>до</b>
    /// разбора тела — не-админ с некорректным телом получает <c>403</c>) → валидация
    /// <c>400</c> (§1.3) → сервис с серверным дедлайном (§1.5).
    /// </summary>
    public static async Task<IResult> ConfirmBatch(
        HttpContext httpContext,
        [FromBody] BulkPrecategorizedRequest? request,
        [FromServices] IPrecategorizedCommodityService service,
        [FromServices] IUserRepository userRepository,
        [FromServices] ILoggerFactory loggerFactory)
    {
        // 401 → 403 — до разбора тела (ADR-036 §1).
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, httpContext.RequestAborted);
        if (authorization is not null)
        {
            return authorization;
        }

        var validationError = ValidateBulkIds(request?.Ids);
        if (validationError is not null)
        {
            return Results.BadRequest(validationError);
        }

        var ids = request!.Ids!;
        var logger = loggerFactory.CreateLogger("PrecategorizedCommodityEndpoints");

        // Принявший — авторизованный администратор (ADR-033 п. 5, F1): после AdminAuthorization
        // пользователь гарантированно есть (401/403 возвращены выше), поэтому !.Value безопасен.
        var acceptedByUserId = UserContext.UserId!.Value;

        return await RunBulkOperationAsync(
            BulkConfirmAction,
            ids.Count,
            httpContext,
            logger,
            async cancellationToken =>
            {
                var result = await service.ConfirmManyAsync(ids, acceptedByUserId, cancellationToken);
                return Results.Ok(ToBulkConfirmResponse(result));
            });
    }

    /// <summary>
    /// Массовое отклонение строк текущей страницы (ADR-036 §1.2): та же одна транзакция и та же
    /// классификация конфликтов, что у <see cref="ConfirmBatch"/>; в ответе нет
    /// <c>updatedCommodities</c> — отклонение не меняет <c>commodities</c> (асимметрия намеренная).
    /// </summary>
    public static async Task<IResult> DeclineBatch(
        HttpContext httpContext,
        [FromBody] BulkPrecategorizedRequest? request,
        [FromServices] IPrecategorizedCommodityService service,
        [FromServices] IUserRepository userRepository,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, httpContext.RequestAborted);
        if (authorization is not null)
        {
            return authorization;
        }

        var validationError = ValidateBulkIds(request?.Ids);
        if (validationError is not null)
        {
            return Results.BadRequest(validationError);
        }

        var ids = request!.Ids!;
        var logger = loggerFactory.CreateLogger("PrecategorizedCommodityEndpoints");

        return await RunBulkOperationAsync(
            BulkDeclineAction,
            ids.Count,
            httpContext,
            logger,
            async cancellationToken =>
            {
                var result = await service.DeclineManyAsync(ids, cancellationToken);
                return Results.Ok(ToBulkDeclineResponse(result));
            });
    }

    /// <summary>
    /// Валидация <c>ids</c> по ADR-036 §1.3 (зеркально <c>CommodityBatchCategorizationEndpoints</c>,
    /// ADR-028 п. 1.2). Возвращает текст <c>400</c> или <see langword="null"/>, если список корректен.
    /// Дубликаты отклоняются, а не «схлопываются»: повтор id — ошибка клиента, иначе один товар
    /// и одно событие журнала могли бы обработаться дважды.
    /// </summary>
    private static string? ValidateBulkIds(IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            return "ids must not be empty.";
        }

        if (ids.Count > MaxBulkIds)
        {
            return $"ids must contain at most {MaxBulkIds} items.";
        }

        var seen = new HashSet<Guid>(ids.Count);
        for (var index = 0; index < ids.Count; index++)
        {
            var id = ids[index];

            if (id == Guid.Empty)
            {
                return $"ids[{index}] is not a valid GUID.";
            }

            if (!seen.Add(id))
            {
                return $"ids contains duplicate id '{id}'.";
            }
        }

        return null;
    }

    /// <summary>
    /// Общий каркас массовой операции: серверный дедлайн (F1) и разделение исходов по ADR-036 §1.5.
    /// <list type="bullet">
    ///   <item>отменён <c>RequestAborted</c> — клиента уже нет: исключение пробрасывается,
    ///   ответ не пишется (транзакция откатана на уровне репозитория);</item>
    ///   <item><c>OperationCanceledException</c> при живом запросе — сработал наш дедлайн:
    ///   <c>Warning</c> + <c>500</c> с текстом про таймаут;</item>
    ///   <item>прочее исключение (БД/сеть/constraint/deadlock <c>40P01</c>/<c>CommandTimeout</c>) —
    ///   <c>Error</c> с типом исключения и <c>SqlState</c> + <c>500</c> с текстом про откат.</item>
    /// </list>
    /// Дедлайн покрывает именно операцию (транзакцию): авторизация и валидация в бюджет не входят
    /// (отсюда и запас ~30 с в значении константы).
    /// </summary>
    private static async Task<IResult> RunBulkOperationAsync(
        string action,
        int requestedCount,
        HttpContext httpContext,
        ILogger logger,
        Func<CancellationToken, Task<IResult>> invoke)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
        cts.CancelAfter(BulkOperationTimeout);

        try
        {
            return await invoke(cts.Token);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // «Клиент ушёл» (ADR-036 §1.5): отвечать некому и не нужно, транзакция уже откатана.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // «Наш дедлайн» (запрос жив): ни одна строка не применена (commit ровно один и последний).
            // Число классифицированных строк здесь недоступно — оно только в Debug-логе репозитория.
            logger.LogWarning(
                ex,
                "Bulk {Action} of {RequestedCount} proposals hit the {TimeoutSeconds} s server deadline and was rolled back; "
                + "no rows were applied. The number of classified rows is only available in the repository Debug log.",
                action,
                requestedCount,
                BulkOperationTimeout.TotalSeconds);

            return BulkOperationFailed(BulkTimeoutMessage);
        }
        catch (Exception ex)
        {
            // Технический сбой: полный откат (решение 6). Тип исключения и SqlState — в логе,
            // наименования товаров и ids нет (логи без ПД).
            logger.LogError(
                ex,
                "Bulk {Action} of {RequestedCount} proposals failed and was rolled back; no rows were applied. "
                + "Exception type: {ExceptionType}, SQL state: {SqlState}.",
                action,
                requestedCount,
                ex.GetType().Name,
                TryGetSqlState(ex));

            return BulkOperationFailed(BulkFailureMessage);
        }
    }

    /// <summary>
    /// Текст ошибки <c>500</c> — текстовое тело (стиль модуля, ADR-027 п. 7), без ProblemDetails;
    /// <c>503</c>/<c>504</c> не используются (§1.5).
    /// </summary>
    private static IResult BulkOperationFailed(string message) =>
        Results.Text(
            message,
            "text/plain; charset=utf-8",
            contentEncoding: null,
            statusCode: StatusCodes.Status500InternalServerError);

    /// <summary>
    /// <c>SqlState</c> для <c>Error</c>-лога (ADR-036 §7). Api намеренно не ссылается на Npgsql:
    /// значение берётся из <see cref="DbException.SqlState"/> (его переопределяет
    /// <c>PostgresException</c>) по всей цепочке вложенных исключений.
    /// </summary>
    private static string? TryGetSqlState(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { Length: > 0 } sqlState })
            {
                return sqlState;
            }
        }

        return null;
    }

    /// <summary>
    /// Маппинг сервисного результата в HTTP-тело (ADR-036 §1.1): порядок <c>results</c> — порядок
    /// запроса (его гарантирует сервис), wire-имена — lowercase (стиль проекта).
    /// У <c>skipped</c>-строк поля категории и счётчика остаются <see langword="null"/>.
    /// </summary>
    private static BulkConfirmResponse ToBulkConfirmResponse(BulkConfirmResult result) => new(
        result.Requested,
        result.Applied,
        result.Skipped,
        result.UpdatedCommodities,
        result.Results
            .Select(item => new BulkConfirmItemResponse(
                item.Id,
                item.Outcome,
                item.Reason,
                item.CategoryId,
                item.CategoryName,
                item.UpdatedCommodities))
            .ToList());

    /// <summary>Маппинг сервисного результата в HTTP-тело (ADR-036 §1.2); <c>updatedCommodities</c> нет.</summary>
    private static BulkDeclineResponse ToBulkDeclineResponse(BulkDeclineResult result) => new(
        result.Requested,
        result.Declined,
        result.Skipped,
        result.Results
            .Select(item => new BulkDeclineItemResponse(item.Id, item.Outcome, item.Reason))
            .ToList());
}

public sealed record UpdatePrecategorizedCategoryRequest(int CategoryId);

public sealed record UpdatePrecategorizedCategoryResponse(Guid Id, int CategoryId, string CategoryName);

public sealed record ConfirmPrecategorizedCommodityResponse(
    Guid Id,
    int CategoryId,
    string CategoryName,
    int UpdatedCommodities);

/// <summary>
/// Тело обоих массовых запросов (ADR-036 §1.1/§1.2): <b>только</b> id строк очереди.
/// Применяемая категория берётся из снимка строки в транзакции (D1), клиент её не передаёт.
/// <c>null</c>/пустой список отклоняются как <c>400</c> (§1.3); JSON-literal <c>null</c>
/// вместо объекта — тоже <c>400</c>, а не NRE/500 (прецедент <c>MerchantEndpoints</c>).
/// </summary>
public sealed record BulkPrecategorizedRequest(IReadOnlyList<Guid>? Ids);

/// <summary>
/// Ответ <c>POST /batch/confirm</c> (ADR-036 §1.1): агрегат и построчный отчёт в порядке
/// запроса. Инвариант: <c>applied + skipped = requested</c>.
/// </summary>
public sealed record BulkConfirmResponse(
    int Requested,
    int Applied,
    int Skipped,
    int UpdatedCommodities,
    IReadOnlyCollection<BulkConfirmItemResponse> Results);

/// <summary>
/// Строка отчёта подтверждения (ADR-036 §1.1): <c>outcome</c> ∈ <c>applied</c> | <c>skipped</c>,
/// <c>reason</c> ∈ <c>already-applied</c> | <c>not-found</c> — только у <c>skipped</c>;
/// у <c>skipped</c> поля категории и счётчика — <c>null</c>.
/// </summary>
public sealed record BulkConfirmItemResponse(
    Guid Id,
    string Outcome,
    string? Reason,
    int? CategoryId,
    string? CategoryName,
    int? UpdatedCommodities);

/// <summary>
/// Ответ <c>POST /batch/decline</c> (ADR-036 §1.2): агрегат и построчный отчёт в порядке
/// запроса. Инвариант: <c>declined + skipped = requested</c>. Поля <c>updatedCommodities</c>
/// нет намеренно — отклонение не меняет <c>commodities</c> (§1.2).
/// </summary>
public sealed record BulkDeclineResponse(
    int Requested,
    int Declined,
    int Skipped,
    IReadOnlyCollection<BulkDeclineItemResponse> Results);

/// <summary>Строка отчёта отклонения (ADR-036 §1.2): <c>outcome</c> ∈ <c>declined</c> | <c>skipped</c>.</summary>
public sealed record BulkDeclineItemResponse(
    Guid Id,
    string Outcome,
    string? Reason);
