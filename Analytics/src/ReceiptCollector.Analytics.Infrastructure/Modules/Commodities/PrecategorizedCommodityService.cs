using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

/// <summary>
/// Админская бизнес-логика предкатегоризации (ADR-027 п.4, FR-5/FR-6).
/// Подтверждение выполняет репозиторий в одной транзакции, после её фиксации категория
/// кладётся в сквозной in-memory кэш (first-wins/no-op — семантика ADR 019).
/// </summary>
internal sealed class PrecategorizedCommodityService : IPrecategorizedCommodityService
{
    private readonly IPrecategorizedCommodityRepository _repository;
    private readonly ICommodityCategoryCache _cache;

    public PrecategorizedCommodityService(
        IPrecategorizedCommodityRepository repository,
        ICommodityCategoryCache cache)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
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

        return new ConfirmPrecategorizedCommodityResult(
            PrecategorizedOperationOutcome.Applied,
            id,
            (int)category,
            CommodityCategoryHelper.GetDisplayName(category),
            result.UpdatedCommodities);
    }

    public Task<PrecategorizedOperationOutcome> DeclineAsync(Guid id, CancellationToken cancellationToken = default)
        => _repository.DeleteProposedAsync(id, cancellationToken);
}
