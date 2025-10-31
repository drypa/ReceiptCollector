using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;

public interface IReceiptReadService
{
    Task<IReadOnlyCollection<ReceiptSummaryDto>> GetRecentAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken);
    Task<ReceiptDetailsDto?> GetByIdAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken);
    Task<int> GetTotalCountAsync(Guid userId, CancellationToken cancellationToken);
}
