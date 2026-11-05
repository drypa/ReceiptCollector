using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Wiring-тест DI-регистрации batch-провайдера дефолтов магазинов (T-03, замечание code-review):
/// конструктор <c>CommodityCategoryResolver</c> принимает <see cref="IMerchantDefaultCategoryProvider"/>
/// опционально (совместимость существующих фабрик тестов), поэтому потеря регистрации в
/// <see cref="DependencyInjectionExtensions"/> не сломала бы ни один unit-тест — шаг
/// <c>merchant-default</c> «молча» выключился бы в production. Этот тест это ловит.
/// </summary>
public sealed class MerchantDefaultCategoryProviderRegistrationTests
{
    private static (ServiceCollection Services, IConfiguration Configuration) CreateServices()
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
        return (services, configuration);
    }

    [Fact]
    public void MerchantDefaultCategoryProvider_is_registered_as_scoped()
    {
        var (services, _) = CreateServices();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IMerchantDefaultCategoryProvider));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(MerchantDefaultCategoryProvider), descriptor.ImplementationType);
    }

    [Fact]
    public void Container_resolves_resolver_and_merchant_default_provider_in_scope()
    {
        // Реальный resolve фиктивного ничего не подменяет: ICommodityCategoryResolver требует
        // кэш/AI/очередь/DbContext, провайдер — ReceiptDbContext; отсутствующая регистрация
        // уронит GetRequiredService и тест упадёт.
        var (services, _) = CreateServices();
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ICommodityCategoryResolver>();
        Assert.NotNull(resolver);

        var merchantDefaults = scope.ServiceProvider.GetRequiredService<IMerchantDefaultCategoryProvider>();
        Assert.NotNull(merchantDefaults);
    }
}
