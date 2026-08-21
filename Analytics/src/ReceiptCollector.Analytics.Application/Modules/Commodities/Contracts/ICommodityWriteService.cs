using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

public interface ICommodityWriteService
{
    Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default);
}
