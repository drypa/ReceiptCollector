using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class ReceiptRepository : IReceiptRepository
{
    private readonly ReceiptDbContext _dbContext;

    public ReceiptRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        _dbContext.SetCurrentUser(receipt.UserId);

        try
        {
            var existing = await _dbContext.Receipts
                .FirstOrDefaultAsync(r => r.Id == receipt.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                var entity = ReceiptEntity.Create(receipt);
                await _dbContext.Receipts.AddAsync(entity, cancellationToken).ConfigureAwait(false);
                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new ReceiptAlreadyExistsException(receipt.Id);
            }
        }
        finally
        {
            _dbContext.ClearCurrentUser();
        }
    }

    public async Task DeleteAsync(Guid receiptId, Guid userId, CancellationToken cancellationToken)
    {
        _dbContext.SetCurrentUser(userId);

        try
        {
            var entity = await _dbContext.Receipts
                .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
                .ConfigureAwait(false);

            if (entity is null)
            {
                return;
            }

            _dbContext.Receipts.Remove(entity);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbContext.ClearCurrentUser();
        }
    }

    public async Task<Receipt?> GetByIdAsync(Guid receiptId, Guid userId, CancellationToken cancellationToken)
    {
        _dbContext.SetCurrentUser(userId);

        try
        {
            var entity = await _dbContext.Receipts
                .AsNoTracking()
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
                .ConfigureAwait(false);

            return entity?.MapToDomain();
        }
        finally
        {
            _dbContext.ClearCurrentUser();
        }
    }

    public async Task<Receipt?> GetByExternalIdAsync(string externalId, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("External id must be provided.", nameof(externalId));
        }

        _dbContext.SetCurrentUser(userId);

        try
        {
            var entity = await _dbContext.Receipts
                .AsNoTracking()
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.ExternalId == externalId && r.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            return entity?.MapToDomain();
        }
        finally
        {
            _dbContext.ClearCurrentUser();
        }
    }
}