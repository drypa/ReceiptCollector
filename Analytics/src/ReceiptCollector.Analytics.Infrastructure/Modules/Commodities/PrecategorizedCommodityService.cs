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
/// </summary>
internal sealed class PrecategorizedCommodityService : IPrecategorizedCommodityService
{
    private readonly IPrecategorizedCommodityRepository _repository;
    private readonly ICommodityCategoryCache _cache;
    private readonly ICategorizationDecisionRecorder _categoryDecisionRecorder;
    private readonly ILogger<PrecategorizedCommodityService> _logger;

    public PrecategorizedCommodityService(
        IPrecategorizedCommodityRepository repository,
        ICommodityCategoryCache cache,
        ICategorizationDecisionRecorder categoryDecisionRecorder,
        ILogger<PrecategorizedCommodityService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _categoryDecisionRecorder = categoryDecisionRecorder ?? throw new ArgumentNullException(nameof(categoryDecisionRecorder));
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
}
