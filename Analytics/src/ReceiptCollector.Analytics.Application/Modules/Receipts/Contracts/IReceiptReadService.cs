using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;

namespace ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;

public interface IReceiptReadService
{
    Task<IReadOnlyCollection<ReceiptSummaryDto>> GetRecentAsync(Guid userId, int limit, CancellationToken cancellationToken);
    Task<ReceiptDetailsDto?> GetByIdAsync(Guid userId, Guid receiptId, CancellationToken cancellationToken);
}
