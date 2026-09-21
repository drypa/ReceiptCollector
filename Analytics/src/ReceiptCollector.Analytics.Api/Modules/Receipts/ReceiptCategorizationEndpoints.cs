using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Modules.Receipts;

/// <summary>
/// Эндпоинты сценария «Автоматическая категоризация товаров чека» (ADR 009).
/// POST /api/receipts/{id}/categories/suggest — предложить категории (ничего не сохраняет).
/// PUT /api/receipts/{id}/categories — подтвердить и сохранить категории товаров чека.
/// </summary>
public static class ReceiptCategorizationEndpoints
{
    public static IEndpointRouteBuilder MapReceiptCategorizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receipts");
        group.WithTags("Receipts");

        group.MapPost("/{receiptId:guid}/categories/suggest", Suggest);
        group.MapPut("/{receiptId:guid}/categories", Save);

        return app;
    }

    public static async Task<IResult> Suggest(
        Guid receiptId,
        [FromServices] ICommodityCategorizationService service,
        [FromServices] IOptions<AiOptions> aiOptions,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(aiOptions.Value.BaseUrl))
        {
            return Results.Json(
                new { error = "AI categorization is not configured (AI:BaseUrl is empty)." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var result = await service.SuggestForReceiptAsync(userId.Value, receiptId, cancellationToken);
            return Results.Ok(result);
        }
        catch (ReceiptNotFoundException)
        {
            return Results.NotFound("Receipt not found.");
        }
    }

    public static async Task<IResult> Save(
        Guid receiptId,
        [FromBody] SaveCategoriesRequest request,
        [FromServices] ICommodityCategorizationService service,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("items must not be empty.");
        }

        var assignments = new List<ConfirmedCategoryAssignment>(request.Items.Count);

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];

            if (item.CommodityId == Guid.Empty)
            {
                return Results.BadRequest($"items[{index}].commodityId is not a valid GUID.");
            }

            if (string.IsNullOrWhiteSpace(item.Category))
            {
                // Пустая строка/пробелы трактуются как сброс категории (null).
                assignments.Add(new ConfirmedCategoryAssignment(item.CommodityId, null));
                continue;
            }

            if (int.TryParse(item.Category, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return Results.BadRequest($"items[{index}].category must be a category name, not a number.");
            }

            if (!Enum.TryParse<CommodityCategory>(item.Category, ignoreCase: true, out var category)
                || !Enum.IsDefined(category)
                || category == CommodityCategory.Undefined)
            {
                return Results.BadRequest($"items[{index}].category '{item.Category}' is not a valid category name.");
            }

            assignments.Add(new ConfirmedCategoryAssignment(item.CommodityId, category));
        }

        try
        {
            var updated = await service.ApplyConfirmedCategoriesAsync(
                userId.Value, receiptId, assignments, cancellationToken);
            return Results.Ok(new SaveCategoriesResponse(receiptId, updated));
        }
        catch (ReceiptNotFoundException)
        {
            return Results.NotFound("Receipt not found.");
        }
    }
}

public sealed record SaveCategoriesRequest(IReadOnlyList<SaveCategoryItemRequest>? Items);

public sealed record SaveCategoryItemRequest(Guid CommodityId, string? Category);

public sealed record SaveCategoriesResponse(Guid ReceiptId, int Updated);