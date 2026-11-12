using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

/// <summary>
/// Прогрев singleton-кэша слов категоризации при старте (ADR-035 п. 6, решение E1): один
/// <c>RefreshAsync</c> наполняет снимок, чтобы первое чтение подстрочного матчинга не платило
/// за перечитывание. Стиль — прецедент <c>CommodityCategoryCacheInitializationHostedService</c>
/// (ADR-019): разрешение зависимостей через <see cref="IServiceScopeFactory"/>.
/// </summary>
/// <remarks>
/// В отличие от кэша категорий (fail-fast при недоступной БД) здесь ошибка прогрева процесс
/// НЕ валит: кэш остаётся «грязным» и перечитается лениво на первом чтении (ADR-035 п. 6).
/// Категоризация при этом деградирует только в «подсказки пусты до первого чтения» — не в падение.
/// </remarks>
internal sealed class CommodityCategoryKeywordCacheInitializationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommodityCategoryKeywordCacheInitializationHostedService> _logger;

    public CommodityCategoryKeywordCacheInitializationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CommodityCategoryKeywordCacheInitializationHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<ICommodityCategoryKeywordCache>();

            await cache.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Исключение не пробрасывается: ошибка прогрева не валит процесс, кэш остаётся
            // «грязным» и перечитается на первом чтении (ADR-035 п. 6).
            _logger.LogError(
                ex,
                "Commodity category keyword cache warm-up failed during application startup; the cache stays dirty and will reload on first read.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
