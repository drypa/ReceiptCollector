using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Порционная категоризация страницы «Товары» (ADR-028): suggest по подмножеству товаров
/// пользователя с серверным бюджетом запроса и пакетное assign-only сохранение.
/// <para>
/// Сервис stateless: порционность (≤ 5 уникальных названий) обеспечивает клиент, сервер принимает
/// любое подмножество товаров текущего пользователя (ADR-028 п. 1.3). Общий конвейер
/// <c>existing → cache → precategorized → ai</c> живёт в <see cref="ICommodityCategoryResolver"/>.
/// </para>
/// </summary>
internal sealed class CommodityBatchCategorizationService : ICommodityBatchCategorizationService
{
    /// <summary>Максимум уникальных названий в одной порции (ADR-028 п. 1.1, ограничение нагрузки на ИИ).</summary>
    private const int MaxUniqueNames = 20;

    /// <summary>Текст ошибки строки без названия (в resolver такие строки не отправляются).</summary>
    private const string EmptyNameError = "Название товара не заполнено.";

    private readonly ICommodityRepository _commodityRepository;
    private readonly ICommodityCategoryCache _cache;
    private readonly ICommodityCategoryResolver _resolver;
    private readonly IOptions<AiOptions> _aiOptions;
    private readonly IOptions<CommodityBatchCategorizationOptions> _batchOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CommodityBatchCategorizationService> _logger;

    public CommodityBatchCategorizationService(
        ICommodityRepository commodityRepository,
        ICommodityCategoryCache cache,
        ICommodityCategoryResolver resolver,
        IOptions<AiOptions> aiOptions,
        IOptions<CommodityBatchCategorizationOptions> batchOptions,
        TimeProvider timeProvider,
        ILogger<CommodityBatchCategorizationService> logger)
    {
        _commodityRepository = commodityRepository ?? throw new ArgumentNullException(nameof(commodityRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _aiOptions = aiOptions ?? throw new ArgumentNullException(nameof(aiOptions));
        _batchOptions = batchOptions ?? throw new ArgumentNullException(nameof(batchOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CommodityBatchCategorizationResult> SuggestAsync(
        Guid userId,
        IReadOnlyCollection<Guid> commodityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commodityIds);

        var startTimestamp = Stopwatch.GetTimestamp();
        var distinctIds = commodityIds.Distinct().ToList();
        var aiAvailable = !string.IsNullOrWhiteSpace(_aiOptions.Value.BaseUrl);

        var found = distinctIds.Count == 0
            ? Array.Empty<Commodity>()
            : (await _commodityRepository
                .GetByOwnerAsync(userId, distinctIds, cancellationToken)
                .ConfigureAwait(false)).ToArray();

        var missingCount = distinctIds.Count - found.Length;
        if (missingCount > 0)
        {
            // ADR-028 п. 11: только количество, без названий/id (чтение терпит устаревшую страницу).
            _logger.LogWarning(
                "Commodity batch suggest: {MissingCount} requested commodity ids are not accessible for the current user.",
                missingCount);
        }

        var results = new List<ReceiptItemCategorizationResult>(found.Length);
        var resolvable = new List<Commodity>(found.Length);
        foreach (var item in found)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                // В resolver пустые названия не отправляются: поведение suggest чека не меняется.
                results.Add(new ReceiptItemCategorizationResult(
                    item.Id,
                    item.Name,
                    null,
                    null,
                    CategorizationSourceNames.Undefined,
                    EmptyNameError));
            }
            else
            {
                resolvable.Add(item);
            }
        }

        var uniqueNameCount = resolvable
            .Select(item => item.Name)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (uniqueNameCount > MaxUniqueNames)
        {
            throw new ArgumentException(
                $"At most {MaxUniqueNames} unique commodity names are allowed per request (got {uniqueNameCount}).",
                nameof(commodityIds));
        }

        var deadlineExceeded = false;
        if (resolvable.Count > 0)
        {
            var (resolvedItems, exceeded) = await ResolveWithDeadlineAsync(resolvable, aiAvailable, cancellationToken)
                .ConfigureAwait(false);

            results.AddRange(resolvedItems);
            deadlineExceeded = exceeded;
        }

        var undefinedCount = results.Count(r => r.Source == CategorizationSourceNames.Undefined);

        if (deadlineExceeded)
        {
            _logger.LogWarning(
                "Commodity batch suggest deadline of {SuggestTimeout} exceeded; {UndefinedCount} rows remain undefined.",
                _batchOptions.Value.SuggestTimeout,
                undefinedCount);
        }

        // ADR-028 п. 11: размер порции, длительность, распределение source, ошибки, aiAvailable.
        // Названия — только Debug (логи без ПД пользователей, прецедент ADR-027 FR-8).
        _logger.LogDebug(
            "Commodity batch suggest completed in {ElapsedMs} ms: requested {RequestedCount} ids, found {FoundCount}, unique names {UniqueNameCount}, undefined {UndefinedCount}, missing {MissingCount}, aiAvailable {AiAvailable}, sources {Sources}, names: {Names}.",
            Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
            distinctIds.Count,
            found.Length,
            uniqueNameCount,
            undefinedCount,
            missingCount,
            aiAvailable,
            string.Join(", ", results
                .GroupBy(r => r.Source, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}={g.Count()}")),
            string.Join(" | ", resolvable.Select(i => i.Name)));

        return new CommodityBatchCategorizationResult(results, aiAvailable);
    }

    public async Task<int> ApplyAsync(
        Guid userId,
        IReadOnlyCollection<CommodityCategoryChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var byCommodityId = new Dictionary<Guid, CommodityCategory>(changes.Count);
        foreach (var change in changes)
        {
            if (change.Category == CommodityCategory.Undefined
                || !Enum.IsDefined(typeof(CommodityCategory), change.Category))
            {
                throw new ArgumentException(
                    $"Commodity '{change.CommodityId}': category Undefined (or unknown) cannot be explicitly assigned.",
                    nameof(changes));
            }

            if (!byCommodityId.TryAdd(change.CommodityId, change.Category))
            {
                throw new ArgumentException(
                    $"Duplicate commodity id '{change.CommodityId}' in changes.",
                    nameof(changes));
            }
        }

        if (byCommodityId.Count == 0)
        {
            return 0;
        }

        // Имена и текущие значения для кэша/счётчика берутся до записи: контракт репозитория
        // (ADR-028 п. 9) пары «id → name» не возвращает, второго SaveChanges не допускается
        // (открытый вопрос №3, подтверждённый вариант).
        var current = await _commodityRepository
            .GetByOwnerAsync(userId, byCommodityId.Keys.ToList(), cancellationToken)
            .ConfigureAwait(false);

        if (current.Count != byCommodityId.Count)
        {
            _logger.LogWarning(
                "Commodity batch save rejected as stale: {NotAccessibleCount} of {RequestedCount} commodity ids are not accessible.",
                byCommodityId.Count - current.Count,
                byCommodityId.Count);

            throw new CommoditiesNotAccessibleException(byCommodityId.Count - current.Count);
        }

        // C9: «то же значение» не является изменением — ни в счётчике, ни для кэша.
        var changed = current
            .Where(item => item.Category?.Id != (int)byCommodityId[item.Id])
            .ToList();

        int updated;
        try
        {
            updated = await _commodityRepository
                .UpdateCategoriesAsync(userId, byCommodityId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CommoditiesNotAccessibleException ex)
        {
            _logger.LogWarning(
                ex,
                "Commodity batch save rejected as stale: some commodity ids are not accessible.");
            throw;
        }

        if (updated > 0)
        {
            // ADR-019: только ПОСЛЕ успешной фиксации; first-wins/no-op при «замороженном» кэше.
            foreach (var item in changed)
            {
                _cache.TryAdd(
                    CommodityNameNormalizer.NormalizeName(item.Name),
                    byCommodityId[item.Id]);
            }
        }

        // ADR-028 п. 11: Information — только счётчики (число строк, пропущенных «не трогаем»,
        // известно вызывающему эндпоинту и логируется там).
        _logger.LogInformation(
            "Commodity batch save applied: {UpdatedCount} of {RequestedCount} rows changed.",
            updated,
            byCommodityId.Count);

        return updated;
    }

    /// <summary>
    /// Запускает общий конвейер с серверным бюджетом запроса (ADR-028 п. 3): по истечении
    /// <c>SuggestTimeout</c> отменяется только токен вызовов ИИ, поэтому незавершённые названия
    /// становятся <c>undefined</c>; отмена запроса клиентом (<paramref name="cancellationToken"/>)
    /// прерывает всё и не маскируется под частичный результат.
    /// Дедлайн управляется <see cref="TimeProvider"/>, чтобы тесты работали без реальных задержек.
    /// </summary>
    private async Task<(IReadOnlyList<ReceiptItemCategorizationResult> Items, bool DeadlineExceeded)>
        ResolveWithDeadlineAsync(
            IReadOnlyCollection<Commodity> items,
            bool aiEnabled,
            CancellationToken cancellationToken)
    {
        using var aiCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var timeout = _batchOptions.Value.SuggestTimeout;
        var timeoutTask = Task.Delay(timeout, _timeProvider, timeoutCancellation.Token);
        var resolveTask = _resolver.ResolveAsync(items, aiEnabled, aiCancellation.Token, cancellationToken);

        var completed = await Task.WhenAny(resolveTask, timeoutTask).ConfigureAwait(false);
        var deadlineExceeded = completed != resolveTask;

        if (deadlineExceeded)
        {
            await aiCancellation.CancelAsync().ConfigureAwait(false);
        }

        // Убираем незавершённый таймер после того, как конвейер отработал (или отменён клиентом).
        await timeoutCancellation.CancelAsync().ConfigureAwait(false);

        var resolved = await resolveTask.ConfigureAwait(false);
        return (resolved, deadlineExceeded);
    }
}
