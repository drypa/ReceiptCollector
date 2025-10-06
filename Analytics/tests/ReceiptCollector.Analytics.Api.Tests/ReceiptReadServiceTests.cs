using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Configuration;

namespace ReceiptCollector.Analytics.Api.Tests;

public class ReceiptReadServiceTests
{
    [Fact]
    public async Task Stub_service_returns_empty_collection()
    {
        var provider = new ServiceCollection()
            .AddInfrastructure(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var service = provider.GetRequiredService<IReceiptReadService>();

        var summary = await service.GetRecentAsync(Guid.NewGuid(), 10, CancellationToken.None);

        Assert.Empty(summary);
    }
}