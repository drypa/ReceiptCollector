using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

public sealed class ReceiptReadServiceTests
{
    [Fact]
    public void Service_resolves_with_infrastructure_registration()
    {
        var provider = new ServiceCollection()
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{PostgresOptions.SectionName}:ConnectionString"] = "Host=localhost;Database=test;Username=test;Password=test"
                }).Build())
            .BuildServiceProvider();

        var service = provider.GetRequiredService<IReceiptReadService>();

        Assert.NotNull(service);
    }
}
