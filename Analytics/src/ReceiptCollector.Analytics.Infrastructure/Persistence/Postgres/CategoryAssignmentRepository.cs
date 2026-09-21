using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

internal sealed class CategoryAssignmentRepository : ICategoryAssignmentRepository
{
    private readonly ReceiptDbContext _dbContext;

    public CategoryAssignmentRepository(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<CommodityCategoryAssignment?> GetByNormalizedNameAsync(
        string normalizedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);

        var entity = await _dbContext.CommodityCategoryAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.NormalizedName == normalizedName, cancellationToken)
            .ConfigureAwait(false);

        return entity?.MapToDomain();
    }

    public async Task UpsertAsync(
        string name,
        string normalizedName,
        CommodityCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero((int)category);

        var timestamp = DateTime.UtcNow;

        var existing = await _dbContext.CommodityCategoryAssignments
            .FirstOrDefaultAsync(a => a.NormalizedName == normalizedName, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _dbContext.CommodityCategoryAssignments.Add(
                CommodityCategoryAssignmentEntity.Create(name, normalizedName, category, timestamp));
        }
        else
        {
            existing.Name = name;
            existing.CategoryId = (int)category;
            existing.CategoryName = CommodityCategoryHelper.GetDisplayName(category);
            existing.UpdatedAt = timestamp;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}