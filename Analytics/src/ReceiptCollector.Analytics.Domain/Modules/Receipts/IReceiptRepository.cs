namespace ReceiptCollector.Analytics.Domain.Modules.Receipts;

public interface IReceiptRepository
{
    Task AddAsync(Receipt receipt, CancellationToken cancellationToken);
    Task DeleteAsync(Guid receiptId, Guid userId, CancellationToken cancellationToken);
    Task<Receipt?> GetByIdAsync(Guid receiptId, Guid userId, CancellationToken cancellationToken);
    Task<Receipt?> GetByExternalIdAsync(string externalId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Поиск чека по естественному ключу (user_id, purchased_at, total_amount) — D4:
    /// дедупликация cross-формата (чеки 2020 уже импортированы с legacy external_id)
    /// и сценария «физический чек добавлен дважды».
    /// </summary>
    Task<Receipt?> GetByNaturalKeyAsync(Guid userId, DateTime purchasedAt, decimal totalAmount, CancellationToken cancellationToken);
}
