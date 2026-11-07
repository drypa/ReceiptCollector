using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Wiring-тест DI-регистрации репозитория журнала (T-02, ADR-033 п. 2). Потребитель контракта
/// появляется только в T-03, поэтому потерю регистрации иначе поймал бы лишь production.
/// </summary>
public sealed class CategorizationDecisionRepositoryRegistrationTests
{
    [Fact]
    public void Repository_is_registered_as_scoped_and_resolves()
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

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ICategorizationDecisionRepository));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(CategorizationDecisionRepository), descriptor.ImplementationType);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICategorizationDecisionRepository>());
    }
}
