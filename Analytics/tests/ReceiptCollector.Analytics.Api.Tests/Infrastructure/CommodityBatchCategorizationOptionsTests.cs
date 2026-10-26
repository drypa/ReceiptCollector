using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты опции бюджета одного suggest-запроса страницы (T-02, ADR-028 п. 3):
/// дефолт 25 с, биндинг из конфигурации/env, невалидное значение падает на старте.
/// </summary>
public class CommodityBatchCategorizationOptionsTests
{
    [Fact]
    public void Default_suggest_timeout_is_25_seconds()
    {
        var options = new CommodityBatchCategorizationOptions();

        Assert.Equal(TimeSpan.FromSeconds(25), options.SuggestTimeout);
    }

    [Fact]
    public void Environment_style_key_binds_section_value()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            // env: CommodityBatchCategorization__SuggestTimeout
            ["CommodityBatchCategorization:SuggestTimeout"] = "00:00:07"
        });

        var options = provider.GetRequiredService<IOptions<CommodityBatchCategorizationOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(7), options.SuggestTimeout);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void Invalid_suggest_timeout_fails_on_start(string value)
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["CommodityBatchCategorization:SuggestTimeout"] = value
        });

        // NFR-7: явная ошибка старта, а не «тихая деградация» к таймауту прокси.
        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<CommodityBatchCategorizationOptions>>().Value);
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
