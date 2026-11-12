using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Wiring-тест DI-регистрации админского сервиса слов категоризации (T-04): scoped-сервис
/// поверх scoped-репозитория и singleton-кэша. Потерю регистрации иначе заметил бы только
/// production (прецедент <c>CommodityCategoryKeywordCacheRegistrationTests</c>).
/// </summary>
public sealed class CommodityCategoryKeywordServiceRegistrationTests
{
    [Fact]
    public void Keyword_service_is_registered_scoped_and_resolves()
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

        var registration = Assert.Single(
            services, descriptor => descriptor.ServiceType == typeof(ICommodityCategoryKeywordService));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(CommodityCategoryKeywordService), registration.ImplementationType);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICommodityCategoryKeywordService>());
    }
}
