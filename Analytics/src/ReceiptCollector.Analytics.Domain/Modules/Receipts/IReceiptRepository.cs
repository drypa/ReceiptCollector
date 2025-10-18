namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public interface IReceiptRepository
{
    Task AddAsync(Receipt receipt, CancellationToken cancellationToken);
    Task DeleteAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken);
}
