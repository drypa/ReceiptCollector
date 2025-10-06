using Microsoft.AspNetCore.Mvc;
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
        var userId = Guid.Empty;
        var receipts = await service.GetRecentAsync(userId, 10, cancellationToken);
        return Results.Ok(receipts);
    }

    private static async Task<IResult> GetById(Guid id, [FromServices] IReceiptReadService service,
        CancellationToken cancellationToken)
    {
        var receipt = await service.GetByIdAsync(id, cancellationToken);
        return receipt is null ? Results.NotFound() : Results.Ok(receipt);
    }
}