using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using ReceiptCollector.Analytics.Api.Middleware;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Middleware;

/// <summary>
/// Тесты middleware активности (шаг 4 плана, ADR-027 п.1, D3/FR-1.1–1.2):
/// любой запрос, кроме /health, сбрасывает таймер простоя.
/// </summary>
public class RequestActivityMonitorMiddlewareTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public async Task Health_paths_do_not_reset_idle_timer(string path)
    {
        var timeProvider = new FakeTimeProvider(Start);
        var monitor = new ApiActivityMonitor(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.True(monitor.IsIdle(TimeSpan.FromMinutes(5)));

        var nextCalled = false;
        var middleware = new RequestActivityMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            monitor);

        await middleware.InvokeAsync(CreateContext(path));

        Assert.True(nextCalled);
        Assert.True(monitor.IsIdle(TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData("/api/receipts")]
    [InlineData("/assets/index.js")]
    [InlineData("/")]
    [InlineData("/precategorized")]
    public async Task Application_paths_reset_idle_timer(string path)
    {
        var timeProvider = new FakeTimeProvider(Start);
        var monitor = new ApiActivityMonitor(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.True(monitor.IsIdle(TimeSpan.FromMinutes(5)));

        var nextCalled = false;
        var middleware = new RequestActivityMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            monitor);

        await middleware.InvokeAsync(CreateContext(path));

        Assert.True(nextCalled);
        Assert.False(monitor.IsIdle(TimeSpan.FromMinutes(5)));
        Assert.Equal(timeProvider.GetUtcNow(), monitor.LastActivityUtc);
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }
}
