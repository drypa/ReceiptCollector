using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты опций фоновой предкатегоризации (шаг 3 плана, ADR-027 п.9, NFR-7/NFR-9):
/// дефолты совпадают с ADR, невалидные значения падают на старте, время внедряется через DI.
/// </summary>
public class PrecategorizationOptionsTests
{
    [Fact]
    public void Defaults_match_adr_027()
    {
        var options = new PrecategorizationOptions();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(5), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.IdleCheckInterval);
        Assert.Equal(5, options.BatchLimit);
        Assert.Equal(TimeSpan.FromSeconds(2), options.IntervalBetweenCalls);
        // ADR-030: окно по умолчанию не задано (фон работает круглосуточно, как до ADR-030).
        Assert.Null(options.WorkWindowStart);
        Assert.Null(options.WorkWindowEnd);
        Assert.Null(options.WorkWindowTimeZone);
    }

    [Fact]
    public void Valid_configuration_binds_section_values()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Precategorization:Enabled"] = "false",
            ["Precategorization:IdleTimeout"] = "00:00:10",
            ["Precategorization:IdleCheckInterval"] = "00:00:05",
            ["Precategorization:BatchLimit"] = "7",
            ["Precategorization:IntervalBetweenCalls"] = "00:00:00",
            ["Precategorization:WorkWindowStart"] = "02:00:00",
            ["Precategorization:WorkWindowEnd"] = "06:00:00",
            ["Precategorization:WorkWindowTimeZone"] = "Europe/Moscow"
        });

        var options = provider.GetRequiredService<IOptions<PrecategorizationOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(10), options.IdleTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.IdleCheckInterval);
        Assert.Equal(7, options.BatchLimit);
        Assert.Equal(TimeSpan.Zero, options.IntervalBetweenCalls);
        Assert.Equal(TimeSpan.FromHours(2), options.WorkWindowStart);
        Assert.Equal(TimeSpan.FromHours(6), options.WorkWindowEnd);
        Assert.Equal("Europe/Moscow", options.WorkWindowTimeZone);
    }

    [Theory]
    [InlineData("Precategorization:BatchLimit", "0")]
    [InlineData("Precategorization:BatchLimit", "-1")]
    [InlineData("Precategorization:IdleTimeout", "00:00:00")]
    [InlineData("Precategorization:IdleTimeout", "-00:00:01")]
    [InlineData("Precategorization:IdleCheckInterval", "00:00:00")]
    [InlineData("Precategorization:IntervalBetweenCalls", "-00:00:01")]
    // ADR-030: только одна граница окна, граница вне суток или невалидный часовой пояс.
    [InlineData("Precategorization:WorkWindowStart", "02:00:00")]
    [InlineData("Precategorization:WorkWindowEnd", "06:00:00")]
    [InlineData("Precategorization:WorkWindowStart", "24:00:00")]
    [InlineData("Precategorization:WorkWindowEnd", "1.00:00:00")]
    [InlineData("Precategorization:WorkWindowStart", "-00:00:01")]
    [InlineData("Precategorization:WorkWindowTimeZone", "Not/AZone")]
    public void Invalid_value_fails_on_start(string key, string value)
    {
        var provider = BuildProvider(new Dictionary<string, string?> { [key] = value });

        // NFR-7: явная ошибка, а не «тихая деградация».
        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<PrecategorizationOptions>>().Value);
    }

    [Fact]
    public void No_work_window_is_valid_and_means_around_the_clock()
    {
        // Окно не задано — фон работает круглосуточно (поведение до ADR-030); валидация проходит.
        var provider = BuildProvider(new Dictionary<string, string?>());

        var options = provider.GetRequiredService<IOptions<PrecategorizationOptions>>().Value;

        Assert.Null(options.WorkWindowStart);
        Assert.Null(options.WorkWindowEnd);
    }

    [Fact]
    public void Equal_work_window_bounds_fail_on_start()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Precategorization:WorkWindowStart"] = "02:00:00",
            ["Precategorization:WorkWindowEnd"] = "02:00:00"
        });

        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<PrecategorizationOptions>>().Value);
    }

    [Fact]
    public void TimeProvider_can_be_replaced_with_fake_and_drives_time()
    {
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        services.AddSingleton<TimeProvider>(fake);

        using var provider = services.BuildServiceProvider();

        var timeProvider = provider.GetRequiredService<TimeProvider>();
        Assert.Same(fake, timeProvider);

        fake.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 12, 1, 0, TimeSpan.Zero), timeProvider.GetUtcNow());
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        return services.BuildServiceProvider();
    }
}
