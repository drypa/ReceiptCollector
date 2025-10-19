namespace ReceiptCollector.Analytics.Domain.Modules.Users;

public interface IUserRepository
{
    Task<User> GetOrCreateAsync(string externalId, CancellationToken cancellationToken);
}
