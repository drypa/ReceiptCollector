using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using ReceiptCollector.Analytics.Infrastructure.Synchronization;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Wiring-тест DI-регистрации хранения слов категоризации (T-03, критерии готовности:
/// кэш — singleton, репозиторий — scoped, hosted-прогрев зарегистрирован ровно один раз).
/// Потребители (сервис T-04, провайдер T-05) появляются позже — потерю регистрации иначе
/// заметил бы только production (прецедент <c>CategorizationDecisionRepositoryRegistrationTests</c>).
/// </summary>
public sealed class CommodityCategoryKeywordCacheRegistrationTests
{
    [Fact]
    public void Keyword_storage_is_registered_with_expected_lifetimes_and_single_warmup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Подключение не открывается — важно лишь, что DbContext конфигурируется.
                ["Infrastructure:Postgres:ConnectionString"] = "Host=localhost;Database=wiring;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        var cache = Assert.Single(services, d => d.ServiceType == typeof(ICommodityCategoryKeywordCache));
        Assert.Equal(ServiceLifetime.Singleton, cache.Lifetime);
        Assert.Equal(typeof(InMemoryCommodityCategoryKeywordCache), cache.ImplementationType);

        var repository = Assert.Single(services, d => d.ServiceType == typeof(ICommodityCategoryKeywordRepository));
        Assert.Equal(ServiceLifetime.Scoped, repository.Lifetime);
        Assert.Equal(typeof(CommodityCategoryKeywordRepository), repository.ImplementationType);

        // Прогрев — ровно один AddHostedService для этого сервиса.
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(CommodityCategoryKeywordCacheInitializationHostedService));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICommodityCategoryKeywordCache>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICommodityCategoryKeywordRepository>());
    }
}
