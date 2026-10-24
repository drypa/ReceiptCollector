using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Infrastructure.Configuration.Options;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Фоновая предкатегоризация товаров в простое (ADR-027 п.6, C5, D4–D7, D12, D14, FR-1.5, FR-2, FR-4, FR-8).
/// <para>
/// Single instance (D12): сервис рассчитан на один экземпляр Analytics, распределённой блокировки нет.
/// При горизонтальном масштабировании каждый экземпляр запустит свой фон (unique-индекс частично
/// защищает от гонок вставок) — координация вне объёма задачи.
/// </para>
/// </summary>
internal sealed class PrecategorizationBackgroundService : BackgroundService
{
    /// <summary>Максимальная длина наименования в схеме (FR-4.7).</summary>
    private const int MaxNameLength = 256;

    /// <summary>Реестр категорий для ИИ (имена enum без Undefined) — как в ручном пути (ADR 009).</summary>
    private static readonly IReadOnlyCollection<string> CategoryNames = CommodityCategoryHelper.GetAll()
        .Where(c => c.Id != CommodityCategory.Undefined)
        .Select(c => c.Id.ToString())
        .ToList();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PrecategorizationOptions> _options;
    private readonly IOptions<AiOptions> _aiOptions;
    private readonly IApiActivityMonitor _monitor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrecategorizationBackgroundService> _logger;

    /// <summary>
    /// Курсор сессий (D14): последний обработанный кандидат, включая пропуски/ошибки.
    /// Хранится в памяти сервиса и сбрасывается при рестарте Analytics.
    /// </summary>
    private CandidateCursor? _cursor;

    public PrecategorizationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PrecategorizationOptions> options,
        IOptions<AiOptions> aiOptions,
        IApiActivityMonitor monitor,
        TimeProvider timeProvider,
        ILogger<PrecategorizationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _aiOptions = aiOptions ?? throw new ArgumentNullException(nameof(aiOptions));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            // FR-1.5: Enabled=false отключает только фон; админский раздел и очередь в suggest работают.
            _logger.LogInformation("Precategorization background service is disabled (Precategorization:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "Precategorization background service started. IdleTimeout: {IdleTimeout}, IdleCheckInterval: {IdleCheckInterval}, BatchLimit: {BatchLimit}.",
            _options.Value.IdleTimeout,
            _options.Value.IdleCheckInterval,
            _options.Value.BatchLimit);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_monitor.IsIdle(_options.Value.IdleTimeout))
            {
                try
                {
                    await RunSessionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break; // FR-2.8: graceful shutdown хоста
                }
                catch (Exception ex)
                {
                    // FR-2.7: ошибка сессии не валит процесс, повтор — на следующем цикле.
                    _logger.LogError(ex, "Precategorization session failed. Retrying on the next idle check.");
                }
            }

            try
            {
                await Task.Delay(_options.Value.IdleCheckInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Precategorization background service stopped.");
    }

    /// <summary>Одна сессия предкатегоризации: последовательная обработка до лимита/активности/конца кандидатов.</summary>
    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetUtcNow();
        var state = new SessionState();
        var stopReason = "no_candidates";

        _logger.LogInformation(
            "Precategorization session started. Last activity: {LastActivityUtc}, cursor: {Cursor}.",
            _monitor.LastActivityUtc,
            _cursor is null ? "<none>" : $"{_cursor.Name}/{_cursor.MerchantId}");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPrecategorizedCommodityRepository>();
            var cache = scope.ServiceProvider.GetRequiredService<ICommodityCategoryCache>();
            var aiClient = scope.ServiceProvider.GetRequiredService<IAiClient>();

            IReadOnlyCollection<CandidateCursor> candidates;
            try
            {
                candidates = await repository
                    .GetCandidatesAsync(_cursor, _options.Value.BatchLimit, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // FR-2.7: ошибка выборки кандидатов завершает сессию, но не процесс.
                stopReason = "error";
                _logger.LogError(ex, "Precategorization candidate selection failed; session aborted.");
                return;
            }

            state.Candidates = candidates.Count;

            if (candidates.Count == 0)
            {
                stopReason = "no_candidates";
                return;
            }

            var processed = 0;
            foreach (var candidate in candidates)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    stopReason = "shutdown";
                    break;
                }

                var aiCallsBefore = state.AiCalls;

                try
                {
                    await ProcessCandidateAsync(candidate, repository, cache, aiClient, state, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    stopReason = "shutdown";
                    break;
                }
                catch (Exception ex)
                {
                    // FR-2.6: ошибка одной позиции логируется, сессия продолжается.
                    _logger.LogWarning(
                        ex,
                        "Precategorization failed for candidate '{ProductName}' (merchant {MerchantId}).",
                        candidate.Name,
                        candidate.MerchantId);
                }

                // D14: курсор двигается всегда — включая Undefined/ошибки/пропуски.
                _cursor = candidate;
                processed++;

                if (!_monitor.IsIdle(_options.Value.IdleTimeout))
                {
                    // FR-2.5: появился HTTP-запрос — сессия завершается после текущей позиции.
                    stopReason = "activity";
                    break;
                }

                if (processed >= _options.Value.BatchLimit)
                {
                    stopReason = "batch_limit";
                    break;
                }

                // D4/FR-2.4: пауза выдерживается только после фактического вызова ИИ и только
                // между вызовами — строка к этому моменту уже сохранена, поэтому остановка хоста
                // или появление запроса во время паузы не теряют результат оплаченного вызова
                // (FR-2.5, FR-2.8). Для existing/cache пауза не требуется.
                if (state.AiCalls > aiCallsBefore && _options.Value.IntervalBetweenCalls > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(
                            _options.Value.IntervalBetweenCalls,
                            _timeProvider,
                            cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        stopReason = "shutdown";
                        break;
                    }
                }
            }
        }
        finally
        {
            var duration = _timeProvider.GetUtcNow() - startedAt;
            _logger.LogInformation(
                "Precategorization session finished. Reason: {Reason}, candidates: {Candidates}, proposals: {Proposals}, aiCalls: {AiCalls}, aiErrors: {AiErrors}, duration: {DurationMs} ms.",
                stopReason,
                state.Candidates,
                state.Proposals,
                state.AiCalls,
                state.AiErrors,
                duration.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Обработка одного кандидата: глобальный existing-запрос → in-memory кэш → ИИ (D1, FR-3.6, FR-4.3).
    /// Результат ИИ сохраняется сразу; пауза <c>IntervalBetweenCalls</c> выдерживается циклом сессии
    /// между вызовами (D4, FR-2.4, FR-2.5).
    /// </summary>
    private async Task ProcessCandidateAsync(
        CandidateCursor candidate,
        IPrecategorizedCommodityRepository repository,
        ICommodityCategoryCache cache,
        IAiClient aiClient,
        SessionState state,
        CancellationToken cancellationToken)
    {
        var name = candidate.Name;

        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            // FR-4.7: пропуск с предупреждением; курсор двигается вызывающим кодом.
            _logger.LogWarning(
                "Skipping precategorization candidate with invalid name (length {NameLength}, merchant {MerchantId}).",
                name?.Length ?? 0,
                candidate.MerchantId);
            return;
        }

        var knownCategory = await repository
            .GetKnownCategoryByNameAsync(name, cancellationToken)
            .ConfigureAwait(false);

        if (knownCategory is { } existingCategory)
        {
            await AddProposalAsync(repository, candidate, existingCategory, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalized = CommodityNameNormalizer.NormalizeName(name);
        if (cache.TryGet(normalized, out var cachedCategory) && cachedCategory != CommodityCategory.Undefined)
        {
            await AddProposalAsync(repository, candidate, cachedCategory, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(_aiOptions.Value.BaseUrl))
        {
            // D5: ИИ не настроен — предложения из existing/кэша создаются, ИИ не вызывается.
            if (!state.AiUnavailableWarned)
            {
                state.AiUnavailableWarned = true;
                _logger.LogWarning(
                    "AI categorization is not configured (AI:BaseUrl is empty); precategorization session will only use known categories and cache.");
            }

            return;
        }

        AiSuggestionResult? suggestion = null;
        AiClientException? aiError = null;
        var callDuration = TimeSpan.Zero;

        try
        {
            var callStartedAt = _timeProvider.GetUtcNow();
            suggestion = await aiClient
                .SuggestCategoryAsync(name, CategoryNames, cancellationToken)
                .ConfigureAwait(false);
            callDuration = _timeProvider.GetUtcNow() - callStartedAt;
        }
        catch (AiClientException ex)
        {
            aiError = ex;
        }
        finally
        {
            state.AiCalls++;
        }

        if (aiError is not null)
        {
            state.AiErrors++;
            _logger.LogWarning(aiError, "AI categorization failed for '{ProductName}'.", name);
            return;
        }

        if (suggestion is null)
        {
            state.AiErrors++;
            _logger.LogWarning("AI categorization returned no result for '{ProductName}'.", name);
            return;
        }

        var category = suggestion.Category;
        if (category == CommodityCategory.Undefined || !Enum.IsDefined(typeof(CommodityCategory), category))
        {
            // D6/FR-4.4: Undefined строку не создаёт.
            state.AiErrors++;
            _logger.LogWarning(
                "AI categorization returned Undefined for '{ProductName}'; no proposal created.",
                name);
            return;
        }

        _logger.LogInformation(
            "AI categorization for '{ProductName}': {Category} ({DurationMs} ms).",
            name,
            category,
            callDuration.TotalMilliseconds);

        await AddProposalAsync(repository, candidate, category, state, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddProposalAsync(
        IPrecategorizedCommodityRepository repository,
        CandidateCursor candidate,
        CommodityCategory category,
        SessionState state,
        CancellationToken cancellationToken)
    {
        var added = await repository
            .TryAddProposalAsync(candidate, category, cancellationToken)
            .ConfigureAwait(false);

        if (added)
        {
            state.Proposals++;
            _logger.LogInformation(
                "Precategorized proposal created for '{ProductName}' (merchant {MerchantId}): {Category}.",
                candidate.Name,
                candidate.MerchantId,
                category);
        }
    }

    /// <summary>Счётчики одной сессии (FR-8), без персональных данных пользователей.</summary>
    private sealed class SessionState
    {
        public int Candidates { get; set; }

        public int Proposals { get; set; }

        public int AiCalls { get; set; }

        public int AiErrors { get; set; }

        public bool AiUnavailableWarned { get; set; }
    }
}
