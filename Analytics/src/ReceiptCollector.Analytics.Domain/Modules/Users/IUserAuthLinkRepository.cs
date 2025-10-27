namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public interface IUserAuthLinkRepository
{
    Task<UserAuthLink?> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAuthLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(UserAuthLink link, CancellationToken cancellationToken);

    Task MarkAsUsedAsync(Guid linkId, DateTimeOffset usedAt, CancellationToken cancellationToken);
}
