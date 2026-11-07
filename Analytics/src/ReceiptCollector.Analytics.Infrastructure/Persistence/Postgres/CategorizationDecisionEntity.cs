using ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

/// <summary>
/// EF-сущность сырого события журнала категоризации (ADR-033 п. 1–2). Query-filter не ставится:
/// журнал глобальный, доступ — только через admin-API (T-08).
/// </summary>
internal sealed class CategorizationDecisionEntity
{
    public Guid Id { get; set; }

    /// <summary>Исходное название товара, как в чеке (varchar(256)).</summary>
    public string CommodityName { get; set; } = string.Empty;

    /// <summary>Нормализованное название (<c>CommodityNameNormalizer.NormalizeName</c>, varchar(256)).</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>Wire-имя источника предложения: ai | precategorized | cache | merchant-default.</summary>
    public string SuggestionSource { get; set; } = string.Empty;

    /// <summary>Код <c>CommodityCategory</c>; <c>Undefined</c> (0) запрещён CHECK'ом.</summary>
    public int SuggestedCategoryId { get; set; }

    /// <summary>Итоговая категория; 0 = <c>Undefined</c> («Не указана»).</summary>
    public int FinalCategoryId { get; set; }

    /// <summary><c>users.id</c> принявшего решение; FK осознанно нет (F1).</summary>
    public Guid AcceptedBy { get; set; }

    /// <summary>Снимок <c>users.is_admin</c> на момент решения.</summary>
    public bool AcceptedByAdmin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    internal static CategorizationDecisionEntity Create(CategorizationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return new CategorizationDecisionEntity
        {
            Id = decision.Id,
            CommodityName = decision.CommodityName,
            NormalizedName = decision.NormalizedName,
            SuggestionSource = decision.SuggestionSource,
            SuggestedCategoryId = (int)decision.SuggestedCategory,
            FinalCategoryId = (int)decision.FinalCategory,
            AcceptedBy = decision.AcceptedBy,
            AcceptedByAdmin = decision.AcceptedByAdmin,
            CreatedAt = decision.CreatedAt
        };
    }
}
