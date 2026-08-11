namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public interface IUserAuthLinkRepository
{
    Task<UserAuthLink?> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAuthLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(UserAuthLink link, CancellationToken cancellationToken);

    Task MarkAsUsedAsync(Guid linkId, DateTimeOffset usedAt, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically marks the link as used if it is not already used.
    /// </summary>
    /// <returns><c>true</c> if the link was marked as used by this call; <c>false</c> if the link
    /// did not exist or was already used.</returns>
    Task<bool> TryMarkAsUsedAsync(Guid linkId, DateTimeOffset usedAt, CancellationToken cancellationToken);
}
