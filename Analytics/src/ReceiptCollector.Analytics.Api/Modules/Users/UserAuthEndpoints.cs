using Microsoft.AspNetCore.Mvc;
using ReceiptCollector.Analytics.Application.Modules.Users.Contracts;

namespace ReceiptCollector.Analytics.Api.Modules.Users;

public static class UserAuthEndpoints
{
    private const string AuthCookieName = "rc-auth";
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromMinutes(30);

    public static IEndpointRouteBuilder MapUserAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");
        group.WithTags("User Authentication");

        group.MapGet("/auth", ConsumeAuthLink);

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

        httpContext.Response.Cookies.Append(AuthCookieName, validation.UserId.Value.ToString(), options);

        return Results.NoContent();
    }
}
