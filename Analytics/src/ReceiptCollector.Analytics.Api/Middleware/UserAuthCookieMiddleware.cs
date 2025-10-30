using System.Security.Claims;
using ReceiptCollector.Analytics.Api.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Middleware;

public sealed class UserAuthCookieMiddleware
{
    private readonly RequestDelegate _next;

    public UserAuthCookieMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated != true &&
            context.Request.Cookies.TryGetValue(UserAuthCookieDefaults.CookieName, out var cookieValue) &&
            Guid.TryParse(cookieValue, out var userId))
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString())
            };

            var identity = new ClaimsIdentity(claims, UserAuthCookieDefaults.AuthenticationScheme);
            context.User = new ClaimsPrincipal(identity);
            context.Items[UserAuthCookieDefaults.HttpContextUserIdKey] = userId;
            
            using (UserContext.SetUserId(userId))
            {
                await _next(context).ConfigureAwait(false);
            }
        }
        else
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
