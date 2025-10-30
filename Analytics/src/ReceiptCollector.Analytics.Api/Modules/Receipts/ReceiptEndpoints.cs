using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Api.Modules.Users;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;

namespace ReceiptCollector.Analytics.Api.Modules.Receipts;

public static class ReceiptEndpoints
{
    public static IEndpointRouteBuilder MapReceiptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receipts");
        group.WithTags("Receipts");

        group.MapGet("", GetAll);

        group.MapGet("/{id:guid}", GetById);

        return app;
    }

    private static async Task<IResult> GetAll([FromServices] IReceiptReadService service,
        CancellationToken cancellationToken)
    {
        var userId = UserContext.UserId;
        if (userId is null || userId == Guid.Empty)
        {
            return Results.BadRequest("user is not authenticated.");
        }

        var receipts = await service.GetRecentAsync(userId.Value, 10, cancellationToken);
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
}