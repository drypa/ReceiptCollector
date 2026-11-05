using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.Commodities;

public static class CommodityEndpoints
{
    public static IEndpointRouteBuilder MapCommodityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/commodities");
        group.WithTags("Commodities");

        group.MapGet("", GetAll);
        group.MapPut("/{id:guid}/category", UpdateCategory);
        group.MapGet("/categories", ListCategories);

        return app;
    }

    public static async Task<IResult> GetAll(
        HttpContext httpContext,
        [FromServices] ICommodityReadService service,
        [FromQuery] int limit = 10,
        [FromQuery] int offset = 0,
        [FromQuery] string? categoryFilter = null,
        CancellationToken cancellationToken = default)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.BadRequest("User is not authenticated.");
        }

        if (limit <= 0)
        {
            return Results.BadRequest("limit must be greater than zero.");
        }

        if (offset < 0)
        {
            return Results.BadRequest("offset cannot be negative.");
        }

        if (!TryParseCategoryFilter(categoryFilter, out var filter))
        {
            return Results.BadRequest(
                $"Invalid categoryFilter '{categoryFilter}'. Allowed values: any, uncategorized, undefined.");
        }

        var commodities = await service.GetAsync(userId.Value, limit, offset, filter, cancellationToken);
        var totalCount = await service.GetTotalCountAsync(userId.Value, filter, cancellationToken);

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(commodities);
    }

    public static async Task<IResult> UpdateCategory(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        [FromServices] ICommodityRepository commodityRepository,
        [FromServices] IUserRepository userRepository,
        [FromServices] ICommodityCategoryCache cache,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
        {
            return Results.Forbid();
        }

        var commodity = await commodityRepository.GetByIdAsync(id, cancellationToken);
        if (commodity is null)
        {
            return Results.NotFound("Commodity not found.");
        }

        if (request.CategoryId is { } rawCategory && !Enum.IsDefined(typeof(CommodityCategory), rawCategory))
        {
            return Results.BadRequest("Invalid category.");
        }

        // null — сброс категории (фронт отправляет {"categoryId": null}): пишем category_id = NULL (ADR-009 п. 8).
        var category = request.CategoryId is { } definedCategory
            ? (CommodityCategory)definedCategory
            : (CommodityCategory?)null;
        await commodityRepository.UpdateCategoryAsync(id, category, cancellationToken);

        // Ручное назначение категории обновляет сквозной кэш (FR-1.3, ADR 019 п.6 — причина 1).
        // Сброс (null) и Undefined в кэш не пишутся (инвариант 8); TryAdd синхронный, await не нужен.
        if (category is not null && category != CommodityCategory.Undefined)
        {
            cache.TryAdd(CommodityNameNormalizer.NormalizeName(commodity.Name), category.Value);
        }

        return Results.Ok(new
        {
            categoryId = request.CategoryId,
            categoryName = category is null ? null : CommodityCategoryHelper.GetDisplayName(category.Value),
        });
    }

    public static IResult ListCategories()
    {
        var categories = CommodityCategoryHelper.GetAll()
            .Select(c => new CategoryDto((int)c.Id, c.Id.ToString(), c.Name, CommodityCategoryHelper.GetGroup(c.Id)))
            .ToList();

        return Results.Ok(categories);
    }

    private static bool TryParseCategoryFilter(string? value, out CommodityCategoryFilter filter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            filter = CommodityCategoryFilter.Any;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out filter)
               && Enum.IsDefined(filter);
    }
}

/// <summary>
/// <c>CategoryId = null</c> — сброс категории (category_id = NULL); Undefined (0) — легаси-сброс в «Не указана».
/// </summary>
public sealed record UpdateCategoryRequest(int? CategoryId);