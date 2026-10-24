using Microsoft.Extensions.Time.Testing;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты монитора HTTP-активности (шаг 4 плана, ADR-027 п.1, FR-1.3/1.4, NFR-1):
/// отсчёт простоя от старта, сброс по Touch, потокобезопасность.
/// </summary>
public class ApiActivityMonitorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsIdle_is_false_right_after_start_and_true_after_threshold()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var monitor = new ApiActivityMonitor(timeProvider);

        Assert.Equal(Start, monitor.LastActivityUtc);
        Assert.False(monitor.IsIdle(TimeSpan.FromMinutes(5)));

        timeProvider.Advance(TimeSpan.FromMinutes(4));
        Assert.False(monitor.IsIdle(TimeSpan.FromMinutes(5)));

        // FR-1.3: отсчёт простоя ведётся от старта сервиса.
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        Assert.True(monitor.IsIdle(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Touch_resets_idle_timer()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var monitor = new ApiActivityMonitor(timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.True(monitor.IsIdle(TimeSpan.FromMinutes(5)));

        monitor.Touch();
        Assert.Equal(timeProvider.GetUtcNow(), monitor.LastActivityUtc);
        Assert.False(monitor.IsIdle(TimeSpan.FromMinutes(5)));

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        Assert.True(monitor.IsIdle(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Concurrent_touch_does_not_break_invariant()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var monitor = new ApiActivityMonitor(timeProvider);

        timeProvider.Advance(TimeSpan.FromSeconds(1));

        var tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => monitor.Touch()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.True(monitor.LastActivityUtc >= Start);
        Assert.False(monitor.IsIdle(TimeSpan.FromMinutes(5)));
    }
}
