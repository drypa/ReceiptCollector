using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Modules.CategorizationDecisions;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Wiring-тест DI-регистрации recorder'а журнала (T-03, ADR-033 п. 2/6). Save-пути T-04…T-06
/// получают его из контейнера; потеря регистрации иначе обнаружилась бы только в production.
/// </summary>
public sealed class CategorizationDecisionRecorderRegistrationTests
{
    [Fact]
    public void Recorder_is_registered_as_scoped_and_resolves()
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

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ICategorizationDecisionRecorder));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(CategorizationDecisionRecorder), descriptor.ImplementationType);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICategorizationDecisionRecorder>());
    }
}
