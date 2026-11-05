using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ReceiptCollector.Analytics.Application.Modules.Commodities.Contracts;
using ReceiptCollector.Analytics.Domain.Modules.Commodities;
using ReceiptCollector.Analytics.Domain.Modules.Merchants;
using ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres;
using Testcontainers.PostgreSql;

namespace ReceiptCollector.Analytics.Api.Tests.Infrastructure;

/// <summary>
/// Тесты операций применения дефолтной категории магазина (T-04, ADR-032 пп. 3–4, E1/F1):
/// <see cref="CommodityRepository.ApplyMerchantDefaultForReceiptAsync"/> и
/// <see cref="CommodityRepository.ApplyMerchantDefaultForMerchantAsync"/>.
/// <para>
/// Схема — Testcontainers Postgres (прецедент <see cref="PrecategorizedCommodityRepositoryTests"/>,
/// того же эталонного метода ConfirmAsync): обе операции используют <c>ExecuteUpdateAsync</c>,
/// который EF InMemory не поддерживает (релационный API), поэтому InMemory здесь неприменим.
/// Кэш — подмененный NSubstitute <see cref="ICommodityCategoryCache"/>.
/// </para>
/// </summary>
public sealed class CommodityRepositoryMerchantDefaultTests : IAsyncLifetime
{
    /// <summary>Единственное persisted-значение источника дожима (ADR-032, B1) — контракт БД.</summary>
    private const string MerchantDefaultSource = "merchant-default";

    private readonly PostgreSqlContainer _postgresContainer;
    private string _connectionString = string.Empty;

    public CommodityRepositoryMerchantDefaultTests()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithCleanUp(true)
            .Build();
    }

    /// <summary>Позиция для seeding: наименование, сохраненный код категории и источник.</summary>
    private sealed record Item(string Name, int? CategoryId, string? CategorySource = null);

    [Fact]
    public async Task ApplyForReceipt_UpdatesOnlyUncategorizedItemsAndMarksSource()
    {
        await ClearDatabaseAsync();

        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var otherReceiptId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Merchants.Add(CreateMerchant(merchantId));
            await context.Receipts.AddRangeAsync(
                CreateReceipt(receiptId, userId, merchantId,
                    new Item("Молоко", null),                                 // без категории — обновится
                    new Item("Хлеб", 0),                                      // легаси Undefined — обновится
                    new Item("Сыр", (int)CommodityCategory.Dairy),            // ручная — не трогать
                    new Item("Кефир", (int)CommodityCategory.Food, "precategorized")), // «AI-категория» с источником — не трогать
                CreateReceipt(otherReceiptId, userId, merchantId,
                    new Item("Масло", null)));                                // другой чек — не трогать
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);
            var updated = await repository.ApplyMerchantDefaultForReceiptAsync(
                receiptId, CommodityCategory.ReadyMeals, CancellationToken.None);

            Assert.Equal(2, updated);
        }

        await using (var context = CreateContext())
        {
            var rows = await context.Commodities
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Name, c => new { c.CategoryId, c.CategoryName, c.CategorySource });

            var expectedName = CommodityCategoryHelper.GetDisplayName(CommodityCategory.ReadyMeals);
            foreach (var name in new[] { "Молоко", "Хлеб" })
            {
                Assert.Equal((int)CommodityCategory.ReadyMeals, rows[name].CategoryId);
                Assert.Equal(expectedName, rows[name].CategoryName);
                Assert.Equal(MerchantDefaultSource, rows[name].CategorySource);
            }

            // FR-M5: содержательные категории не перезаписаны, источник не проставлен.
            Assert.Equal((int)CommodityCategory.Dairy, rows["Сыр"].CategoryId);
            Assert.Null(rows["Сыр"].CategorySource);
            Assert.Equal((int)CommodityCategory.Food, rows["Кефир"].CategoryId);
            Assert.Equal("precategorized", rows["Кефир"].CategorySource);

            // Операция целится ровно в один чек (C1).
            Assert.Null(rows["Масло"].CategoryId);
            Assert.Null(rows["Масло"].CategorySource);
        }
    }

    [Fact]
    public async Task ApplyForMerchant_UpdatesItemsOfAllUsers_AndSkipsOtherMerchants()
    {
        await ClearDatabaseAsync();

        var merchantId = Guid.NewGuid();
        var otherMerchantId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Merchants.AddRange(CreateMerchant(merchantId), CreateMerchant(otherMerchantId));
            await context.Receipts.AddRangeAsync(
                CreateReceipt(Guid.NewGuid(), userA, merchantId,
                    new Item("Молоко", null),
                    new Item("Сыр", (int)CommodityCategory.Dairy)),
                CreateReceipt(Guid.NewGuid(), userB, merchantId,
                    new Item("Молоко", 0)),
                CreateReceipt(Guid.NewGuid(), userA, otherMerchantId,
                    new Item("Молоко", null)));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            // E1: операция вне owner-scoping — фильтр пользователя выставлен и не должен мешать
            // (без IgnoreQueryFilters позиции userB остались бы нетронутыми).
            context.SetCurrentUser(userA);

            var repository = CreateRepository(context);
            var updated = await repository.ApplyMerchantDefaultForMerchantAsync(
                merchantId, CommodityCategory.ReadyMeals, CancellationToken.None);

            Assert.Equal(2, updated);
        }

        await using (var context = CreateContext())
        {
            var milk = await context.Commodities.AsNoTracking().Where(c => c.Name == "Молоко").ToListAsync();

            // Позиции обоих пользователей одного магазина дожаты (E1).
            Assert.Equal(3, milk.Count);
            Assert.Equal(
                2,
                milk.Count(c => c.CategoryId == (int)CommodityCategory.ReadyMeals
                                && c.CategorySource == MerchantDefaultSource));

            // Магазин другой — позиция того же пользователя без категории.
            Assert.Single(milk, c => c.CategoryId == null && c.CategorySource == null);

            var cheese = Assert.Single(
                await context.Commodities.AsNoTracking().Where(c => c.Name == "Сыр").ToListAsync());
            Assert.Equal((int)CommodityCategory.Dairy, cheese.CategoryId);
            Assert.Null(cheese.CategorySource);
        }
    }

    [Fact]
    public async Task ApplyMerchantDefault_IsIdempotent_SecondCallReturnsZeroAndKeepsSource()
    {
        await ClearDatabaseAsync();

        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Merchants.Add(CreateMerchant(merchantId));
            await context.Receipts.AddAsync(CreateReceipt(receiptId, userId, merchantId,
                new Item("Молоко", null),
                new Item("Сыр", (int)CommodityCategory.Dairy)));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            Assert.Equal(1, await repository.ApplyMerchantDefaultForReceiptAsync(
                receiptId, CommodityCategory.ReadyMeals, CancellationToken.None));

            // Повторный вызов — 0 строк (идемпотентность дожима, edge case «повторное нажатие»).
            Assert.Equal(0, await repository.ApplyMerchantDefaultForReceiptAsync(
                receiptId, CommodityCategory.ReadyMeals, CancellationToken.None));
            Assert.Equal(0, await repository.ApplyMerchantDefaultForMerchantAsync(
                merchantId, CommodityCategory.ReadyMeals, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var rows = await context.Commodities
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Name, c => new { c.CategoryId, c.CategoryName, c.CategorySource });

            // Повторы не переписали CategorySource/CategoryName.
            Assert.Equal((int)CommodityCategory.ReadyMeals, rows["Молоко"].CategoryId);
            Assert.Equal(
                CommodityCategoryHelper.GetDisplayName(CommodityCategory.ReadyMeals),
                rows["Молоко"].CategoryName);
            Assert.Equal(MerchantDefaultSource, rows["Молоко"].CategorySource);

            Assert.Equal((int)CommodityCategory.Dairy, rows["Сыр"].CategoryId);
            Assert.Null(rows["Сыр"].CategorySource);
        }
    }

    [Fact]
    public async Task ApplyForReceipt_PopulatesCacheWithNormalizedNamesOnlyAfterCommit()
    {
        await ClearDatabaseAsync();

        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Merchants.Add(CreateMerchant(merchantId));
            await context.Receipts.AddAsync(CreateReceipt(receiptId, userId, merchantId,
                new Item("Молоко", null),
                new Item("  МОЛОКО  ", null),   // то же нормализованное имя (первый-wins на стороне кэша)
                new Item("Хлеб", null)));
            await context.SaveChangesAsync();
        }

        // Проверка порядка «запись → кэш» (F1, amend ADR-032): внутри TryAdd читаем состояние БД
        // ВТОРЫМ подключением. До commit транзакции репозитория второй коннект по MVCC увидел бы
        // позиции без категории (0 строк с маркером); после commit — все три.
        var sawCommittedRowsAtTryAdd = new List<bool>();
        var cache = Substitute.For<ICommodityCategoryCache>();
        cache
            .When(x => x.TryAdd(Arg.Any<string>(), Arg.Any<CommodityCategory>()))
            .Do(_ =>
            {
                using var probe = CreateContext();
                sawCommittedRowsAtTryAdd.Add(
                    probe.Commodities.AsNoTracking().Count(c => c.CategorySource == MerchantDefaultSource) == 3);
            });

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context, cache);
            var updated = await repository.ApplyMerchantDefaultForReceiptAsync(
                receiptId, CommodityCategory.ReadyMeals, CancellationToken.None);

            Assert.Equal(3, updated);
        }

        // TryAdd по нормализованным именам обновленных строк; дубль «МОЛОКО» дает второй вызов
        // с тем же ключом — first-wins/no-op обеспечивает сам кэш (ADR 019 FR-1.4).
        cache.Received(2).TryAdd("молоко", CommodityCategory.ReadyMeals);
        cache.Received(1).TryAdd("хлеб", CommodityCategory.ReadyMeals);
        Assert.Equal(3, sawCommittedRowsAtTryAdd.Count);
        Assert.All(sawCommittedRowsAtTryAdd, committed => Assert.True(committed));
    }

    [Fact]
    public async Task ApplyMerchantDefault_WhenNothingUpdated_DoesNotTouchCache()
    {
        await ClearDatabaseAsync();

        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Merchants.Add(CreateMerchant(merchantId));
            await context.Receipts.AddAsync(CreateReceipt(receiptId, userId, merchantId,
                new Item("Сыр", (int)CommodityCategory.Dairy),
                new Item("Кефир", (int)CommodityCategory.Food, MerchantDefaultSource)));
            await context.SaveChangesAsync();
        }

        var cache = Substitute.For<ICommodityCategoryCache>();

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context, cache);

            // Позиции с содержательной категорией — предикат не матчит ничего.
            Assert.Equal(0, await repository.ApplyMerchantDefaultForReceiptAsync(
                receiptId, CommodityCategory.ReadyMeals, CancellationToken.None));
            Assert.Equal(0, await repository.ApplyMerchantDefaultForMerchantAsync(
                merchantId, CommodityCategory.ReadyMeals, CancellationToken.None));

            // Неизвестный receiptId — тоже 0.
            Assert.Equal(0, await repository.ApplyMerchantDefaultForReceiptAsync(
                Guid.NewGuid(), CommodityCategory.ReadyMeals, CancellationToken.None));
        }

        // При updatedCount = 0 — ни одного TryAdd (план T-04, ADR-032 п. 4).
        cache.DidNotReceiveWithAnyArgs().TryAdd(default!, default);
    }

    [Fact]
    public async Task ApplyMerchantDefault_RejectsUndefinedAndUnknownCategoryWithoutWrites()
    {
        await ClearDatabaseAsync();

        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.Merchants.Add(CreateMerchant(merchantId));
            await context.Receipts.AddAsync(CreateReceipt(receiptId, userId, merchantId,
                new Item("Молоко", null)));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            // A1: Undefined и коды вне справочника не могут быть применены как дефолт.
            await Assert.ThrowsAsync<ArgumentException>(() => repository.ApplyMerchantDefaultForReceiptAsync(
                receiptId, CommodityCategory.Undefined, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => repository.ApplyMerchantDefaultForMerchantAsync(
                merchantId, (CommodityCategory)9999, CancellationToken.None));
        }

        await using (var context = CreateContext())
        {
            var item = Assert.Single(await context.Commodities.AsNoTracking().ToListAsync());
            Assert.Null(item.CategoryId);
            Assert.Null(item.CategorySource);
        }
    }

    [Fact]
    public async Task ExistingCategoryUpdatePaths_DoNotWriteCategorySource()
    {
        await ClearDatabaseAsync();

        var merchantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var receipt = CreateReceipt(Guid.NewGuid(), userId, merchantId,
            new Item("Молоко", null),
            new Item("Хлеб", null));

        await using (var context = CreateContext())
        {
            context.Merchants.Add(CreateMerchant(merchantId));
            context.Receipts.Add(receipt);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = CreateRepository(context);

            await repository.UpdateCategoryAsync(
                receipt.Items[0].Id, CommodityCategory.Dairy, CancellationToken.None);
            await repository.UpdateCategoriesAsync(
                userId,
                new Dictionary<Guid, CommodityCategory> { [receipt.Items[1].Id] = CommodityCategory.Food },
                CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            // B1: существующие пути записи категории колонку не пишут — NULL = manual/legacy.
            var rows = await context.Commodities.AsNoTracking().ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.NotNull(row.CategoryId);
                Assert.Null(row.CategorySource);
            });
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
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention();

        return new ReceiptDbContext(builder.Options);
    }

    private static CommodityRepository CreateRepository(
        ReceiptDbContext context,
        ICommodityCategoryCache? cache = null)
        => new(context, cache);

    private async Task ClearDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await context.PrecategorizedCommodities.ExecuteDeleteAsync();
        await context.Commodities.ExecuteDeleteAsync();
        await context.Receipts.ExecuteDeleteAsync();
        await context.Merchants.ExecuteDeleteAsync();
    }

    private static MerchantEntity CreateMerchant(Guid id)
        => new() { Id = id, Name = $"Store {id:N}", Category = MerchantCategory.Undefined };

    private static ReceiptEntity CreateReceipt(Guid id, Guid userId, Guid merchantId, params Item[] items)
        => new()
        {
            Id = id,
            UserId = userId,
            MerchantId = merchantId,
            ExternalId = $"external-{id:N}",
            TotalAmount = 100m,
            PurchasedAt = DateTime.UtcNow,
            Items = items.Select(item => new CommodityEntity
            {
                Id = Guid.NewGuid(),
                ReceiptId = id,
                Name = item.Name,
                Quantity = 1m,
                UnitPrice = 10m,
                Nds = 0,
                NdsSum = 0m,
                CategoryId = item.CategoryId,
                CategoryName = item.CategoryId is null or 0
                    ? null
                    : CommodityCategoryHelper.GetDisplayName((CommodityCategory)item.CategoryId.Value),
                CategorySource = item.CategorySource
            }).ToList()
        };
}
