using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class MerchantRepository : IMerchantRepository
{
    private readonly ReceiptDbContext _dbContext;

    public MerchantRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(Merchant merchant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(merchant);

        var entity = await _dbContext.Merchants
            .FirstOrDefaultAsync(m => m.Id == merchant.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = MerchantEntity.Create(merchant);
            await _dbContext.Merchants.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            entity.Name = merchant.Name;
            entity.Category = merchant.Category;
            entity.Address = merchant.Address;
            entity.Inn = merchant.Inn;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Merchant?> GetByIdAsync(Guid merchantId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Merchants
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == merchantId, cancellationToken)
            .ConfigureAwait(false);

        return entity?.MapToDomain();
    }
}