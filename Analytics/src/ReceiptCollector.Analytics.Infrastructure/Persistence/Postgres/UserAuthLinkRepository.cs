using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Users;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class UserAuthLinkRepository : IUserAuthLinkRepository
{
    private readonly ReceiptDbContext _dbContext;

    public UserAuthLinkRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<UserAuthLink?> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = await _dbContext.UserAuthLinks
            .AsNoTracking()
            .Where(link => link.UserId == userId && link.UsedAt == null && link.ExpiresAt > now)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return entity?.ToDomain();
    }

    public async Task<UserAuthLink?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.UserAuthLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(link => link.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        return entity?.ToDomain();
    }

    public async Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var links = await _dbContext.UserAuthLinks
            .Where(link => link.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (links.Count == 0)
        {
            return;
        }

        _dbContext.UserAuthLinks.RemoveRange(links);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAsync(UserAuthLink link, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(link);

        var entity = UserAuthLinkEntity.FromDomain(link);
        await _dbContext.UserAuthLinks.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkAsUsedAsync(Guid linkId, DateTimeOffset usedAt, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.UserAuthLinks
            .FirstOrDefaultAsync(link => link.Id == linkId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return;
        }

        entity.UsedAt = usedAt;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
