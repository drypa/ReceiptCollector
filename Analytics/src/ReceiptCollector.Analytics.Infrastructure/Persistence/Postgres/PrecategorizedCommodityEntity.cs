using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// EF-сущность очереди предкатегоризации (ADR-027 п.3). Query-filter на неё НЕ ставится:
/// очередь общая для всех пользователей (ADR-027 п.2).
/// </summary>
internal sealed class PrecategorizedCommodityEntity
{
    public Guid Id { get; set; }

    /// <summary>Точное наименование товара, как в <c>commodities.name</c> (varchar(256)).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Очищенный ключ строки (ADR-034 п. 2, решение B1): <c>CommodityNameNormalizer.NormalizeName(Name)</c>
    /// (varchar(256)). Пишется один раз при вставке (<c>TryAddProposalAsync</c>); сырое <see cref="Name"/>
    /// сохраняется для UI и журнала (FR-4).
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;

    public Guid MerchantId { get; set; }

    public MerchantEntity? Merchant { get; set; }

    /// <summary>Предложенная категория; <c>Undefined</c> (0) в очереди не хранится (D11).</summary>
    public int ProposedCategoryId { get; set; }

    /// <summary>
    /// Первоначальное предложение строки (ADR-033 п. 1, решение D1). Записывается только фоном
    /// при вставке; <c>UpdateProposedCategoryAsync</c> его не трогает.
    /// </summary>
    public int OriginalProposedCategoryId { get; set; }

    public PrecategorizedCommodityStatus Status { get; set; } = PrecategorizedCommodityStatus.Proposed;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }
}
