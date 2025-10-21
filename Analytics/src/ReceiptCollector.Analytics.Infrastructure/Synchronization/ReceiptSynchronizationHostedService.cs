using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

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
            var dbContext = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);

            if (pendingMigrations.Any())
            {
                await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var databaseCreator = dbContext.Database.GetService<IDatabaseCreator>() as RelationalDatabaseCreator;

                if (databaseCreator is not null)
                {
                    if (!await databaseCreator.ExistsAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await databaseCreator.CreateAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (!await databaseCreator.HasTablesAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await databaseCreator.CreateTablesAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
                }
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
