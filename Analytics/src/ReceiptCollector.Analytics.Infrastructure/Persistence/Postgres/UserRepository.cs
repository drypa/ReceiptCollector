using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class UserRepository : IUserRepository
{
    private readonly ReceiptDbContext _dbContext;

    public UserRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<User> GetOrCreateAsync(string externalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("External id must be provided.", nameof(externalId));
        }

        var existing = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing.MapToDomain();
        }

        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Name = "<Unknown user>",
            ExternalId = externalId
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return user.MapToDomain();
    }
}
