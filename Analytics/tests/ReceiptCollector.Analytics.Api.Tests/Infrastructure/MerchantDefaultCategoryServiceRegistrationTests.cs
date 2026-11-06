using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Wiring-тест DI-регистрации сервиса дожима (T-06, прецедент
/// <see cref="MerchantDefaultCategoryProviderRegistrationTests"/>): потребитель контракта
/// (<c>IMerchantDefaultCategoryService</c>) появляется только в T-07, поэтому потерю
/// регистрации в <see cref="DependencyInjectionExtensions"/> ни один существующий тест
/// не поймал бы — эндпоинт упал бы только в production. Этот тест это ловит.
/// </summary>
public sealed class MerchantDefaultCategoryServiceRegistrationTests
{
    private static ServiceCollection CreateServices()
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
        return services;
    }

    [Fact]
    public void MerchantDefaultCategoryService_is_registered_as_scoped()
    {
        var services = CreateServices();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IMerchantDefaultCategoryService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(MerchantDefaultCategoryService), descriptor.ImplementationType);
    }

    [Fact]
    public void Container_resolves_merchant_default_category_service_in_scope()
    {
        // Реальный resolve: сервис требует IMerchantRepository и ICommodityRepository —
        // отсутствующая регистрация любого звена уронит GetRequiredService.
        var services = CreateServices();
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMerchantDefaultCategoryService>();
        Assert.NotNull(service);
    }
}
