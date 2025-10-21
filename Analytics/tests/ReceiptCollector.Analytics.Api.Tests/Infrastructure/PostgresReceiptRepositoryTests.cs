using Microsoft.EntityFrameworkCore;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Receipts;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using System.Linq;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

public sealed class PostgresReceiptRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public PostgresReceiptRepositoryTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    [Fact]
    public async Task Add_and_get_receipt_returns_receipt_with_items()
    {
        await ClearDatabaseAsync();

        var receipt = CreateReceipt();

        await using (var context = CreateContext())
        {
            await context.Merchants.AddAsync(CreateMerchant(receipt.MerchantId));
            var repository = new ReceiptRepository(context);
            await repository.AddAsync(receipt, CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var repository = new ReceiptRepository(context);
            var stored = await repository.GetByIdAsync(receipt.Id, receipt.UserId, CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal(receipt.Id, stored!.Id);
            Assert.Equal(receipt.UserId, stored.UserId);
            Assert.Equal(receipt.MerchantId, stored.MerchantId);
            Assert.Equal(receipt.TotalAmount, stored.TotalAmount);
            Assert.Equal(receipt.PurchasedAt, stored.PurchasedAt, TimeSpan.FromMilliseconds(1));

            var expectedItems = receipt.Items.OrderBy(i => i.Id).ToList();
            var actualItems = stored.Items.OrderBy(i => i.Id).ToList();

            Assert.Equal(expectedItems.Count, actualItems.Count);

            for (var index = 0; index < expectedItems.Count; index++)
            {
                AssertSubset(expectedItems[index], actualItems[index]);
            }
        }
    }

    [Fact]
    public async Task Delete_removes_receipt()
    {
        await ClearDatabaseAsync();

        var receipt = CreateReceipt();

        await using (var context = CreateContext())
        {
            await context.Merchants.AddAsync(CreateMerchant(receipt.MerchantId));
            var repository = new ReceiptRepository(context);
            await repository.AddAsync(receipt, CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var repository = new ReceiptRepository(context);
            await repository.DeleteAsync(receipt.Id, receipt.UserId, CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var repository = new ReceiptRepository(context);
            var stored = await repository.GetByIdAsync(receipt.Id, receipt.UserId, CancellationToken.None);
            Assert.Null(stored);
        }
    }

    [Fact]
    public async Task Get_with_different_user_returns_null()
    {
        await ClearDatabaseAsync();

        var receipt = CreateReceipt();

        await using (var context = CreateContext())
        {
            await context.Merchants.AddAsync(CreateMerchant(receipt.MerchantId));
            var repository = new ReceiptRepository(context);
            await repository.AddAsync(receipt, CancellationToken.None);
        }

        var otherUserId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            var repository = new ReceiptRepository(context);
            var stored = await repository.GetByIdAsync(receipt.Id, otherUserId, CancellationToken.None);
            Assert.Null(stored);
        }
    }

    [Fact]
    public async Task Delete_with_different_user_does_not_remove_receipt()
    {
        await ClearDatabaseAsync();

        var receipt = CreateReceipt();

        await using (var context = CreateContext())
        {
            await context.Merchants.AddAsync(CreateMerchant(receipt.MerchantId));
            var repository = new ReceiptRepository(context);
            await repository.AddAsync(receipt, CancellationToken.None);
        }

        var otherUserId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            var repository = new ReceiptRepository(context);
            await repository.DeleteAsync(receipt.Id, otherUserId, CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var repository = new ReceiptRepository(context);
            var stored = await repository.GetByIdAsync(receipt.Id, receipt.UserId, CancellationToken.None);
            Assert.NotNull(stored);
        }
    }

    public async Task InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    private ReceiptDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ReceiptDbContext>()
            .UseNpgsql(_connectionString);

        return new ReceiptDbContext(builder.Options);
    }

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.Commodities.ExecuteDeleteAsync();
        await context.Receipts.ExecuteDeleteAsync();
        await context.Merchants.ExecuteDeleteAsync();
    }

    private static Receipt CreateReceipt()
    {
        var receiptId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var purchasedAt = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

        var category = new Category(1, "Groceries");
        var firstItem = new Commodity(
            Guid.NewGuid(),
            receiptId,
            "Milk",
            2,
            60,
            10,
            12,
            category);

        var secondItem = new Commodity(
            Guid.NewGuid(),
            receiptId,
            "Bread",
            1,
            40,
            10,
            4);

        return new Receipt(
            receiptId,
            userId,
            merchantId,
            172,
            purchasedAt,
            "external-id",
            new[] { firstItem, secondItem });
    }

    private static MerchantEntity CreateMerchant(Guid merchantId)
    {
        return new MerchantEntity
        {
            Id = merchantId,
            Name = "Local Store",
            Category = MerchantCategory.Undefined
        };
    }

    private static void AssertSubset(Commodity expected, Commodity actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Quantity, actual.Quantity);
        Assert.Equal(expected.UnitPrice, actual.UnitPrice);
        Assert.Equal(expected.Nds, actual.Nds);
        Assert.Equal(expected.NdsSum, actual.NdsSum);

        if (expected.Category is null)
        {
            Assert.Null(actual.Category);
        }
        else
        {
            Assert.NotNull(actual.Category);
            Assert.Equal(expected.Category.Id, actual.Category!.Id);
            Assert.Equal(expected.Category.Name, actual.Category.Name);
        }
    }
}
