using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;

public interface IReceiptReadService
{
    Task<IReadOnlyCollection<ReceiptSummaryDto>> GetRecentAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken = default);
    Task<ReceiptDetailsDto?> GetByIdAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountByMerchantIdAsync(Guid userId, Guid merchantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ReceiptSummaryDto>> GetByMerchantIdAsync(Guid userId, Guid merchantId, int limit, int offset, CancellationToken cancellationToken = default);
}
