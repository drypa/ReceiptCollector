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

    public async Task DeleteAsync(Guid receiptId, CancellationToken cancellationToken)
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

    public async Task<Receipt?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Receipts
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken)
            .ConfigureAwait(false);

        return entity?.MapToDomain();
    }
}