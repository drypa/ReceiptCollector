using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Models;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.Merchants;

public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/merchants");
        group.WithTags("Merchants");

        group.MapGet("", GetAll);
        group.MapPut("/{merchantId:guid}/category", UpdateCategory);
        group.MapGet("/categories", ListCategories);

        return app;
    }

    public static async Task<IResult> GetAll(
        HttpContext httpContext,
        [FromServices] IMerchantRepository merchantRepository,
        [FromServices] IUserRepository userRepository,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
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

        if (limit <= 0)
        {
            return Results.BadRequest("limit must be greater than zero.");
        }

        if (offset < 0)
        {
            return Results.BadRequest("offset cannot be negative.");
        }

        var merchants = await merchantRepository.GetAllAsync(cancellationToken);

        IEnumerable<Merchant> filtered = merchants;
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var totalCount = filtered.Count();
        var page = filtered
            .OrderBy(m => m.Name)
            .Skip(offset)
            .Take(limit)
            .Select(m => new MerchantDto(m.Id, m.Name, (int)m.Category, m.Address, m.Inn))
            .ToList();

        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(page);
    }

    public static async Task<IResult> UpdateCategory(
        Guid merchantId,
        [FromBody] UpdateMerchantCategoryRequest request,
        [FromServices] IMerchantRepository merchantRepository,
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

        var merchant = await merchantRepository.GetByIdAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound("Merchant not found.");
        }

        if (!Enum.IsDefined(typeof(MerchantCategory), request.CategoryId))
        {
            return Results.BadRequest("Invalid category.");
        }

        var category = (MerchantCategory)request.CategoryId;
        await merchantRepository.UpdateCategoryAsync(merchantId, category, cancellationToken);

        return Results.Ok(new { categoryId = request.CategoryId, categoryName = MerchantCategoryHelper.GetDisplayName(category) });
    }

    public static async Task<IResult> ListCategories(
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

        var categories = MerchantCategoryHelper.GetAll()
            .Select(c => new MerchantCategoryDto((int)c.Id, c.Name))
            .ToList();

        return Results.Ok(categories);
    }
}

public sealed record UpdateMerchantCategoryRequest(int CategoryId);
