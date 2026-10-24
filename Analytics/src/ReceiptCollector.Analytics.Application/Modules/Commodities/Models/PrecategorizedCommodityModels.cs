using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

/// <summary>
/// DTO строки очереди для админского раздела (контракт GET /api/precategorized-commodities).
/// Имя категории вычисляется через <see cref="CommodityCategoryHelper.GetDisplayName"/> (D9).
/// </summary>
public sealed record PrecategorizedCommodityDto(
    Guid Id,
    string Name,
    Guid MerchantId,
    string? MerchantName,
    int ProposedCategoryId,
    string ProposedCategoryName,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// Результат подтверждения предложения (POST .../confirm). При
/// <see cref="PrecategorizedOperationOutcome.Applied"/> содержит применённую категорию
/// и число фактически обновлённых товаров (может быть 0, D10/FR-6.6).
/// </summary>
public sealed record ConfirmPrecategorizedCommodityResult(
    PrecategorizedOperationOutcome Outcome,
    Guid Id,
    int? CategoryId,
    string? CategoryName,
    int UpdatedCommodities);
