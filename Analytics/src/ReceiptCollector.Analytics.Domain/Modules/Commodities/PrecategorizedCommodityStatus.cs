namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Статус строки очереди предкатегоризации (ADR-027 п.3/п.4, D2).
/// Хранится в БД как <c>smallint</c> с CHECK-ограничением.
/// </summary>
public enum PrecategorizedCommodityStatus
{
    /// <summary>Предложение ожидает решения администратора (wire-имя <c>proposed</c>).</summary>
    Proposed = 0,

    /// <summary>Предложение подтверждено и применено к товарам (wire-имя <c>applied</c>).</summary>
    Applied = 1
}

/// <summary>Wire-имена статусов для контракта API (<c>proposed</c>/<c>applied</c>, ADR-027 п.7).</summary>
public static class PrecategorizedCommodityStatusNames
{
    public const string Proposed = "proposed";
    public const string Applied = "applied";

    public static string ToWireName(PrecategorizedCommodityStatus status)
        => status == PrecategorizedCommodityStatus.Applied ? Applied : Proposed;
}
