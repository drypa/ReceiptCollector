using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.PrecategorizedCommodities;

/// <summary>
/// Админский API очереди предкатегоризации (ADR-027 п.7, FR-5).
/// Права и стиль ошибок — по эталону <c>MerchantEndpoints</c>: текстовые тела,
/// без ProblemDetails (ADR-027 п.7, FR-5.9).
/// </summary>
public static class PrecategorizedCommodityEndpoints
{
    private const int MaxLimit = 200;

    public static IEndpointRouteBuilder MapPrecategorizedCommodityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/precategorized-commodities");
        group.WithTags("PrecategorizedCommodities");

        group.MapGet("", GetAll);
        group.MapPut("/{id:guid}/category", UpdateCategory);
        group.MapPost("/{id:guid}/confirm", Confirm);
        group.MapDelete("/{id:guid}", Decline);

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

        var result = await service.ConfirmAsync(id, cancellationToken);

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
}

public sealed record UpdatePrecategorizedCategoryRequest(int CategoryId);

public sealed record UpdatePrecategorizedCategoryResponse(Guid Id, int CategoryId, string CategoryName);

public sealed record ConfirmPrecategorizedCommodityResponse(
    Guid Id,
    int CategoryId,
    string CategoryName,
    int UpdatedCommodities);
