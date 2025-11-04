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

        if (!UserContext.HasUserId &&
            context.Request.Cookies.TryGetValue(UserAuthCookie.CookieName, out var cookieValue) &&
            Guid.TryParse(cookieValue, out var userId))
        {
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
