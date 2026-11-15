using Microsoft.Extensions.Logging;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Contracts;
using ReceiptCollector.Analytics.Application.Modules.CategorizationDecisions.Models;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Админская бизнес-логика предкатегоризации (ADR-027 п.4, FR-5/FR-6).
/// Подтверждение выполняет репозиторий в одной транзакции, после её фиксации категория
/// кладётся в сквозной in-memory кэш (first-wins/no-op — семантика ADR 019), затем —
/// одно событие журнала «предложено → принято» (ADR-033 п. 5, точка фиксации 3).
/// Массовые операции (ADR-036) сохраняют тот же порядок «commit → кэш → журнал»: одна транзакция
/// на весь список в репозитории, после commit — <c>TryAdd</c> по applied-строкам и один вызов
/// recorder'а всеми draft'ами с <see cref="CancellationToken.None"/>; отклонение кэш и журнал
/// не затрагивает.
/// </summary>
internal sealed class PrecategorizedCommodityService : IPrecategorizedCommodityService
{
    /// <summary>
    /// Порог «долгой» массовой операции для <c>Warning</c> (ADR-036 §7): раннее предупреждение
    /// о деградации обновления <c>commodities</c>. Заметно ниже дедлайна операции (90 с у
    /// эндпоинта, ADR-036 F1): превышение ещё не означает откат, но говорит, что батч подошёл
    /// к пределу бюджету запроса.
    /// </summary>
    private static readonly TimeSpan BulkSlowOperationThreshold = TimeSpan.FromSeconds(30);

    private readonly IPrecategorizedCommodityRepository _repository;
    private readonly ICommodityCategoryCache _cache;
    private readonly ICategorizationDecisionRecorder _categoryDecisionRecorder;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrecategorizedCommodityService> _logger;

    public PrecategorizedCommodityService(
        IPrecategorizedCommodityRepository repository,
        ICommodityCategoryCache cache,
        ICategorizationDecisionRecorder categoryDecisionRecorder,
        TimeProvider timeProvider,
        ILogger<PrecategorizedCommodityService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _categoryDecisionRecorder = categoryDecisionRecorder ?? throw new ArgumentNullException(nameof(categoryDecisionRecorder));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(IReadOnlyCollection<PrecategorizedCommodityDto> Items, int Total)> GetPageAsync(
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetPageAsync(limit, offset, search, cancellationToken).ConfigureAwait(false);
        var total = await _repository.GetPageCountAsync(search, cancellationToken).ConfigureAwait(false);

        var dtos = items
            .Select(item => new PrecategorizedCommodityDto(
                item.Id,
                item.Name,
                item.MerchantId,
                item.MerchantName,
                (int)item.ProposedCategory,
                CommodityCategoryHelper.GetDisplayName(item.ProposedCategory),
                PrecategorizedCommodityStatusNames.ToWireName(item.Status),
                item.CreatedAt))
            .ToList();

        return (dtos, total);
    }

    public Task<PrecategorizedOperationOutcome> UpdateCategoryAsync(
        Guid id,
        CommodityCategory category,
        CancellationToken cancellationToken = default)
    {
        // Дополнительная защита: эндпоинт валидирует значение, сервис не доверяет вызывающему (D11).
        if (category == CommodityCategory.Undefined || !Enum.IsDefined(typeof(CommodityCategory), category))
        {
            throw new ArgumentException("Category Undefined cannot be assigned to a proposal.", nameof(category));
        }

        return _repository.UpdateProposedCategoryAsync(id, category, cancellationToken);
    }

    public async Task<ConfirmPrecategorizedCommodityResult> ConfirmAsync(
        Guid id,
        Guid acceptedByUserId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await _repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
        {
            return new ConfirmPrecategorizedCommodityResult(
                PrecategorizedOperationOutcome.NotFound,
                id,
                null,
                null,
                0);
        }

        var result = await _repository
            .ConfirmAsync(id, proposal.ProposedCategory, cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome != PrecategorizedOperationOutcome.Applied
            || result.Name is null
            || result.Category is null)
        {
            // NotFound/Conflict — подтверждение не состоялось, событий журнала нет (ADR-033 п. 5).
            return new ConfirmPrecategorizedCommodityResult(
                result.Outcome,
                id,
                null,
                null,
                result.UpdatedCommodities);
        }

        var category = result.Category.Value;

        // FR-6.4: только ПОСЛЕ успешного commit транзакции; TryAdd — no-op при «замороженном» кэше.
        _cache.TryAdd(CommodityNameNormalizer.NormalizeName(result.Name), category);

        // Точка фиксации 3 (ADR-033 п. 5, решение D1): одно событие на строку очереди — решение
        // админа состоялось даже при updatedCommodities = 0. Предложено — неизменяемое исходное
        // предложение строки (прочитано до транзакции), принято — значение, реально применённое
        // в транзакции (result.Category совпадает с proposal.ProposedCategory, который сервис
        // передал репозиторию).
        await RecordDecisionsBestEffortAsync(
            [new CategorizationDecisionDraft(
                result.Name,
                CategorizationDecisionSources.Precategorized,
                proposal.OriginalProposedCategory,
                category,
                acceptedByUserId,
                AcceptedByAdmin: true)],
            cancellationToken)
            .ConfigureAwait(false);

        return new ConfirmPrecategorizedCommodityResult(
            PrecategorizedOperationOutcome.Applied,
            id,
            (int)category,
            CommodityCategoryHelper.GetDisplayName(category),
            result.UpdatedCommodities);
    }

    /// <summary>
    /// Вызов журнала строго после commit подтверждения (NFR-3/C1): ответ эндпоинта и число
    /// updatedCommodities от журнала не зависят. Recorder по контракту (T-03) не бросает;
    /// защитный <c>catch</c> гарантирует NFR-3 и в случае нарушения контракта (прецедент
    /// T-04/T-05). Запись журнала — последняя БД-операция запроса: после неё сервис и эндпоинт
    /// ничего не сохраняют, поэтому оставшиеся в change tracker сущности после сбоя
    /// <c>SaveChangesAsync</c> повторно не вставятся.
    /// </summary>
    private async Task RecordDecisionsBestEffortAsync(
        IReadOnlyCollection<CategorizationDecisionDraft> decisions,
        CancellationToken cancellationToken)
    {
        try
        {
            await _categoryDecisionRecorder.RecordAsync(decisions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Categorization decision log call failed after precategorized proposal was confirmed: {DecisionCount} events.",
                decisions.Count);
        }
    }

    public Task<PrecategorizedOperationOutcome> DeclineAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.DeleteProposedAsync(id, cancellationToken);

    public async Task<BulkConfirmResult> ConfirmManyAsync(
        IReadOnlyCollection<Guid> ids,
        Guid acceptedByUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        // Защита: пустой список — нулевой результат без обращения к репозиторию
        // (валидация запроса — ответственность эндпоинта, ADR-036 §1.3).
        if (ids.Count == 0)
        {
            return new BulkConfirmResult(0, 0, 0, 0, []);
        }

        var startTimestamp = _timeProvider.GetTimestamp();
        IReadOnlyCollection<BulkConfirmRepositoryItem> items;
        try
        {
            // Одна транзакция на весь список (B2): исключение/отмена означают полный откат —
            // ни одна строка не применена, поэтому кэш и журнал ниже не достигаются.
            items = await _repository.ConfirmManyAsync(ids, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Прерывание по дедлайну или уходу клиента (различает эндпоинт, ADR-036 §1.5):
            // транзакция откатана, применённых строк нет; разбивка классификации — в Debug-логе репозитория.
            _logger.LogWarning(
                "Bulk confirm of {RequestedCount} proposals was cancelled after {ElapsedMilliseconds} ms; no rows were applied.",
                ids.Count,
                (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            // Технический сбой (БД/сеть/deadlock/таймаут команды): полный откат (решение 6),
            // эндпоинт отдаёт 500. Тип исключения — в логе, наименования и ids — нет (логи без ПД).
            _logger.LogError(
                ex,
                "Bulk confirm of {RequestedCount} proposals failed after {ElapsedMilliseconds} ms; no rows were applied.",
                ids.Count,
                (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
            throw;
        }

        // ---- Всё ниже — строго после commit (ADR-036 §3): кэш, затем журнал (последняя БД-операция).

        // Кэш (ADR-019): TryAdd first-wins/no-op по каждой applied-строке. Поля Name/ProposedCategory
        // заполнены для Applied по контракту репозитория (снимок FOR UPDATE); проверка — защита
        // от нарушения контракта: строка без снимка не должна уронить операцию после commit.
        var drafts = new List<CategorizationDecisionDraft>();
        foreach (var item in items)
        {
            if (item.Outcome != PrecategorizedOperationOutcome.Applied
                || item.Name is null
                || item.ProposedCategory is not { } appliedCategory)
            {
                continue;
            }

            _cache.TryAdd(CommodityNameNormalizer.NormalizeName(item.Name), appliedCategory);

            // ADR-033/E1: одно событие на applied-строку — предложено = неизменяемое исходное
            // предложение снимка, принято = фактически применённая категория того же снимка (D1).
            // Fallback `?? appliedCategory` — защита от нарушения контракта репозитория
            // (OriginalProposedCategory заполнен у Applied по снимку FOR UPDATE), а не штатная семантика.
            drafts.Add(new CategorizationDecisionDraft(
                item.Name,
                CategorizationDecisionSources.Precategorized,
                item.OriginalProposedCategory ?? appliedCategory,
                appliedCategory,
                acceptedByUserId,
                AcceptedByAdmin: true));
        }

        if (drafts.Count > 0)
        {
            // Один вызов recorder'а на весь батч (E1) с CancellationToken.None: данные уже
            // зафиксированы, события не должны теряться из-за ухода администратора со страницы.
            // Recorder по контракту не бросает; защитный catch — NFR-3 (сбой журнала не меняет
            // счётчики, кэш и ответ).
            await RecordDecisionsBestEffortAsync(drafts, CancellationToken.None).ConfigureAwait(false);
        }

        var results = new List<BulkConfirmItemResult>(ids.Count);
        var applied = 0;
        var alreadyApplied = 0;
        var notFound = 0;
        var updatedCommodities = 0;

        // Порядок ответа — порядок запроса (ADR-036 §1.1): сопоставление по id, а не по порядку
        // снимка. Записи репозитория может и не быть (защита) — это skipped/not-found,
        // инвариант applied + skipped = requested сохраняется.
        var itemById = BuildByIdIndex(items, item => item.Id);
        foreach (var id in ids)
        {
            if (!itemById.TryGetValue(id, out var item))
            {
                notFound++;
                results.Add(new BulkConfirmItemResult(id, BulkItemOutcomes.Skipped, BulkSkipReasons.NotFound, null, null, null));
                continue;
            }

            switch (item.Outcome)
            {
                case PrecategorizedOperationOutcome.Applied:
                    applied++;
                    updatedCommodities += item.UpdatedCommodities;
                    results.Add(new BulkConfirmItemResult(
                        id,
                        BulkItemOutcomes.Applied,
                        Reason: null,
                        CategoryId: (int?)item.ProposedCategory,
                        CategoryName: item.ProposedCategory is { } category
                            ? CommodityCategoryHelper.GetDisplayName(category)
                            : null,
                        item.UpdatedCommodities));
                    break;
                case PrecategorizedOperationOutcome.Conflict:
                    alreadyApplied++;
                    results.Add(new BulkConfirmItemResult(id, BulkItemOutcomes.Skipped, BulkSkipReasons.AlreadyApplied, null, null, null));
                    break;
                default:
                    notFound++;
                    results.Add(new BulkConfirmItemResult(id, BulkItemOutcomes.Skipped, BulkSkipReasons.NotFound, null, null, null));
                    break;
            }
        }

        var skipped = alreadyApplied + notFound;
        var elapsed = _timeProvider.GetElapsedTime(startTimestamp);

        // §7: одна Information-строка на операцию со счётчиками, разбивкой пропусков, длительностью
        // и признаком отмены. Наименования и ids — не здесь (логи без ПД).
        _logger.LogInformation(
            "Bulk confirm: requested {RequestedCount}, applied {AppliedCount}, skipped {SkippedCount} "
            + "(already applied {AlreadyAppliedCount}, not found {NotFoundCount}), updated commodities {UpdatedCommodities}, "
            + "elapsed {ElapsedMilliseconds} ms, cancellation requested: {CancellationRequested}.",
            ids.Count,
            applied,
            skipped,
            alreadyApplied,
            notFound,
            updatedCommodities,
            (long)elapsed.TotalMilliseconds,
            cancellationToken.IsCancellationRequested);

        LogBulkSkippedAllWarning("confirm", ids.Count, alreadyApplied, notFound, skipped);
        LogBulkSlowWarning("confirm", elapsed);

        return new BulkConfirmResult(ids.Count, applied, skipped, updatedCommodities, results);
    }

    public async Task<BulkDeclineResult> DeclineManyAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return new BulkDeclineResult(0, 0, 0, []);
        }

        var startTimestamp = _timeProvider.GetTimestamp();
        IReadOnlyCollection<BulkDeclineRepositoryItem> items;
        try
        {
            items = await _repository.DeclineManyAsync(ids, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Bulk decline of {RequestedCount} proposals was cancelled after {ElapsedMilliseconds} ms; no rows were removed.",
                ids.Count,
                (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Bulk decline of {RequestedCount} proposals failed after {ElapsedMilliseconds} ms; no rows were removed.",
                ids.Count,
                (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
            throw;
        }

        // Отклонение — не точка фиксации решения: кэш и журнал не затрагиваются (ADR-036 §3).
        var results = new List<BulkDeclineItemResult>(ids.Count);
        var declined = 0;
        var alreadyApplied = 0;
        var notFound = 0;

        var itemById = BuildByIdIndex(items, item => item.Id);
        foreach (var id in ids)
        {
            if (!itemById.TryGetValue(id, out var item))
            {
                notFound++;
                results.Add(new BulkDeclineItemResult(id, BulkItemOutcomes.Skipped, BulkSkipReasons.NotFound));
                continue;
            }

            switch (item.Outcome)
            {
                case PrecategorizedOperationOutcome.Applied:
                    declined++;
                    results.Add(new BulkDeclineItemResult(id, BulkItemOutcomes.Declined, Reason: null));
                    break;
                case PrecategorizedOperationOutcome.Conflict:
                    alreadyApplied++;
                    results.Add(new BulkDeclineItemResult(id, BulkItemOutcomes.Skipped, BulkSkipReasons.AlreadyApplied));
                    break;
                default:
                    notFound++;
                    results.Add(new BulkDeclineItemResult(id, BulkItemOutcomes.Skipped, BulkSkipReasons.NotFound));
                    break;
            }
        }

        var skipped = alreadyApplied + notFound;
        var elapsed = _timeProvider.GetElapsedTime(startTimestamp);

        _logger.LogInformation(
            "Bulk decline: requested {RequestedCount}, declined {DeclinedCount}, skipped {SkippedCount} "
            + "(already applied {AlreadyAppliedCount}, not found {NotFoundCount}), "
            + "elapsed {ElapsedMilliseconds} ms, cancellation requested: {CancellationRequested}.",
            ids.Count,
            declined,
            skipped,
            alreadyApplied,
            notFound,
            (long)elapsed.TotalMilliseconds,
            cancellationToken.IsCancellationRequested);

        LogBulkSkippedAllWarning("decline", ids.Count, alreadyApplied, notFound, skipped);
        LogBulkSlowWarning("decline", elapsed);

        return new BulkDeclineResult(ids.Count, declined, skipped, results);
    }

    /// <summary>
    /// Индекс результатов репозитория по id для сопоставления с порядком запроса (ADR-036 §1.1).
    /// TryAdd: при неожиданном дубле записи первого вхождения достаточно (дубликаты id отклоняет
    /// эндпоинт, ADR-036 §1.3).
    /// </summary>
    private static Dictionary<Guid, T> BuildByIdIndex<T>(IReadOnlyCollection<T> items, Func<T, Guid> idSelector)
    {
        var index = new Dictionary<Guid, T>(items.Count);
        foreach (var item in items)
        {
            index.TryAdd(idSelector(item), item);
        }

        return index;
    }

    /// <summary>
    /// §7 ADR-036: все строки пропущены — штатный признак чужого окна или устаревшего снимка
    /// страницы, но для наблюдаемости это Warning, а не Information.
    /// </summary>
    private void LogBulkSkippedAllWarning(
        string action,
        int requested,
        int alreadyApplied,
        int notFound,
        int skipped)
    {
        if (skipped == 0 || skipped != requested)
        {
            return;
        }

        _logger.LogWarning(
            "Bulk {Action}: all {RequestedCount} rows were skipped (already applied {AlreadyAppliedCount}, "
            + "not found {NotFoundCount}) — usually another window or a stale page snapshot.",
            action,
            requested,
            alreadyApplied,
            notFound);
    }

    /// <summary>
    /// §7 ADR-036: длительность выше порога — раннее предупреждение о деградации
    /// (стоимость обновления commodities растёт с объёмом таблицы).
    /// </summary>
    private void LogBulkSlowWarning(string action, TimeSpan elapsed)
    {
        if (elapsed <= BulkSlowOperationThreshold)
        {
            return;
        }

        _logger.LogWarning(
            "Bulk {Action}: operation took {ElapsedMilliseconds} ms, above the {ThresholdSeconds} s warning threshold.",
            action,
            (long)elapsed.TotalMilliseconds,
            BulkSlowOperationThreshold.TotalSeconds);
    }
}
