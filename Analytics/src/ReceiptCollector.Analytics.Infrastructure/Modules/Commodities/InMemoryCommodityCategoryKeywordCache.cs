using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Singleton-кэш слов категоризации (ADR-035, решение E1, п. 6): неизменяемый снимок
/// публикуется через <see cref="Volatile.Write{T}(ref T)"/>/<see cref="Volatile.Read{T}(ref T)"/>,
/// читатели работают без блокировок и без БД (NFR-1); писатели сериализованы
/// <see cref="SemaphoreSlim"/> (1,1) — семафор участвует только на перечитывании.
/// Стиль singleton-кэша и прогрева — прецедент <see cref="InMemoryCommodityCategoryCache"/> /
/// <c>CommodityCategoryCacheInitializationHostedService</c> (ADR-019), но с инвалидацией
/// после мутации (в отличие от «заморозки» кэша категорий).
/// </summary>
/// <remarks>
/// «Грязный» флаг (<see cref="_dirty"/>, <c>volatile</c>) означает «снимок может быть устаревшим»:
/// выставлен изначально (до прогрева) и после сбоя перечитывания. Успешное перечитывание сначала
/// атомарно подменяет ссылку на снимок (<see cref="Volatile.Write{T}(ref T)"/> — release), затем
/// снимает флаг; читатель, увидевший флаг снятым (acquire), обязан увидеть и новый снимок.
/// Репозиторий — scoped (<c>ICommodityCategoryKeywordRepository</c> над scoped DbContext), поэтому
/// перечитывание создаёт собственную область через <see cref="IServiceScopeFactory"/> — singleton
/// не держит scoped-зависимость (паттерн hosted-сервисов проекта).
/// </remarks>
internal sealed class InMemoryCommodityCategoryKeywordCache : ICommodityCategoryKeywordCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InMemoryCommodityCategoryKeywordCache> _logger;

    /// <summary>Сериализует перечитывание (запись); чтения на горячем пути его не берут.</summary>
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    /// <summary>Ссылка на снимок: чтение — <see cref="Volatile.Read{T}(ref T)"/>, запись — только под <see cref="_reloadGate"/>.</summary>
    private CommodityCategoryKeywordSnapshot _snapshot = CommodityCategoryKeywordSnapshot.Empty;

    /// <summary>«Грязный»: снимок может быть устаревшим (до первого прогрева или после сбоя refresh).</summary>
    private volatile bool _dirty = true;

    public InMemoryCommodityCategoryKeywordCache(
        IServiceScopeFactory scopeFactory,
        ILogger<InMemoryCommodityCategoryKeywordCache> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CommodityCategoryKeywordSnapshot GetSnapshot()
    {
        // Горячий путь (NFR-1): «чисто» — только чтение ссылки, без БД и без блокировок.
        if (!_dirty)
        {
            return Volatile.Read(ref _snapshot);
        }

        // Деградированный путь: перечитывание под тем же семафором, что и RefreshAsync.
        _reloadGate.Wait();
        try
        {
            // Double-check: пока ждали семафор, перечитывание мог выполнить другой поток
            // (или параллельный RefreshAsync после мутации) — на БД второй раз не идём.
            if (!_dirty)
            {
                return Volatile.Read(ref _snapshot);
            }

            try
            {
                // Синхронное перечитывание допустимо только здесь: это не горячий путь,
                // а единичное восстановление после сбоя (ADR-035 п. 6).
                ReloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Сбой не бросается на горячий путь: флаг остаётся «грязным», отдаём старый снимок.
                _logger.LogWarning(
                    ex,
                    "Commodity category keyword cache reload on read failed; the previous snapshot stays in use.");
            }

            return Volatile.Read(ref _snapshot);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ReloadAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _reloadGate.Release();
            }
        }
        catch (Exception ex)
        {
            // Мутация к этому моменту уже зафиксирована (БД — источник правды), поэтому сбой
            // перечитывания не бросается вызывающему: мутация остаётся успешной, флаг «грязный»
            // — следующее чтение повторит перечитывание (NFR-4 восстанавливается сам).
            _dirty = true;
            _logger.LogWarning(
                ex,
                "Commodity category keyword cache refresh failed; the cache stays dirty and will reload on next read.");
        }
    }

    /// <summary>
    /// Одно полное чтение таблицы и атомарная подмена ссылки. Порядок обязателен: сначала
    /// <see cref="Volatile.Write{T}(ref T)"/> снимка, затем снятие «грязного» флага — читатель,
    /// увидевший чистый флаг, гарантированно видит новый снимок.
    /// </summary>
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICommodityCategoryKeywordRepository>();

        var keywords = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        Volatile.Write(ref _snapshot, new CommodityCategoryKeywordSnapshot(keywords.ToArray()));
        _dirty = false;
    }
}
