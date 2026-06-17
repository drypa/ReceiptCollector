using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

internal sealed class ReceiptSynchronizationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptSynchronizationHostedService> _logger;
    private readonly IOptions<ReceiptSynchronizationOptions> _options;

    public ReceiptSynchronizationHostedService(IServiceScopeFactory scopeFactory, ILogger<ReceiptSynchronizationHostedService> logger, IOptions<ReceiptSynchronizationOptions> options)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.Value.Skip)
        {
            _logger.LogInformation("Receipt synchronization skipped due to Skip flag.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

            if (!await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Unable to connect to the analytics database. Ensure the database is created and accessible with the configured credentials.");
            }

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
