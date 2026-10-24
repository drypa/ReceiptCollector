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

    public Guid MerchantId { get; set; }

    public MerchantEntity? Merchant { get; set; }

    /// <summary>Предложенная категория; <c>Undefined</c> (0) в очереди не хранится (D11).</summary>
    public int ProposedCategoryId { get; set; }

    public PrecategorizedCommodityStatus Status { get; set; } = PrecategorizedCommodityStatus.Proposed;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }
}
