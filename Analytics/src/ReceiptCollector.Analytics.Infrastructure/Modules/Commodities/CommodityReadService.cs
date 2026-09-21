using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Models;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Commodities;

internal sealed class CommodityReadService : ICommodityReadService
{
    private readonly ReceiptDbContext _dbContext;

    public CommodityReadService(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyCollection<CommodityItemDto>> GetAsync(
        Guid userId, int limit, int offset,
        CommodityCategoryFilter categoryFilter = CommodityCategoryFilter.Any,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_dbContext.Commodities
            .AsNoTracking()
            .Include(c => c.Receipt)
            .ThenInclude(r => r.Merchant)
            .Where(c => c.Receipt.UserId == userId), categoryFilter);

        return await query
            .OrderByDescending(c => c.Receipt.PurchasedAt)
            .ThenBy(c => c.Name)
            .Skip(offset)
            .Take(limit)
            .Select(c => new CommodityItemDto(
                c.Id,
                c.Receipt.Merchant.Name,
                c.ReceiptId,
                c.Receipt.PurchasedAt,
                c.Name,
                c.Quantity,
                c.UnitPrice,
                c.Quantity * c.UnitPrice,
                c.CategoryId,
                c.CategoryName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> GetTotalCountAsync(
        Guid userId,
        CommodityCategoryFilter categoryFilter = CommodityCategoryFilter.Any,
        CancellationToken cancellationToken = default)
    {
        return await ApplyFilter(_dbContext.Commodities
            .AsNoTracking()
            .Where(c => c.Receipt.UserId == userId), categoryFilter)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IQueryable<CommodityEntity> ApplyFilter(
        IQueryable<CommodityEntity> query,
        CommodityCategoryFilter categoryFilter)
    {
        return categoryFilter switch
        {
            CommodityCategoryFilter.Uncategorized => query.Where(c => c.CategoryId == null),
            CommodityCategoryFilter.Undefined => query.Where(c => c.CategoryId == 0),
            _ => query
        };
    }
}
