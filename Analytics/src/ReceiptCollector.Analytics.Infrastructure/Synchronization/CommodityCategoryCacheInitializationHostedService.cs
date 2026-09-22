using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Synchronization;

/// <summary>
/// Наполняет in-memory кэш категорий при старте из commodities (FR-1.2, FR-1.7; ADR 019 п.4).
/// Fail-fast при недоступности БД (паттерн ReceiptSynchronizationHostedService).
/// </summary>
internal sealed class CommodityCategoryCacheInitializationHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICommodityCategoryCache _cache;
    private readonly IOptions<CommodityCategoryCacheOptions> _options;
    private readonly ILogger<CommodityCategoryCacheInitializationHostedService> _logger;

    public CommodityCategoryCacheInitializationHostedService(
        IServiceScopeFactory scopeFactory,
        ICommodityCategoryCache cache,
        IOptions<CommodityCategoryCacheOptions> options,
        ILogger<CommodityCategoryCacheInitializationHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var maxSize = _options.Value.MaxSize;
        if (maxSize < 1)
        {
            // MaxSize < 1 — кэш отключён (инвариант 7): не наполняем, ИИ вызывается всегда.
            _logger.LogInformation("Commodity category cache is disabled (MaxSize < 1).");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

            if (!await dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "Unable to connect to the analytics database. Ensure the database is created and accessible with the configured credentials.");
            }

            // FR-1.2/FR-1.7: позиции с заполненной категорией, отличной от Undefined (CategoryId != 0).
            // Проекция только нужных колонок (Name, CategoryId); OrderBy(Name) — детерминизм first-wins
            // при равных названиях.
            // ВАЖНО (ADR 019 п.4): query filter ReceiptDbContext настроен ТОЛЬКО на ReceiptEntity,
            // поэтому запрос через DbSet<CommodityEntity> охватывает позиции ВСЕХ пользователей —
            // это соответствует сквозному масштабу кэша (FR-2.4). Фильтр по userId НЕ добавляем.
            var commodities = await dbContext.Commodities
                .AsNoTracking()
                .Where(c => c.CategoryId != null && c.CategoryId != 0)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Name, c.CategoryId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var commodity in commodities)
            {
                // Ранний выход: Count == MaxSize — кэш «замёрз», дальнейшие TryAdd были бы no-op
                // (first-wins + OrderBy(Name) даёт тот же результат, что и полный перебор).
                if (_cache.Count >= maxSize)
                {
                    break;
                }

                var categoryId = commodity.CategoryId!.Value;
                if (!Enum.IsDefined(typeof(CommodityCategory), categoryId))
                {
                    continue; // невалидный CategoryId в данные попасть не должен, но защищаемся
                }

                _cache.TryAdd(
                    CommodityNameNormalizer.NormalizeName(commodity.Name),
                    (CommodityCategory)categoryId);
            }

            _logger.LogInformation(
                "Commodity category cache initialized: {Count} entries (MaxSize={MaxSize}).",
                _cache.Count, maxSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Commodity category cache initialization failed during application startup.");
            throw; // fail-fast: без БД сервис бесполезен (аналогично ReceiptSynchronizationHostedService)
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}