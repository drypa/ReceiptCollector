namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

public interface ICommodityRepository
{
    Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Присваивает категорию товару (сбрасывает категорию при <paramref name="category"/> == null).
    /// </summary>
    Task UpdateCategoryAsync(Guid commodityId, CommodityCategory? category, CancellationToken cancellationToken = default);
}
