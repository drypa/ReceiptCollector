using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Models;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Infrastructure.Modules.Receipts;

internal sealed class ReceiptReadService : IReceiptReadService
{
    private readonly ReceiptDbContext _dbContext;

    public ReceiptReadService(ReceiptDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<IReadOnlyCollection<ReceiptSummaryDto>> GetRecentAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        return await _dbContext.Receipts
            .AsNoTracking()
            .Where(receipt => receipt.UserId == userId)
            .OrderByDescending(receipt => receipt.PurchasedAt)
            .Take(limit)
            .Select(receipt => new ReceiptSummaryDto(
                receipt.Id,
                receipt.Merchant,
                receipt.TotalAmount,
                receipt.PurchasedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ReceiptDetailsDto?> GetByIdAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.Receipts
            .AsNoTracking()
            .Include(receipt => receipt.Items)
            .FirstOrDefaultAsync(receipt => receipt.Id == receiptId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return null;
        }

        var items = entity.Items
            .OrderBy(item => item.Name)
            .Select(item => new ReceiptItemDto(
                item.Name,
                item.Quantity,
                item.UnitPrice,
                item.Quantity * item.UnitPrice,
                null))
            .ToList();

        return new ReceiptDetailsDto(
            entity.Id,
            entity.Merchant,
            entity.TotalAmount,
            entity.PurchasedAt,
            items);
    }
}
