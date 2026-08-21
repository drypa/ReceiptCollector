namespace ReceiptCollector.Analytics.Domain.Modules.Commodities;

public interface ICommodityRepository
{
    Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default);
}
