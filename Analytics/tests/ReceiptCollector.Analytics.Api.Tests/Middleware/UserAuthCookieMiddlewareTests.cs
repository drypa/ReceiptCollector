using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ReceiptCollector.Analytics.Api.Middleware;
using ReceiptCollector.Analytics.Api.Modules.Users;

namespace ReceiptCollector.Analytics.Api.Tests.Middleware;

public class UserAuthCookieMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_without_cookie_keeps_user_anonymous()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new UserAuthCookieMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        Assert.False(context.Items.ContainsKey(UserAuthCookieDefaults.HttpContextUserIdKey));
    }

    [Fact]
    public async Task InvokeAsync_with_invalid_cookie_value_keeps_user_anonymous()
    {
        var context = CreateContextWithCookie("not-a-guid");
        var middleware = new UserAuthCookieMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        Assert.False(context.Items.ContainsKey(UserAuthCookieDefaults.HttpContextUserIdKey));
    }

    [Fact]
    public async Task InvokeAsync_with_valid_cookie_authenticates_user()
    {
        var userId = Guid.NewGuid();
        var context = CreateContextWithCookie(userId.ToString());
        var middleware = new UserAuthCookieMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var identity = context.User.Identity;
        Assert.NotNull(identity);
        Assert.True(identity!.IsAuthenticated);
        Assert.Equal(UserAuthCookieDefaults.AuthenticationScheme, identity.AuthenticationType);

        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
        Assert.NotNull(userIdClaim);
        Assert.Equal(userId.ToString(), userIdClaim!.Value);

        Assert.True(context.Items.ContainsKey(UserAuthCookieDefaults.HttpContextUserIdKey));
        Assert.Equal(userId, context.Items[UserAuthCookieDefaults.HttpContextUserIdKey]);
    }

    private static DefaultHttpContext CreateContextWithCookie(string cookieValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Append("Cookie", $"{UserAuthCookieDefaults.CookieName}={cookieValue}");
        return context;
    }
}
