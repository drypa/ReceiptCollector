namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Строка очереди предкатегоризации (ADR-027 п.3): предложение категории для точного
/// наименования товара в контексте магазина. Денормализованного имени категории нет (D9) —
/// отображаемое имя вычисляется через <see cref="CommodityCategoryHelper.GetDisplayName"/>.
/// </summary>
public sealed class PrecategorizedCommodity
{
    public PrecategorizedCommodity(
        Guid id,
        string name,
        Guid merchantId,
        string? merchantName,
        CommodityCategory proposedCategory,
        CommodityCategory originalProposedCategory,
        PrecategorizedCommodityStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? appliedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
        MerchantId = merchantId;
        MerchantName = merchantName;
        ProposedCategory = proposedCategory;
        OriginalProposedCategory = originalProposedCategory;
        Status = status;
        CreatedAt = createdAt;
        AppliedAt = appliedAt;
    }

    public Guid Id { get; }

    /// <summary>Точное наименование товара, как в <c>commodities.name</c> (без нормализации).</summary>
    public string Name { get; }

    public Guid MerchantId { get; }

    /// <summary>Имя магазина — только для отображения админу (в таблице очереди).</summary>
    public string? MerchantName { get; }

    /// <summary>Предложенная категория; <see cref="CommodityCategory.Undefined"/> в очереди не хранится (D11).</summary>
    public CommodityCategory ProposedCategory { get; }

    /// <summary>
    /// Первоначальное предложение строки очереди (ADR-033 п. 1, решение D1). Пишется только фоном
    /// при создании строки; <c>PUT .../category</c> его не меняет, поэтому подтверждение может
    /// зафиксировать в журнале «предложено = исходное, принято = текущее».
    /// </summary>
    public CommodityCategory OriginalProposedCategory { get; }

    public PrecategorizedCommodityStatus Status { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? AppliedAt { get; }
}
