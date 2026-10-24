namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

/// <summary>
/// Курсор сессии предкатегоризации (D14, ADR-027 п.6): последний обработанный кандидат.
/// Ключ детерминированного порядка выборки — пара <c>(name, merchant_id)</c> (FR-3.5).
/// Хранится в памяти hosted-сервиса и сбрасывается при рестарте Analytics.
/// </summary>
public sealed record CandidateCursor(string Name, Guid MerchantId);

/// <summary>Результат операции над строкой очереди (FR-5.7/5.8).</summary>
public enum PrecategorizedOperationOutcome
{
    /// <summary>Операция выполнена.</summary>
    Applied = 0,

    /// <summary>Строка не найдена → HTTP 404.</summary>
    NotFound = 1,

    /// <summary>Строка уже в статусе <c>applied</c> → HTTP 409.</summary>
    Conflict = 2
}

/// <summary>
/// Результат подтверждения строки очереди (D10, FR-6.6): строка переводится в <c>applied</c>
/// даже при <see cref="UpdatedCommodities"/> = 0.
/// </summary>
public sealed record ConfirmPrecategorizedResult(
    PrecategorizedOperationOutcome Outcome,
    string? Name,
    CommodityCategory? Category,
    int UpdatedCommodities);
