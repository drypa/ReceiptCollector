using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.Receipts;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receipts");
        group.WithTags("Receipts");

        group.MapGet("", GetAll);

        group.MapGet("/{id:guid}", GetById);

        group.MapGet("/by-merchant/{merchantId:guid}", GetByMerchant);

        // Добавляем эндпоинт для обновления имени магазина по идентификатору
        group.MapPut("/merchants/{merchantId:guid}/name", UpdateMerchantName)
            .RequireAuthorization(); // Требуем авторизацию

        return app;
    }

    private static async Task<IResult> GetAll(HttpContext httpContext, [FromServices] IReceiptReadService service, [FromQuery] int limit = 10,
        [FromQuery] int offset = 0, CancellationToken cancellationToken = default)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.BadRequest("user is not authenticated.");
        }

        if (limit <= 0)
        {
            return Results.BadRequest("limit must be greater than zero.");
        }

        if (offset < 0)
        {
            return Results.BadRequest("offset cannot be negative.");
        }

        var receipts = await service.GetRecentAsync(userId.Value, limit, offset, cancellationToken);
        var totalCount = await service.GetTotalCountAsync(userId.Value, cancellationToken);
        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(receipts);
    }

    private static async Task<IResult> GetById(Guid id, [FromServices] IReceiptReadService service,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.BadRequest("user is not authenticated.");
        }

        var receipt = await service.GetByIdAsync(userId.Value, id, cancellationToken);
        return receipt is null ? Results.NotFound() : Results.Ok(receipt);
    }

    private static async Task<IResult> GetByMerchant(HttpContext httpContext, Guid merchantId, [FromServices] IReceiptReadService service,
        [FromQuery] int limit = 10, [FromQuery] int offset = 0, CancellationToken cancellationToken = default)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.BadRequest("user is not authenticated.");
        }

        if (limit <= 0)
        {
            return Results.BadRequest("limit must be greater than zero.");
        }

        if (offset < 0)
        {
            return Results.BadRequest("offset cannot be negative.");
        }

        var receipts = await service.GetByMerchantIdAsync(userId.Value, merchantId, limit, offset, cancellationToken);
        var totalCount = await service.GetTotalCountByMerchantIdAsync(userId.Value, merchantId, cancellationToken);
        httpContext.Response.Headers["X-Total-Count"] = totalCount.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(receipts);
    }

    public static async Task<IResult> UpdateMerchantName(Guid merchantId, [FromBody] UpdateMerchantNameRequest request,
        [FromServices] IMerchantRepository merchantRepository,
        [FromServices] IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        // Проверяем, что пользователь авторизован
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.Unauthorized();
        }

        // Проверяем, что пользователь является администратором
        var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsAdmin)
        {
            return Results.Forbid(); 
        }

        // Получаем магазин по идентификатору
        var merchant = await merchantRepository.GetByIdAsync(merchantId, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound("Merchant not found.");
        }

        // Обновляем имя магазина
        merchant.UpdateName(request.Name);
        
        // Сохраняем обновленный магазин
        await merchantRepository.AddAsync(merchant, cancellationToken);

        return Results.Ok("Merchant name updated successfully.");
    }
}

// Класс для запроса обновления имени магазина
public sealed record UpdateMerchantNameRequest(string Name);