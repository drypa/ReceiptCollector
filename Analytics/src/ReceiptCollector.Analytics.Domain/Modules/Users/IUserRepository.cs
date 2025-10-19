namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public interface IUserRepository
{
    Task<User?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);
}
