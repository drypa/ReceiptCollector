using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Merchants.Contracts;

/// <summary>
/// Узкий контракт batch-чтения товарных категорий по умолчанию магазинов чеков
/// (ADR-032, D1): используется шагом <c>merchant-default</c> общего конвейера
/// <see cref="Commodities.Contracts.ICommodityCategoryResolver"/>. Только чтение —
/// провайдер ничего не сохраняет и не кэширует; применение дефолта — отдельная
/// точка (импорт/дожим, ADR-032 пп. 3–4).
/// </summary>
public interface IMerchantDefaultCategoryProvider
{
    /// <summary>
    /// Возвращает для переданных идентификаторов чеков товарные категории по умолчанию
    /// магазинов этих чеков: <c>receiptId → CommodityCategory?</c>. Ровно один batch-запрос
    /// на порцию (N+1 запрещён — стиль FR-9.3 ADR-027); порция может относиться к разным
    /// магазинам — обрабатываются все.
    /// </summary>
    /// <remarks>
    /// Семантика словаря (безопасное чтение ADR-032, A1): магазин без дефолта
    /// (NULL / 0 / устаревший код справочника) даёт значение <c>null</c> — «дефолта нет»;
    /// чек, отсутствующий в хранилище, в словаре не присутствует. Потребитель обязан
    /// одинаково трактовать <c>null</c> и отсутствие ключа как промах шага.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, CommodityCategory?>> GetDefaultsByReceiptIdsAsync(
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken cancellationToken = default);
}
