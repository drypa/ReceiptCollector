using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

/// <summary>
/// Бизнес-логика админского workflow предкатегоризации (ADR-027 п.4/п.7, FR-5, FR-6).
/// Работает через <see cref="IPrecategorizedCommodityRepository"/> — единственную точку
/// глобального доступа; сервис сам <c>IgnoreQueryFilters</c> не использует (NFR-5).
/// </summary>
public interface IPrecategorizedCommodityService
{
    /// <summary>Постраничный список только <c>proposed</c> строк с поиском и общим количеством.</summary>
    Task<(IReadOnlyCollection<PrecategorizedCommodityDto> Items, int Total)> GetPageAsync(
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Изменение предложенной категории без применения к товарам (FR-5.2, UC-3).
    /// <c>applied</c> → <see cref="PrecategorizedOperationOutcome.Conflict"/>,
    /// нет строки → <see cref="PrecategorizedOperationOutcome.NotFound"/>.
    /// </summary>
    Task<PrecategorizedOperationOutcome> UpdateCategoryAsync(
        Guid id,
        CommodityCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подтверждение: транзакция репозитория (D10) и <c>TryAdd</c> в in-memory кэш
    /// строго после фиксации транзакции (FR-6.4).
    /// </summary>
    Task<ConfirmPrecategorizedCommodityResult> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Отклонение — физическое удаление <c>proposed</c>-строки (C4, FR-5.4).</summary>
    Task<PrecategorizedOperationOutcome> DeclineAsync(Guid id, CancellationToken cancellationToken = default);
}
