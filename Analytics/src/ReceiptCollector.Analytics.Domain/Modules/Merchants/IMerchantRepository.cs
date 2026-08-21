namespace ReceiptCollector.Analytics.Domain.Modules.Merchants;

public interface IMerchantRepository
{
    Task AddAsync(Merchant merchant, CancellationToken cancellationToken = default);
    Task<Merchant?> GetByIdAsync(Guid merchantId, CancellationToken cancellationToken = default);
    Task<Merchant?> GetByInnAsync(string inn, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Merchant>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpdateCategoryAsync(Guid merchantId, MerchantCategory category, CancellationToken cancellationToken = default);
}