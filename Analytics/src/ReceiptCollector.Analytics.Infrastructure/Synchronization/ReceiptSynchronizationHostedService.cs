using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

/// <summary>
/// Периодическая синхронизация чеков MongoDB → PostgreSQL (D6):
/// BackgroundService, цикл — синхронизация сразу при старте + каждые IntervalSeconds.
/// Ошибки (Mongo/Postgres/сеть) логируются и не валят процесс; стартовая проверка
/// доступности PG — в теле цикла (API поднимается даже при недоступной БД и ретраит).
/// </summary>
internal sealed class ReceiptSynchronizationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptSynchronizationHostedService> _logger;
    private readonly IOptions<ReceiptSynchronizationOptions> _options;

    /// <summary>Защита от наложения циклов (D6): non-blocking Wait(0).</summary>
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public ReceiptSynchronizationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReceiptSynchronizationHostedService> logger,
        IOptions<ReceiptSynchronizationOptions> options)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Value.Skip)
        {
            _logger.LogInformation("Receipt synchronization skipped due to Skip flag.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(_options.Value.IntervalSeconds, 1));

        _logger.LogInformation("Receipt synchronization started. Interval: {IntervalSeconds} seconds.",
            _options.Value.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SynchronizeOnceAsync(stoppingToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown (D6): отмена токена — выходим из цикла.
                break;
            }
        }
    }

    private async Task SynchronizeOnceAsync(CancellationToken cancellationToken)
    {
        // D6: non-blocking — если предыдущий цикл ещё выполняется, текущий пропускаем.
        if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Receipt synchronization is already in progress, skipping this tick.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

            if (!await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning(
                    "Unable to connect to the analytics database. Retrying on the next interval.");
                return;
            }

            var synchronizationService = scope.ServiceProvider.GetRequiredService<ReceiptSynchronizationService>();
            await synchronizationService.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receipt synchronization failed. Retrying on the next interval.");
        }
        finally
        {
            _syncLock.Release();
        }
    }
}