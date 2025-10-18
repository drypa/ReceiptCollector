namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public interface IReceiptRepository
{
    Task AddAsync(Receipt receipt, CancellationToken cancellationToken);
    Task DeleteAsync(Guid receiptId, Guid userId, CancellationToken cancellationToken);
    Task<Receipt?> GetByIdAsync(Guid receiptId, Guid userId, CancellationToken cancellationToken);
}
