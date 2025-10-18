using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
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
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == receipt.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            var entity = MapToEntity(receipt);
            await _dbContext.Receipts.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            //TODO: what to do?
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

        return entity is null ? null : MapToDomain(entity);
    }

    private static ReceiptEntity MapToEntity(Receipt receipt)
    {
        return new ReceiptEntity
        {
            Id = receipt.Id,
            UserId = receipt.UserId,
            Merchant = receipt.Merchant,
            TotalAmount = receipt.TotalAmount,
            PurchasedAt = receipt.PurchasedAt,
            Items = receipt.Items.Select(MapToEntity).ToList()
        };
    }

    private static CommodityEntity MapToEntity(Commodity commodity)
    {
        return new CommodityEntity
        {
            Id = commodity.Id,
            ReceiptId = commodity.ReceiptId,
            Name = commodity.Name,
            Quantity = commodity.Quantity,
            UnitPrice = commodity.UnitPrice,
            Nds = commodity.Nds,
            NdsSum = commodity.NdsSum,
            CategoryId = commodity.Category?.Id,
            CategoryName = commodity.Category?.Name ?? null
        };
    }

    private static Receipt MapToDomain(ReceiptEntity entity)
    {
        var items = entity.Items.Select(item =>
        {
            Category? category = item.CategoryId.HasValue && item.CategoryName is not null
                ? new Category(item.CategoryId.Value, item.CategoryName)
                : null;

            return new Commodity(
                item.Id,
                entity.Id,
                item.Name,
                item.Quantity,
                item.UnitPrice,
                item.Nds,
                item.NdsSum,
                category);
        }).ToList();

        return new Receipt(
            entity.Id,
            entity.UserId,
            entity.Merchant,
            entity.TotalAmount,
            entity.PurchasedAt,
            items);
    }
}
