using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Domain.Modules.CategorizationDecisions;

/// <summary>
/// Сырое событие журнала категоризации «предложено → принято» (ADR-033 п. 1–2, решения B1/F1).
/// Записывается по одному на каждое подтверждение человеком ранее предложенной категории (FR-8);
/// дедупликация выполняется только на чтении. Коды категорий — <see cref="CommodityCategory"/>
/// без FK (справочник — enum, ADR 005), имя принявшего не денормализуется.
/// </summary>
public sealed record CategorizationDecision(
    Guid Id,
    string CommodityName,
    string NormalizedName,
    string SuggestionSource,
    CommodityCategory SuggestedCategory,
    CommodityCategory FinalCategory,
    Guid AcceptedBy,
    bool AcceptedByAdmin,
    DateTimeOffset CreatedAt);
