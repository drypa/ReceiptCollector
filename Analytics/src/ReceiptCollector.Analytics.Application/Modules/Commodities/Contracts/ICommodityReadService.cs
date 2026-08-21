using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;

public interface ICommodityReadService
{
    Task<IReadOnlyCollection<CommodityItemDto>> GetAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken = default);

    Task<int> GetTotalCountAsync(Guid userId, CancellationToken cancellationToken = default);
}
