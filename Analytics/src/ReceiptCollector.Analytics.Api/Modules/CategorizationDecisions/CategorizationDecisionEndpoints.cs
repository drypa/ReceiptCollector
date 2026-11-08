using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.CategorizationDecisions;

/// <summary>
/// Админский API журнала решений категоризации (ADR-033 п. 7, FR-6/FR-7): единственный эндпоинт
/// модуля — страница уникальных сочетаний. Права и стиль ошибок — по эталону админских эндпоинтов
/// (<see cref="AdminAuthorization"/>: нет пользователя → 401, не админ → 403; текстовые тела,
/// без ProblemDetails — ADR-027/028).
/// <para>
/// Write-эндпоинтов журнала нет и быть не может (ADR-033 п. 9): события создаются исключительно
/// серверными save-путями (T-04…T-06), чтение журнала глобально — поэтому доступ закрыт admin-only.
/// </para>
/// </summary>
public static class CategorizationDecisionEndpoints
{
    /// <summary>Размер страницы по умолчанию (ADR-033 п. 7).</summary>
    private const int DefaultLimit = 50;

    /// <summary>Верхняя граница limit; проверяется явно, как в ADR-027 п. 7.</summary>
    private const int MaxLimit = 200;

    /// <summary>
    /// Подсказка допустимых wire-имён источника в тексте ошибки — фиксированный порядок
    /// (перебор <see cref="CategorizationDecisionSources.All"/> дал бы недетерминированный
    /// порядок FrozenSet), имена берутся из констант, а не копируются строками.
    /// </summary>
    private static readonly string AllowedSourceNames =
        $"{CategorizationDecisionSources.Ai}, {CategorizationDecisionSources.Precategorized}, "
        + $"{CategorizationDecisionSources.Cache}, {CategorizationDecisionSources.MerchantDefault}";

    public static IEndpointRouteBuilder MapCategorizationDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categorization-decisions");
        group.WithTags("CategorizationDecisions");

        // Только чтение: write-эндпоинт журнала запрещён ADR-033 п. 9.
        group.MapGet("", GetDecisions);

        return app;
    }

    /// <summary>
    /// Страница уникальных сочетаний журнала (FR-6/FR-7): ответ <c>200</c> массивом строк,
    /// <c>X-Total-Count</c> — число уникальных сочетаний под фильтром (<c>Total</c> read-сервиса,
    /// а не число сырых событий). JSON-поля — по ADR-033 п. 7 (camelCase от свойств
    /// <see cref="CategorizationDecisionRow"/>).
    /// <para>
    /// Валидация параметров — здесь, до обращения к read-сервису: репозиторий намеренно НЕ
    /// валидирует диапазон <c>Limit</c>/<c>Offset</c> (требование, зафиксированное в ревью T-07) и
    /// на внедиапазонные значения бросает исключение — эндпоинт обязан вернуть 400.
    /// Дополнительных правил нет (например, <c>from &gt; to</c> не валидируется — выдача просто
    /// пустая, ADR-033 п. 7).
    /// </para>
    /// </summary>
    public static async Task<IResult> GetDecisions(
        HttpContext httpContext,
        [FromServices] ICategorizationDecisionReadService service,
        [FromServices] IUserRepository userRepository,
        [FromQuery] bool onlyMismatch = false,
        [FromQuery] string? source = null,
        [FromQuery] int? suggestedCategory = null,
        [FromQuery] int? finalCategory = null,
        [FromQuery] string? search = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AdminAuthorization.AuthorizeAsync(userRepository, cancellationToken);
        if (authorization is not null)
        {
            return authorization;
        }

        // Порядок проверок — как в ADR-033 п. 7: source → suggestedCategory → finalCategory →
        // from/to → limit/offset. Параметр присутствует (не null) и невалиден → 400; пустая
        // строка source/from/to — тоже «присутствует», отдельных правил для неё не вводится.

        if (source is not null && !CategorizationDecisionSources.IsAllowed(source))
        {
            return Results.BadRequest($"source must be one of: {AllowedSourceNames}.");
        }

        if (suggestedCategory is int suggestedCategoryId
            && (suggestedCategoryId == (int)CommodityCategory.Undefined
                || !Enum.IsDefined((CommodityCategory)suggestedCategoryId)))
        {
            return Results.BadRequest("suggestedCategory must be a defined category code other than 0.");
        }

        if (finalCategory is int finalCategoryId && !Enum.IsDefined((CommodityCategory)finalCategoryId))
        {
            // В отличие от suggestedCategory, код 0 («Не указана») для итога допустим (ADR-033 п. 7).
            return Results.BadRequest("finalCategory must be a defined category code.");
        }

        DateTimeOffset? fromTimestamp = null;
        if (from is not null)
        {
            if (!DateTimeOffset.TryParse(
                    from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedFrom))
            {
                return Results.BadRequest("from must be a valid ISO 8601 date-time.");
            }

            fromTimestamp = parsedFrom;
        }

        DateTimeOffset? toTimestamp = null;
        if (to is not null)
        {
            if (!DateTimeOffset.TryParse(
                    to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTo))
            {
                return Results.BadRequest("to must be a valid ISO 8601 date-time.");
            }

            toTimestamp = parsedTo;
        }

        if (limit < 1 || limit > MaxLimit)
        {
            return Results.BadRequest($"limit must be between 1 and {MaxLimit}.");
        }

        if (offset < 0)
        {
            return Results.BadRequest("offset cannot be negative.");
        }

        var query = new CategorizationDecisionQuery(
            onlyMismatch,
            source,
            suggestedCategory,
            finalCategory,
            search,
            fromTimestamp,
            toTimestamp,
            limit,
            offset);

        var (items, total) = await service.GetPageAsync(query, cancellationToken);

        // Total — число уникальных сочетаний под тем же фильтром (FR-7), согласовано с пагинацией.
        httpContext.Response.Headers["X-Total-Count"] = total.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(items);
    }
}
