namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public interface IUserRepository
{
    Task<User?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByTelegramIdAsync(int telegramId, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);
    
    Task UpdateAdminStatusAsync(int userTelegramId, bool isAdmin, CancellationToken cancellationToken);
}
