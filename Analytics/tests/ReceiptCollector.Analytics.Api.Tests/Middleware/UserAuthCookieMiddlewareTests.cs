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
        Assert.False(context.Items.ContainsKey(UserAuthCookie.HttpContextUserIdKey));
    }

    [Fact]
    public async Task InvokeAsync_with_invalid_cookie_value_keeps_user_anonymous()
    {
        var context = CreateContextWithCookie("not-a-guid");
        var middleware = new UserAuthCookieMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.False(context.User.Identity?.IsAuthenticated ?? false);
        Assert.False(context.Items.ContainsKey(UserAuthCookie.HttpContextUserIdKey));
    }

    [Fact]
    public async Task InvokeAsync_with_valid_cookie_authenticates_user()
    {
        var userId = Guid.NewGuid();
        var context = CreateContextWithCookie(userId.ToString());
        var middleware = new UserAuthCookieMiddleware(_ =>
        {
            Assert.True(UserContext.HasUserId);

            Assert.Equal(UserContext.UserId, userId);

            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
    }

    private static DefaultHttpContext CreateContextWithCookie(string cookieValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Append("Cookie", $"{UserAuthCookie.CookieName}={cookieValue}");
        return context;
    }
}
