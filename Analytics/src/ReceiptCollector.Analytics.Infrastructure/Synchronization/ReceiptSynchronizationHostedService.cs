using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class ReceiptSynchronizationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptSynchronizationHostedService> _logger;

    public ReceiptSynchronizationHostedService(IServiceScopeFactory scopeFactory, ILogger<ReceiptSynchronizationHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var synchronizationService = scope.ServiceProvider.GetRequiredService<ReceiptSynchronizationService>();
            await synchronizationService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receipt synchronization failed during application startup.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
