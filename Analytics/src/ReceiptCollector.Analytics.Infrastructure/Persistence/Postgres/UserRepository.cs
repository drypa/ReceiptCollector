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

    public async Task<User?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("External id must be provided.", nameof(externalId));
        }

        var existing = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);

        return existing?.MapToDomain();
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return existing?.MapToDomain();
    }

    public async Task<User?> GetByTelegramIdAsync(int telegramId, CancellationToken cancellationToken)
    {
        if (telegramId <= 0)
        {
            throw new ArgumentException("Telegram id must be positive.", nameof(telegramId));
        }

        var existing = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.TelegramId == telegramId, cancellationToken)
            .ConfigureAwait(false);

        return existing?.MapToDomain();
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var entity = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.ExternalId == user.ExternalId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = UserEntity.Create(user);
            await _dbContext.Users.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            entity.Name = user.Name;
            entity.TelegramId = user.TelegramId;

            if (entity.Id != user.Id)
            {
                entity.Id = user.Id;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task BatchUpdateAdminStatusAsync(List<int> userTelegramIds, bool isAdmin,
        CancellationToken cancellationToken)
    {
        await _dbContext.Users
            .Where(u => userTelegramIds.Contains(u.TelegramId))
            .ForEachAsync(x => x.IsAdmin = isAdmin, cancellationToken)
            .ConfigureAwait(false);


        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}