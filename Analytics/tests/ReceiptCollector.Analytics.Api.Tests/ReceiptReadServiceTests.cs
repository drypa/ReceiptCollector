using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Application.Modules.Receipts.Contracts;
using ReceiptCollector.Analytics.Infrastructure.Modules.Receipts;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;

namespace ReceiptCollector.Analytics.Api.Tests;

public class ReceiptReadServiceTests
{
    [Fact]
    public async Task GetRecentAsync_returns_empty_when_no_receipts()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ReceiptDbContext(options);
        var service = new ReceiptReadService(context);

        var summary = await service.GetRecentAsync(Guid.NewGuid(), 10, CancellationToken.None);

        Assert.Empty(summary);
    }

    [Fact]
    public async Task Service_returns_receipt_details_for_existing_receipt()
    {
        var options = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new ReceiptDbContext(options);

        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();

        var entity = new ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.ReceiptEntity
        {
            Id = receiptId,
            UserId = userId,
            Merchant = "Local Store",
            ExternalId = "external",
            TotalAmount = 100m,
            PurchasedAt = DateTime.UtcNow,
            Items =
            [
                new ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.CommodityEntity
                {
                    Id = Guid.NewGuid(),
                    ReceiptId = receiptId,
                    Name = "Milk",
                    Quantity = 1,
                    UnitPrice = 100,
                    Nds = 0,
                    NdsSum = 0
                }
            ]
        };

        await context.Receipts.AddAsync(entity);
        await context.SaveChangesAsync();

        var service = new ReceiptReadService(context);

        var summaries = await service.GetRecentAsync(userId, 10, CancellationToken.None);
        Assert.Single(summaries);

        var details = await service.GetByIdAsync(receiptId, CancellationToken.None);
        Assert.NotNull(details);
        Assert.Equal("Local Store", details!.Merchant);
        Assert.Single(details.Items);
    }
}