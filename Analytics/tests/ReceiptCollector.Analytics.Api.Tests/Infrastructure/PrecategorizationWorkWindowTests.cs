using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты арифметики рабочего окна предкатегоризации (ADR-030): границы, окно через полночь,
/// незаданное и нулевое окно.
/// </summary>
public sealed class PrecategorizationWorkWindowTests
{
    [Fact]
    public void Not_configured_window_is_always_open()
    {
        var options = new PrecategorizationOptions();

        Assert.False(PrecategorizationWorkWindow.IsConfigured(options));
        Assert.True(PrecategorizationWorkWindow.IsOpen(options, At(0)));
        Assert.True(PrecategorizationWorkWindow.IsOpen(options, At(23, 59)));
    }

    [Theory]
    [InlineData(2, 6, 1, 59, false)]
    [InlineData(2, 6, 2, 0, true)]
    [InlineData(2, 6, 5, 59, true)]
    [InlineData(2, 6, 6, 0, false)]
    [InlineData(2, 6, 12, 0, false)]
    public void Day_window_includes_start_and_excludes_end(
        int startHour,
        int endHour,
        int hour,
        int minute,
        bool expected)
    {
        Assert.Equal(expected, PrecategorizationWorkWindow.IsOpen(Window(startHour, endHour), At(hour, minute)));
    }

    [Theory]
    [InlineData(23, 6, 22, 59, false)]
    [InlineData(23, 6, 23, 0, true)]
    [InlineData(23, 6, 0, 0, true)]
    [InlineData(23, 6, 5, 59, true)]
    [InlineData(23, 6, 6, 0, false)]
    [InlineData(23, 6, 12, 0, false)]
    public void Overnight_window_crosses_midnight(
        int startHour,
        int endHour,
        int hour,
        int minute,
        bool expected)
    {
        Assert.Equal(expected, PrecategorizationWorkWindow.IsOpen(Window(startHour, endHour), At(hour, minute)));
    }

    [Fact]
    public void Zero_length_window_is_closed()
    {
        // Валидация не пропускает такие значения на старте; защита нужна для прямого конструирования.
        Assert.False(PrecategorizationWorkWindow.IsOpen(Window(2, 2), At(2)));
    }

    [Fact]
    public void Describe_names_zone_or_server_local_time()
    {
        Assert.Equal(
            "not configured (around the clock)",
            PrecategorizationWorkWindow.Describe(new PrecategorizationOptions()));

        var withZone = Window(2, 6);
        withZone.WorkWindowTimeZone = "Europe/Moscow";
        Assert.Equal("02:00:00–06:00:00 (Europe/Moscow)", PrecategorizationWorkWindow.Describe(withZone));

        var local = Window(23, 6);
        Assert.Equal("23:00:00–06:00:00 (server local time)", PrecategorizationWorkWindow.Describe(local));
    }

    private static PrecategorizationOptions Window(int startHour, int endHour) => new()
    {
        WorkWindowStart = TimeSpan.FromHours(startHour),
        WorkWindowEnd = TimeSpan.FromHours(endHour)
    };

    private static DateTimeOffset At(int hour, int minute = 0)
        => new(2026, 10, 24, hour, minute, 0, TimeSpan.Zero);
}
