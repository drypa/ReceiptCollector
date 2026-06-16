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

    private static async Task<IResult> GetAll(
        HttpContext httpContext,
        [FromServices] ICommodityReadService service,
        [FromQuery] int limit = 10,
        [FromQuery] int offset = 0,
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

        var commodities = await service.GetAsync(userId.Value, limit, offset, cancellationToken);
        var totalCount = await service.GetTotalCountAsync(userId.Value, cancellationToken);

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(commodities);
    }

    private static async Task<IResult> UpdateCategory(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        [FromServices] ICommodityRepository commodityRepository,
        [FromServices] IUserRepository userRepository,
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

        if (!Enum.IsDefined(typeof(CommodityCategory), request.CategoryId))
        {
            return Results.BadRequest("Invalid category.");
        }

        var category = (CommodityCategory)request.CategoryId;
        await commodityRepository.UpdateCategoryAsync(id, category, cancellationToken);

        return Results.Ok(new { categoryId = request.CategoryId, categoryName = CommodityCategoryHelper.GetDisplayName(category) });
    }

    private static IResult ListCategories()
    {
        var categories = CommodityCategoryHelper.GetAll()
            .Select(c => new CategoryDto((int)c.Id, c.Name))
            .ToList();

        return Results.Ok(categories);
    }
}

public sealed record UpdateCategoryRequest(int CategoryId);
