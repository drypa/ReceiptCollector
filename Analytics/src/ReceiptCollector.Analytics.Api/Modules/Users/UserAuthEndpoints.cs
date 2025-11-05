using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Modules.Users;

public static class UserAuthEndpoints
{
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromMinutes(30);


    public static IEndpointRouteBuilder MapUserAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Endpoints.AuthGroup);
        group.WithTags("User Authentication");

        group.MapGet(Endpoints.AuthByLinkPath, ConsumeAuthLink);
        group.MapGet(Endpoints.AuthLinkRequestPath, RequestAuthLink)
            .Produces<UserAuthLinkResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static async Task<IResult> ConsumeAuthLink(
        [FromQuery] string? token,
        HttpContext httpContext,
        [FromServices] IUserAuthLinkService authLinkService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Results.BadRequest("Token is required.");
        }

        var validation = await authLinkService.ValidateAsync(token, cancellationToken);

        if (!validation.IsValid || validation.UserId is null)
        {
            return Results.BadRequest(validation.Error ?? "Invalid token.");
        }

        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = CookieLifetime
        };

        httpContext.Response.Cookies.Append(UserAuthCookie.CookieName, validation.UserId.Value.ToString(), options);

        return Results.NoContent();
    }

    private static async Task<IResult> RequestAuthLink(
        [FromQuery] int? telegramId,
        [FromServices] IUserAuthLinkService authLinkService,
        CancellationToken cancellationToken)
    {
        if (telegramId is null || telegramId <= 0)
        {
            return Results.BadRequest("telegramId is required.");
        }

        try
        {
            var link = await authLinkService.GenerateByTelegramIdAsync(telegramId.Value, cancellationToken);
            return Results.Ok(new UserAuthLinkResponse(link.Link, link.ExpiresAt));
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound();
        }
    }

    private sealed record UserAuthLinkResponse(string Link, DateTimeOffset ExpiresAt);
}
