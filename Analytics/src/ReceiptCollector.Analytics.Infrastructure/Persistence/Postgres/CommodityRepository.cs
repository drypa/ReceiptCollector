using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class CommodityRepository : ICommodityRepository
{
    private readonly ReceiptDbContext _dbContext;

    public CommodityRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Commodity?> GetByIdAsync(Guid commodityId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Commodities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == commodityId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        return new Commodity(
            entity.Id,
            entity.ReceiptId,
            entity.Name,
            entity.Quantity,
            entity.UnitPrice,
            entity.Nds,
            entity.NdsSum,
            entity.CategoryId.HasValue
                ? new Category(entity.CategoryId.Value, entity.CategoryName ?? "")
                : null);
    }

    public async Task UpdateCategoryAsync(Guid commodityId, CommodityCategory category, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Commodities
            .FirstOrDefaultAsync(c => c.Id == commodityId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new InvalidOperationException($"Commodity with id '{commodityId}' not found.");
        }

        entity.CategoryId = (int)category;
        entity.CategoryName = CommodityCategoryHelper.GetDisplayName(category);

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
